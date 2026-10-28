using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// Draws every mesh with its material: opaque, then transparent back to front, then always-on-top handles against
    /// their own depth. One pipeline per shader and state combination, built on first use.
    /// </summary>
    internal sealed unsafe class ManaWebGpuMaterialPass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuSceneBindings _bindings;
        private readonly TextureFormat _colorFormat;
        // Keyed by shader and state flags: a string key was built (and thrown away) for every mesh every frame.
        private readonly Dictionary<(string Shader, int Flags), nint> _pipelines = new();
        private readonly List<(nint Pipeline, ulong Batch, float Distance, ManaWebGpuSceneMesh Mesh)> _opaque = new();
        // Runs of _opaque to draw: one mesh alone (Offset Alone), or many alike as one instanced draw.
        private readonly List<(int Start, int Count, uint Offset)> _runs = new();
        private readonly ManaWebGpuObjectUniform[] _records = new ManaWebGpuObjectUniform[ManaWebGpuSceneBindings.InstancesPerDraw];
        private readonly ManaWebGpuInstanceBuffer _instances;
        private RenderPipeline* _boundPipeline;
        private bool _customGroupBound;
        private long _frameNumber;
        private const uint Alone = uint.MaxValue;
        private readonly Dictionary<string, nint> _shaderModules = new();

        public ManaWebGpuMaterialPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat, ManaWebGpuSkyPass? sky = null)
            : base(gpu)
        {
            _sky = sky;
            _bindings = bindings;
            _colorFormat = colorFormat;
            _instances = new ManaWebGpuInstanceBuffer(gpu, sizeof(ManaWebGpuObjectUniform) * ManaWebGpuSceneBindings.InstancesPerDraw, "material");
            try
            {
                // The default material, compiled up front rather than on the first frame that draws a mesh.
                GetOrCreatePipeline("ManaPbrShader");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private readonly ManaWebGpuSkyPass? _sky;

        public override string ProfileName => "GPU: Material Pass";
        public override int PipelineCount => _pipelines.Count;
        public override int ShaderModuleCount => _shaderModules.Count;

        public override void DrawScene(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
            _frameNumber = frame.Number;
            _boundPipeline = null;

            // Opaque meshes grouped by pipeline (fewer state changes), then by what they look like -- meshes with the same
            // geometry and textures go out as one instanced draw -- and nearest first within each, so the depth test turns
            // away hidden pixels before their lighting runs rather than after.
            var eye = frame.Scene.Camera.Position;
            _opaque.Clear();
            foreach (var sceneMesh in frame.Meshes)
            {
                if (!sceneMesh.Instance.IsTransparent && !sceneMesh.Instance.AlwaysOnTop)
                {
                    _opaque.Add(((nint)PipelineFor(sceneMesh.Instance), BatchKey(sceneMesh),
                        Vector3.DistanceSquared(sceneMesh.Instance.WorldTransform.Translation, eye), sceneMesh));
                }
            }
            _opaque.Sort(static (a, b) =>
            {
                var byPipeline = a.Pipeline.CompareTo(b.Pipeline);
                if (byPipeline != 0)
                {
                    return byPipeline;
                }
                var byBatch = a.Batch.CompareTo(b.Batch);
                if (byBatch != 0)
                {
                    return byBatch;
                }
                return a.Distance.CompareTo(b.Distance);
            });

            // Gathered first and uploaded once, then drawn: the instance buffer may be remade larger while gathering,
            // and every draw has to bind the one that holds the data.
            _instances.Begin();
            _runs.Clear();
            var index = 0;
            while (index < _opaque.Count)
            {
                var (pipeline, batch, _, _) = _opaque[index];
                var end = index + 1;
                if (batch != 0)
                {
                    while (end < _opaque.Count && end - index < ManaWebGpuSceneBindings.InstancesPerDraw
                        && _opaque[end].Pipeline == pipeline && _opaque[end].Batch == batch)
                    {
                        end++;
                    }
                }

                var count = end - index;
                if (count < 2)
                {
                    _runs.Add((index, 1, Alone));
                    index = end;
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    _records[i] = _opaque[index + i].Mesh.ObjectUniform(_frameNumber);
                }
                _runs.Add((index, count, _instances.Append<ManaWebGpuObjectUniform>(_records.AsSpan(0, count))));
                index = end;
            }
            _instances.Upload();

            foreach (var (start, count, offset) in _runs)
            {
                if (offset == Alone)
                {
                    DrawMesh(pass, _opaque[start].Mesh);
                }
                else
                {
                    DrawInstanced(pass, _opaque[start].Mesh, (uint)count, offset);
                }
            }

            // Behind what is opaque (only uncovered pixels pass its depth test), under what is not: water and glass
            // write no depth, so they must blend over a sky that is already there.
            if (_sky != null)
            {
                _sky.DrawSky(frame, pass);
                // Back on a material layout before the next draw: after the sky's pipeline (its own group 0), wgpu-native
                // checked a later water draw's group 2 against a stale binding ("bound with size 16 where the shader
                // expects 304") and threw away the whole pass.
                var pbr = GetOrCreatePipeline("ManaPbrShader");
                Api.RenderPassEncoderSetPipeline(pass, pbr);
                Api.RenderPassEncoderSetBindGroup(pass, 0, _bindings.FrameGroup, 0, null);
                _boundPipeline = pbr;
                _customGroupBound = false;
            }
            if (frame.Meshes.Count == 0)
            {
                return;
            }

            var cameraPosition = frame.Scene.Camera.Position;
            foreach (var sceneMesh in frame.Meshes
                .Where(mesh => mesh.Instance.IsTransparent && !mesh.Instance.AlwaysOnTop)
                .OrderByDescending(mesh => Vector3.DistanceSquared(mesh.Instance.WorldTransform.Translation, cameraPosition)))
            {
                DrawMesh(pass, sceneMesh);
            }
        }

        // Handles keep the scene's colour but depth-test against one another, not the scene.
        public override void AfterScene(ManaWebGpuFrame frame)
        {
            if (frame.SceneSuspended || frame.HandleDepth == null || !frame.Meshes.Any(mesh => mesh.Instance.AlwaysOnTop))
            {
                return;
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = frame.SceneTarget,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store
            };
            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = frame.HandleDepth,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };
            var descriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthAttachment
            };

            var pass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &descriptor);
            _boundPipeline = null;
            try
            {
                foreach (var sceneMesh in frame.Meshes)
                {
                    if (sceneMesh.Instance.AlwaysOnTop)
                    {
                        DrawMesh(pass, sceneMesh);
                    }
                }
                Api.RenderPassEncoderEnd(pass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(pass);
            }
        }

        private void DrawMesh(RenderPassEncoder* pass, ManaWebGpuSceneMesh sceneMesh)
        {
            // Only reaches the GPU when the mesh's transform or material changed since last frame.
            sceneMesh.PrepareObjectUniform(_frameNumber);

            // Only a shader with custom uniforms reads its own group 2; any other just needs one bound for its layout.
            var ownCustomGroup = sceneMesh.HasCustomUniforms;
            if (sceneMesh.CustomUniformBuffer != null && ownCustomGroup)
            {
                ManaWebGpuCustomUniforms.Fill(sceneMesh.Instance, sceneMesh.CustomUniformLayout, sceneMesh.CustomUniformData);
                sceneMesh.TryUpdateCustomUniform(sceneMesh.CustomUniformData);
            }

            Bind(pass, PipelineFor(sceneMesh.Instance));

            if (sceneMesh.ObjectBindGroup != null)
            {
                Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
            }

            if (sceneMesh.CustomBindGroup != null && (ownCustomGroup || !_customGroupBound))
            {
                Api.RenderPassEncoderSetBindGroup(pass, 2, sceneMesh.CustomBindGroup, 0, null);
                _customGroupBound = true;
            }
            sceneMesh.Mesh.Draw(pass);
        }

        // Group 0 is bound after every pipeline change, not once per pass: switching to a pipeline whose layout is
        // incompatible with the bound one (the skybox's group 0 is {uniform, sampler, cube}) unsets it.
        private void Bind(RenderPassEncoder* pass, RenderPipeline* pipeline)
        {
            if (pipeline == _boundPipeline)
            {
                return;
            }
            Api.RenderPassEncoderSetPipeline(pass, pipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindings.FrameGroup, 0, null);
            _boundPipeline = pipeline;
            _customGroupBound = false;
        }

        // Many meshes alike in one draw: the first one's geometry and textures, each one's own object uniform.
        private void DrawInstanced(RenderPassEncoder* pass, ManaWebGpuSceneMesh first, uint count, uint offset)
        {
            Bind(pass, InstancedPipelineFor(first.Instance));
            var group = first.InstancedBindGroup(_bindings.InstancedObjectLayout, _instances.Buffer);
            Api.RenderPassEncoderSetBindGroup(pass, 1, group, 1, &offset);
            first.Mesh.DrawInstanced(pass, count);
        }

        /// <summary>
        /// What a mesh can be batched by -- its geometry and its textures -- or 0 when it cannot: only the PBR shader has
        /// an instanced build, and a shader with custom uniforms has per-mesh data the instance array does not carry.
        /// </summary>
        private static ulong BatchKey(ManaWebGpuSceneMesh mesh)
        {
            var shader = mesh.Instance.ShaderName;
            if ((shader != null && shader != "ManaPbrShader") || mesh.HasCustomUniforms || mesh.ObjectEntries.Length == 0
                || mesh.Instance.TerrainLayers)
            {
                return 0;
            }
            var key = (mesh.Instance.MeshData.GeometryHash * 1099511628211UL) ^ mesh.TextureKey;
            if (key == 0)
            {
                key = 1;
            }
            return key;
        }

        private RenderPipeline* InstancedPipelineFor(ManaRenderMeshInstance instance) =>
            GetOrCreatePipeline(
                instance.ShaderName,
                cullBackFaces: instance.OpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                instanced: true);

        private RenderPipeline* PipelineFor(ManaRenderMeshInstance instance) =>
            GetOrCreatePipeline(
                instance.ShaderName,
                instance.IsTransparent,
                instance.DisableDepthWrite,
                // Only blended materials cull; water, liquid and lightning want both faces.
                cullBackFaces: instance.OpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                alwaysOnTop: instance.AlwaysOnTop,
                additiveBlend: instance.OpacityMode == ManaOpacityMode.Additive,
                terrain: instance.TerrainLayers);

        private RenderPipeline* GetOrCreatePipeline(
            string? shaderName, bool isTransparent = false, bool disableDepthWrite = false, bool cullBackFaces = false,
            bool alwaysOnTop = false, bool additiveBlend = false, bool instanced = false, bool terrain = false)
        {
            shaderName ??= "ManaPbrShader";

            var flags = 0;
            if (isTransparent) { flags |= 1; }
            if (disableDepthWrite) { flags |= 2; }
            if (cullBackFaces) { flags |= 4; }
            if (alwaysOnTop) { flags |= 8; }
            if (additiveBlend) { flags |= 16; }
            if (instanced) { flags |= 32; }
            if (terrain) { flags |= 64; }
            var pipelineKey = (shaderName, flags);
            if (_pipelines.TryGetValue(pipelineKey, out var cachedPipeline))
            {
                return (RenderPipeline*)cachedPipeline;
            }

            // Shared with the inspector's schema cache, so the shader drawn and the one parsed for its custom
            // uniforms never drift apart.
            var shaderFile = ManaWebGpuCustomUniformSchemaCache.ResolveShaderFileNameOrDefault(shaderName);
            // The instanced build is the same file with MANA_INSTANCED defined: a module of its own.
            var moduleKey = shaderName;
            string[] defines = [];
            if (instanced)
            {
                moduleKey = shaderName + "#instanced";
                defines = ["MANA_INSTANCED"];
            }
            // A textured terrain's build reads its layer atlas (pbr_irradiance.wgsl), never instanced.
            if (terrain)
            {
                moduleKey = shaderName + "#terrain";
                defines = ["MANA_TERRAIN"];
            }
            if (!_shaderModules.TryGetValue(moduleKey, out var shaderModule))
            {
                try
                {
                    shaderModule = (nint)Gpu.CreateShaderModule(shaderFile, defines);
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic($"Failed to load WGSL shader '{shaderFile}', falling back to default colored_mesh.wgsl. Error: {ex.Message}");
                    shaderFile = "colored_mesh.wgsl";
                    shaderModule = (nint)Gpu.CreateShaderModule(shaderFile);
                }

                _shaderModules[moduleKey] = shaderModule;
            }

            // Every material pipeline has group 2 (custom uniforms) available, whether or not its shader declares it;
            // an instanced one has its object group as an array instead, and no group 2.
            var layout = _bindings.MaterialLayout;
            if (instanced)
            {
                layout = _bindings.InstancedMaterialLayout;
            }
            var pipeline = ManaWebGpuMeshPipeline.Create(
                Gpu, (ShaderModule*)shaderModule, _colorFormat, layout, shaderName, isTransparent,
                alwaysOnTop ? false : disableDepthWrite,
                depthCompare: CompareFunction.Less,
                cullBackFaces: cullBackFaces,
                additiveBlend: additiveBlend);
            _pipelines[pipelineKey] = (nint)pipeline;

            ManaConsole.WriteDiagnostic($"WebGPU render pipeline for '{shaderName}' created successfully using '{shaderFile}'.");
            return pipeline;
        }

        public override void Dispose()
        {
            _instances.Dispose();
            foreach (var pipeline in _pipelines.Values)
            {
                Api.RenderPipelineRelease((RenderPipeline*)pipeline);
            }
            _pipelines.Clear();

            foreach (var shaderModule in _shaderModules.Values)
            {
                Api.ShaderModuleRelease((ShaderModule*)shaderModule);
            }
            _shaderModules.Clear();
        }
    }
}
