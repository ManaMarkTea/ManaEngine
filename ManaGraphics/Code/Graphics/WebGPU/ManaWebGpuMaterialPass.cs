using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Draws every scene mesh with its material's shader: opaque first, then transparent back to front, then the
    /// always-on-top handles in a pass of their own against a separate depth buffer. Owns one pipeline per shader and
    /// blend/depth/cull combination, built the first time a mesh asks for it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuMaterialPass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuSceneBindings _bindings;
        private readonly TextureFormat _colorFormat;
        private readonly Dictionary<string, nint> _pipelines = new();
        private readonly Dictionary<string, nint> _shaderModules = new();

        public ManaWebGpuMaterialPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat)
            : base(gpu)
        {
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

        public override string ProfileName => "GPU: Material Pass";
        public override int PipelineCount => _pipelines.Count;
        public override int ShaderModuleCount => _shaderModules.Count;

        public override void DrawScene(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
            if (frame.Meshes.Count == 0)
            {
                return;
            }

            foreach (var sceneMesh in frame.Meshes)
            {
                if (!sceneMesh.Instance.IsTransparent && !sceneMesh.Instance.AlwaysOnTop)
                {
                    DrawMesh(pass, sceneMesh);
                }
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

        private void DrawMesh(RenderPassEncoder* pass, ManaWebGpuContext.ManaWebGpuSceneMesh sceneMesh)
        {
            // Only reaches the GPU when the mesh's transform or material changed since last frame.
            sceneMesh.TryUpdateObjectUniform(Api, Gpu.Queue, ManaWebGpuObjectUniform.For(sceneMesh.Instance));

            if (sceneMesh.CustomUniformBuffer != null)
            {
                ManaWebGpuCustomUniforms.Fill(sceneMesh.Instance, sceneMesh.CustomUniformLayout, sceneMesh.CustomUniformData);
                sceneMesh.TryUpdateCustomUniform(Api, Gpu.Queue, sceneMesh.CustomUniformData);
            }

            var instance = sceneMesh.Instance;
            var pipeline = GetOrCreatePipeline(
                instance.ShaderName,
                instance.IsTransparent,
                instance.DisableDepthWrite,
                // Only real alpha-blended materials (glTF glass etc) self-cull -- not the intrinsically
                // transparent shaders (water/liquid/lightning), which stay OpacityMode.Auto/Opaque at the
                // material level and want both faces for their own volumetric/surface effects.
                cullBackFaces: instance.OpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                alwaysOnTop: instance.AlwaysOnTop,
                additiveBlend: instance.OpacityMode == ManaOpacityMode.Additive);
            Api.RenderPassEncoderSetPipeline(pass, pipeline);

            // Group 0 is bound after every SetPipeline, not once per pass: switching to a pipeline whose layout is
            // incompatible with the bound one (the skybox's group 0 is {uniform, sampler, cube}) unsets it.
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindings.FrameGroup, 0, null);

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

        private RenderPipeline* GetOrCreatePipeline(
            string? shaderName, bool isTransparent = false, bool disableDepthWrite = false, bool cullBackFaces = false,
            bool alwaysOnTop = false, bool additiveBlend = false)
        {
            shaderName ??= "ManaPbrShader";

            var pipelineKey =
                $"{shaderName}_T{(isTransparent ? 1 : 0)}_D{(disableDepthWrite ? 1 : 0)}_C{(cullBackFaces ? 1 : 0)}_A{(alwaysOnTop ? 1 : 0)}_B{(additiveBlend ? 1 : 0)}";
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
