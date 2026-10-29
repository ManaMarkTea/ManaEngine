using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The scene pass's four-sample colour and depth under MSAA. The colour resolves into the HDR target as the pass
    /// ends; depth cannot be resolved by WebGPU, so <see cref="ResolveDepth"/> copies its first sample into the surface's
    /// ordinary depth for everything drawn after (outlines, always-on-top handles, the target preview).
    /// </summary>
    internal sealed unsafe class ManaWebGpuMsaaTargets : ManaWebGpuDeviceObject
    {
        public const uint SampleCount = 4;

        private readonly TextureFormat _colorFormat;
        private readonly TextureFormat _depthFormat;
        private Texture* _color;
        private Texture* _depth;
        private BindGroupLayout* _layout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private BindGroup* _group;
        private uint _width;
        private uint _height;

        public ManaWebGpuMsaaTargets(ManaWebGpuDevice gpu, TextureFormat colorFormat, TextureFormat depthFormat)
            : base(gpu)
        {
            _colorFormat = colorFormat;
            _depthFormat = depthFormat;
            try
            {
                CreatePipeline();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public TextureView* Color { get; private set; }
        public TextureView* Depth { get; private set; }

        /// <summary>Targets at this size, remade when the window changes.</summary>
        public void Ensure(uint width, uint height)
        {
            if (Color != null && width == _width && height == _height)
            {
                return;
            }
            ReleaseTargets();
            _width = width;
            _height = height;
            Create(_colorFormat, TextureUsage.RenderAttachment, out _color, out var color);
            Color = color;
            Create(_depthFormat, TextureUsage.RenderAttachment | TextureUsage.TextureBinding, out _depth, out var depth);
            Depth = depth;

            var entry = new BindGroupEntry { Binding = 0, TextureView = Depth };
            var descriptor = new BindGroupDescriptor { Layout = _layout, EntryCount = 1, Entries = &entry };
            _group = Api.DeviceCreateBindGroup(Device, &descriptor);
            if (_group == null)
            {
                throw new InvalidOperationException("Failed to create the MSAA depth resolve bind group.");
            }
        }

        /// <summary>Writes the four-sample depth's first sample into <paramref name="target"/> (cleared, then every pixel written).</summary>
        public void ResolveDepth(CommandEncoder* encoder, TextureView* target)
        {
            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = target,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilReadOnly = true
            };
            var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 0, ColorAttachments = null, DepthStencilAttachment = &depthAttachment };
            var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            try
            {
                Api.RenderPassEncoderSetPipeline(pass, _pipeline);
                Api.RenderPassEncoderSetBindGroup(pass, 0, _group, 0, null);
                Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                Api.RenderPassEncoderEnd(pass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(pass);
            }
        }

        private void Create(TextureFormat format, TextureUsage usage, out Texture* texture, out TextureView* view)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = usage,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = _width, Height = _height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
                SampleCount = SampleCount
            };
            texture = Api.DeviceCreateTexture(Device, &descriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create a {SampleCount}x MSAA {format} target.");
            }
            view = Api.TextureCreateView(texture, null);
            if (view == null)
            {
                throw new InvalidOperationException($"Failed to view the MSAA {format} target.");
            }
        }

        private void CreatePipeline()
        {
            var entry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.Dimension2D, Multisampled = true }
            };
            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &entry };
            _layout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
            var layout = _layout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
            _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);

            var shader = Gpu.CreateShaderModule("depth_resolve.wgsl");
            var vertexEntry = Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentEntry = Marshal.StringToCoTaskMemUTF8("fs_main");
            try
            {
                var fragment = new FragmentState { Module = shader, EntryPoint = (byte*)fragmentEntry, TargetCount = 0, Targets = null };
                var stencil = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
                var depth = new DepthStencilState
                {
                    Format = _depthFormat,
                    DepthWriteEnabled = true,
                    DepthCompare = CompareFunction.Always,
                    StencilFront = stencil,
                    StencilBack = stencil,
                    StencilReadMask = uint.MaxValue,
                    StencilWriteMask = uint.MaxValue
                };
                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState { Module = shader, EntryPoint = (byte*)vertexEntry },
                    Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    DepthStencil = &depth,
                    Fragment = &fragment
                };
                _pipeline = Api.DeviceCreateRenderPipeline(Device, &descriptor);
                if (_pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create the MSAA depth resolve pipeline.");
                }
            }
            finally
            {
                Api.ShaderModuleRelease(shader);
                Marshal.FreeCoTaskMem(vertexEntry);
                Marshal.FreeCoTaskMem(fragmentEntry);
            }
        }

        private void ReleaseTargets()
        {
            if (_group != null) { Api.BindGroupRelease(_group); _group = null; }
            if (Color != null) { Api.TextureViewRelease(Color); Color = null; }
            if (_color != null) { Api.TextureRelease(_color); _color = null; }
            if (Depth != null) { Api.TextureViewRelease(Depth); Depth = null; }
            if (_depth != null) { Api.TextureRelease(_depth); _depth = null; }
        }

        public override void Dispose()
        {
            ReleaseTargets();
            if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
            if (_pipelineLayout != null) { Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
            if (_layout != null) { Api.BindGroupLayoutRelease(_layout); _layout = null; }
        }
    }
}
