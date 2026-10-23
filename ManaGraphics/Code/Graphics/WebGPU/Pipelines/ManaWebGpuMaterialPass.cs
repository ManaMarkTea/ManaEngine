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
        private readonly List<(nint Pipeline, float Distance, ManaWebGpuSceneMesh Mesh)> _opaque = new();
        private RenderPipeline* _boundPipeline;
        private long _frameNumber;
        private readonly Dictionary<string, nint> _shaderModules = new();

        public ManaWebGpuMaterialPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat, ManaWebGpuSkyPass? sky = null)
            : base(gpu)
        {
            _sky = sky;
            _bindings = bindings;
            _colorFormat = colorFormat;
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

            // Opaque meshes grouped by pipeline (fewer state changes) and nearest first within each, so the depth test
            // turns away hidden pixels before their lighting runs rather than after.
            var eye = frame.Scene.Camera.Position;
            _opaque.Clear();
            foreach (var sceneMesh in frame.Meshes)
            {
                if (!sceneMesh.Instance.IsTransparent && !sceneMesh.Instance.AlwaysOnTop)
                {
                    _opaque.Add(((nint)PipelineFor(sceneMesh.Instance), Vector3.DistanceSquared(sceneMesh.Instance.WorldTransform.Translation, eye), sceneMesh));
                }
            }
            _opaque.Sort(static (a, b) =>
            {
                var byPipeline = a.Pipeline.CompareTo(b.Pipeline);
                if (byPipeline != 0)
                {
                    return byPipeline;
                }
                return a.Distance.CompareTo(b.Distance);
            });
            foreach (var (_, _, sceneMesh) in _opaque)
            {
                DrawMesh(pass, sceneMesh);
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

            if (sceneMesh.CustomUniformBuffer != null)
            {
                ManaWebGpuCustomUniforms.Fill(sceneMesh.Instance, sceneMesh.CustomUniformLayout, sceneMesh.CustomUniformData);
                sceneMesh.TryUpdateCustomUniform(sceneMesh.CustomUniformData);
            }

            var pipeline = PipelineFor(sceneMesh.Instance);
            if (pipeline != _boundPipeline)
            {
                Api.RenderPassEncoderSetPipeline(pass, pipeline);
                // Group 0 is bound after every pipeline change, not once per pass: switching to a pipeline whose layout
                // is incompatible with the bound one (the skybox's group 0 is {uniform, sampler, cube}) unsets it.
                Api.RenderPassEncoderSetBindGroup(pass, 0, _bindings.FrameGroup, 0, null);
                _boundPipeline = pipeline;
            }

            if (sceneMesh.ObjectBindGroup != null)
            {
                Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
            }

            if (sceneMesh.CustomBindGroup != null)
            {
                Api.RenderPassEncoderSetBindGroup(pass, 2, sceneMesh.CustomBindGroup, 0, null);
            }
            sceneMesh.Mesh.Draw(pass);
        }

        private RenderPipeline* PipelineFor(ManaRenderMeshInstance instance) =>
            GetOrCreatePipeline(
                instance.ShaderName,
                instance.IsTransparent,
                instance.DisableDepthWrite,
                // Only blended materials cull; water, liquid and lightning want both faces.
                cullBackFaces: instance.OpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                alwaysOnTop: instance.AlwaysOnTop,
                additiveBlend: instance.OpacityMode == ManaOpacityMode.Additive);

        private RenderPipeline* GetOrCreatePipeline(
            string? shaderName, bool isTransparent = false, bool disableDepthWrite = false, bool cullBackFaces = false,
            bool alwaysOnTop = false, bool additiveBlend = false)
        {
            shaderName ??= "ManaPbrShader";

            var flags = 0;
            if (isTransparent) { flags |= 1; }
            if (disableDepthWrite) { flags |= 2; }
            if (cullBackFaces) { flags |= 4; }
            if (alwaysOnTop) { flags |= 8; }
            if (additiveBlend) { flags |= 16; }
            var pipelineKey = (shaderName, flags);
            if (_pipelines.TryGetValue(pipelineKey, out var cachedPipeline))
            {
                return (RenderPipeline*)cachedPipeline;
            }

            // Shared with the inspector's schema cache, so the shader drawn and the one parsed for its custom
            // uniforms never drift apart.
            var shaderFile = ManaWebGpuCustomUniformSchemaCache.ResolveShaderFileNameOrDefault(shaderName);
            if (!_shaderModules.TryGetValue(shaderName, out var shaderModule))
            {
                try
                {
                    shaderModule = (nint)Gpu.CreateShaderModule(shaderFile);
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic($"Failed to load WGSL shader '{shaderFile}', falling back to default colored_mesh.wgsl. Error: {ex.Message}");
                    shaderFile = "colored_mesh.wgsl";
                    shaderModule = (nint)Gpu.CreateShaderModule(shaderFile);
                }

                _shaderModules[shaderName] = shaderModule;
            }

            // Every material pipeline has group 2 (custom uniforms) available, whether or not its shader declares it.
            var pipeline = ManaWebGpuMeshPipeline.Create(
                Gpu, (ShaderModule*)shaderModule, _colorFormat, _bindings.MaterialLayout, shaderName, isTransparent,
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
