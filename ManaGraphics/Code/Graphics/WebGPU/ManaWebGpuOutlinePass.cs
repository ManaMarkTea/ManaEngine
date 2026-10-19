using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Draws every outlined mesh into the HDR post-processor's outline mask, which the tone map then edges. Depth-tested
    /// against what the scene pass left, so a highlighted mesh only shows where nothing is in front of it; handles test
    /// against their own depth buffer instead.
    /// </summary>
    internal sealed unsafe class ManaWebGpuOutlinePass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuSceneBindings _bindings;
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;

        public ManaWebGpuOutlinePass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings)
            : base(gpu)
        {
            _bindings = bindings;
        }

        public override string ProfileName => "GPU: Outline Mask";
        public override int PipelineCount => _pipeline != null ? 1 : 0;
        public override int ShaderModuleCount => _shaderModule != null ? 1 : 0;

        public override void AfterScene(ManaWebGpuFrame frame)
        {
            if (frame.OutlineMask == null || frame.Depth == null)
            {
                return;
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = frame.OutlineMask,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 0.0 }
            };

            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = frame.Depth,
                DepthLoadOp = LoadOp.Undefined,
                DepthStoreOp = StoreOp.Undefined,
                DepthClearValue = 0.0f,
                DepthReadOnly = true,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilReadOnly = true
            };

            var descriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthAttachment
            };

            var pipeline = GetOrCreatePipeline();
            var renderPass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &descriptor);
            try
            {
                // The frame and object groups the scene pass already filled; nothing to upload here.
                Api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                Api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindings.FrameGroup, 0, null);
                DrawOutlined(renderPass, frame, alwaysOnTop: false);

                if (frame.Meshes.Any(mesh => mesh.Instance.OutlineEnabled && mesh.Instance.AlwaysOnTop))
                {
                    Api.RenderPassEncoderEnd(renderPass);
                    Api.RenderPassEncoderRelease(renderPass);
                    colorAttachment.LoadOp = LoadOp.Load;
                    depthAttachment.View = frame.HandleDepth;
                    renderPass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &descriptor);
                    Api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                    Api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindings.FrameGroup, 0, null);
                    DrawOutlined(renderPass, frame, alwaysOnTop: true);
                }

                Api.RenderPassEncoderEnd(renderPass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(renderPass);
            }
        }

        private void DrawOutlined(RenderPassEncoder* renderPass, ManaWebGpuFrame frame, bool alwaysOnTop)
        {
            foreach (var sceneMesh in frame.Meshes)
            {
                if (!sceneMesh.Instance.OutlineEnabled || sceneMesh.Instance.AlwaysOnTop != alwaysOnTop)
                {
                    continue;
                }

                Api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
                sceneMesh.Mesh.Draw(renderPass);
            }
        }

        private RenderPipeline* GetOrCreatePipeline()
        {
            if (_pipeline != null)
            {
                return _pipeline;
            }

            _shaderModule = Gpu.CreateShaderModule("outline_mask.wgsl");
            _pipeline = ManaWebGpuMeshPipeline.Create(
                Gpu,
                _shaderModule,
                TextureFormat.Rgba8Unorm,
                _bindings.MeshLayout,
                "OutlineMask",
                disableDepthWrite: true,
                depthCompare: CompareFunction.LessEqual);
            ManaConsole.WriteDiagnostic("WebGPU outline-mask pipeline created successfully (LessEqual).");
            return _pipeline;
        }

        public override void Dispose()
        {
            if (_pipeline != null)
            {
                Api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }
    }
}
