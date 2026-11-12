using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// For each post effect that covers only some meshes this frame (<see cref="ManaRenderScene.ScopedPostEffects"/>), a
    /// mask of the pixels where those meshes are what is seen -- depth-tested against the scene, as the outline mask is --
    /// for the effect to read (<see cref="MaskView"/>).
    /// </summary>
    internal sealed unsafe class ManaWebGpuPostEffectMaskPass : ManaWebGpuRenderPass
    {
        private const TextureFormat MaskFormat = TextureFormat.R8Unorm;
        private readonly ManaWebGpuSceneBindings _bindings;
        private readonly Texture*[] _masks = new Texture*[ManaPostEffects.SlotCount];
        private readonly TextureView*[] _maskViews = new TextureView*[ManaPostEffects.SlotCount];
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;
        private uint _width;
        private uint _height;
        private byte _drawn;

        public ManaWebGpuPostEffectMaskPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings)
            : base(gpu)
        {
            _bindings = bindings;
        }

        public override string ProfileName => "GPU: Post Effect Masks";
        public override int PipelineCount => _pipeline != null ? 1 : 0;
        public override int ShaderModuleCount => _shaderModule != null ? 1 : 0;

        /// <summary>This frame's mask for an effect's slot, or null when that effect covers the whole screen (or is off).</summary>
        public TextureView* MaskView(int slot)
        {
            if (!ManaPostEffects.Has(_drawn, slot))
            {
                return null;
            }
            return _maskViews[slot];
        }

        public override void AfterScene(ManaWebGpuFrame frame)
        {
            _drawn = 0;
            var scoped = frame.Scene.ScopedPostEffects;
            if (scoped == 0 || frame.SceneSuspended || frame.Depth == null || frame.Width == 0 || frame.Height == 0)
            {
                return;
            }
            if (frame.Width != _width || frame.Height != _height)
            {
                ReleaseMasks();
                _width = frame.Width;
                _height = frame.Height;
            }
            for (var slot = 0; slot < ManaPostEffects.SlotCount; slot++)
            {
                if (ManaPostEffects.Has(scoped, slot))
                {
                    Draw(frame, slot);
                    _drawn |= ManaPostEffects.Bit(slot);
                }
            }
        }

        private void Draw(ManaWebGpuFrame frame, int slot)
        {
            var colorAttachment = new RenderPassColorAttachment
            {
                View = EnsureMask(slot),
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 0.0 }
            };
            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = frame.Depth,
                DepthLoadOp = LoadOp.Undefined,
                DepthStoreOp = StoreOp.Undefined,
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
                Api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                Api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindings.FrameGroup, 0, null);
                foreach (var sceneMesh in frame.Meshes)
                {
                    if (!ManaPostEffects.Has(sceneMesh.Instance.PostEffects, slot) || sceneMesh.Instance.AlwaysOnTop)
                    {
                        continue;
                    }
                    // Instanced draws never fill a mesh's own uniform; drawn alone, it needs this frame's.
                    sceneMesh.PrepareObjectUniform(frame.Number);
                    Api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
                    sceneMesh.Mesh.Draw(renderPass);
                }
                Api.RenderPassEncoderEnd(renderPass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(renderPass);
            }
        }

        private TextureView* EnsureMask(int slot)
        {
            if (_maskViews[slot] != null)
            {
                return _maskViews[slot];
            }
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = _width, Height = _height, DepthOrArrayLayers = 1 },
                Format = MaskFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _masks[slot] = Api.DeviceCreateTexture(Device, &descriptor);
            _maskViews[slot] = Api.TextureCreateView(_masks[slot], null);
            if (_masks[slot] == null || _maskViews[slot] == null)
            {
                throw new InvalidOperationException("Failed to create a post effect mask.");
            }
            return _maskViews[slot];
        }

        private RenderPipeline* GetOrCreatePipeline()
        {
            if (_pipeline != null)
            {
                return _pipeline;
            }
            _shaderModule = Gpu.CreateShaderModule("post_effect_mask.wgsl");
            _pipeline = ManaWebGpuMeshPipeline.Create(
                Gpu,
                _shaderModule,
                MaskFormat,
                _bindings.MeshLayout,
                "PostEffectMask",
                disableDepthWrite: true,
                depthCompare: CompareFunction.LessEqual);
            ManaConsole.WriteDiagnostic("WebGPU post-effect mask pipeline created.");
            return _pipeline;
        }

        private void ReleaseMasks()
        {
            for (var slot = 0; slot < ManaPostEffects.SlotCount; slot++)
            {
                if (_maskViews[slot] != null) { Api.TextureViewRelease(_maskViews[slot]); _maskViews[slot] = null; }
                if (_masks[slot] != null) { Api.TextureRelease(_masks[slot]); _masks[slot] = null; }
            }
            _drawn = 0;
        }

        public override void Dispose()
        {
            ReleaseMasks();
            if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
            if (_shaderModule != null) { Api.ShaderModuleRelease(_shaderModule); _shaderModule = null; }
        }
    }
}
