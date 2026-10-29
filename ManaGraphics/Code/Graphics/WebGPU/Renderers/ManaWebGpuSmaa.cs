using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// SMAA 1x (smaa.wgsl): the tone-mapped scene into a texture of the window's format, its luma edges, the blending
    /// weights from the precomputed area and search tables (embedded, ManaGraphics/Assets/Smaa), and -- drawn into the
    /// window in the tone-map pass -- the neighbourhood blend with the selection outline over it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSmaa : ManaWebGpuDeviceObject
    {
        private readonly TextureFormat _surfaceFormat;
        private ShaderModule* _shader;
        private Sampler* _sampler;
        private WebGpuBuffer* _params;
        private Texture* _area;
        private TextureView* _areaView;
        private Texture* _search;
        private TextureView* _searchView;
        private RenderPipeline* _edgesPipeline;
        private RenderPipeline* _weightsPipeline;
        private RenderPipeline* _presentPipeline;

        private Texture* _color;
        private TextureView* _colorView;
        private Texture* _edges;
        private TextureView* _edgesView;
        private Texture* _blend;
        private TextureView* _blendView;
        private BindGroup* _edgesGroup;
        private BindGroup* _weightsGroup;
        private BindGroup* _presentGroup;
        private TextureView* _outlineMask;
        private uint _width;
        private uint _height;

        public ManaWebGpuSmaa(ManaWebGpuDevice gpu, TextureFormat surfaceFormat)
            : base(gpu)
        {
            _surfaceFormat = surfaceFormat;
            try
            {
                Create();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Whether the last <see cref="Encode"/> left a frame for <see cref="DrawPresent"/>.</summary>
        public bool Ready => _presentGroup != null;

        /// <summary>Passes 0-2: tone map, edges, weights. The blend itself is <see cref="DrawPresent"/>, in the window's pass.</summary>
        public void Encode(CommandEncoder* encoder, ManaWebGpuHdrPostProcessor hdr, uint width, uint height)
        {
            if (width == 0 || height == 0 || hdr.OutlineMaskView == null)
            {
                return;
            }
            if (width != _width || height != _height || hdr.OutlineMaskView != _outlineMask)
            {
                Resize(width, height, hdr.OutlineMaskView);
            }
            var metrics = new Vector4(1.0f / width, 1.0f / height, width, height);
            Api.QueueWriteBuffer(Queue, _params, 0, &metrics, (nuint)sizeof(Vector4));

            hdr.EncodeTonemap(encoder, _colorView);
            Pass(encoder, _edgesView, _edgesPipeline, _edgesGroup);
            Pass(encoder, _blendView, _weightsPipeline, _weightsGroup);
        }

        public void DrawPresent(RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetPipeline(pass, _presentPipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _presentGroup, 0, null);
            Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
        }

        // Cleared first: the edge pass discards where there is no edge, and both read zero there.
        private void Pass(CommandEncoder* encoder, TextureView* target, RenderPipeline* pipeline, BindGroup* group)
        {
            var colorAttachment = new RenderPassColorAttachment
            {
                View = target,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 0.0 }
            };
            var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
            var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            try
            {
                Api.RenderPassEncoderSetPipeline(pass, pipeline);
                Api.RenderPassEncoderSetBindGroup(pass, 0, group, 0, null);
                Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                Api.RenderPassEncoderEnd(pass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(pass);
            }
        }

        private void Create()
        {
            _shader = Gpu.CreateShaderModule("smaa.wgsl");
            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Nearest,
                MaxAnisotropy = 1
            };
            _sampler = Api.DeviceCreateSampler(Device, &samplerDescriptor);
            var bufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Vector4) };
            _params = Api.DeviceCreateBuffer(Device, &bufferDescriptor);

            Table("Smaa.AreaTex.bin", 160, 560, TextureFormat.RG8Unorm, 2, out _area, out _areaView);
            Table("Smaa.SearchTex.bin", 64, 16, TextureFormat.R8Unorm, 1, out _search, out _searchView);

            _edgesPipeline = Pipeline("fs_edges", TextureFormat.RG8Unorm);
            _weightsPipeline = Pipeline("fs_weights", TextureFormat.Rgba8Unorm);
            _presentPipeline = Pipeline("fs_present", _surfaceFormat);
            if (_sampler == null || _params == null || _edgesPipeline == null || _weightsPipeline == null || _presentPipeline == null)
            {
                throw new InvalidOperationException("Failed to create the SMAA pipelines.");
            }
        }

        private void Table(string resource, uint width, uint height, TextureFormat format, uint bytesPerPixel, out Texture* texture, out TextureView* view)
        {
            using var stream = typeof(ManaWebGpuSmaa).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"SMAA table '{resource}' is not embedded.");
            var bytes = new byte[width * height * bytesPerPixel];
            stream.ReadExactly(bytes);

            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };
            texture = Api.DeviceCreateTexture(Device, &descriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create the SMAA table '{resource}'.");
            }
            var destination = new ImageCopyTexture { Texture = texture, MipLevel = 0, Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { Offset = 0, BytesPerRow = width * bytesPerPixel, RowsPerImage = height };
            var extent = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
            fixed (byte* data = bytes)
            {
                Api.QueueWriteTexture(Queue, &destination, data, (nuint)bytes.Length, &layout, &extent);
            }
            view = Api.TextureCreateView(texture, null);
        }

        // An automatic layout: each pass's bind group holds only what its entry point reads, so no pass binds the
        // texture it draws into.
        private RenderPipeline* Pipeline(string fragmentEntry, TextureFormat format)
        {
            var vertex = Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentName = Marshal.StringToCoTaskMemUTF8(fragmentEntry);
            try
            {
                var target = new ColorTargetState { Format = format, Blend = null, WriteMask = ColorWriteMask.All };
                var fragment = new FragmentState { Module = _shader, EntryPoint = (byte*)fragmentName, TargetCount = 1, Targets = &target };
                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = null,
                    Vertex = new VertexState { Module = _shader, EntryPoint = (byte*)vertex },
                    Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    Fragment = &fragment
                };
                return Api.DeviceCreateRenderPipeline(Device, &descriptor);
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertex);
                Marshal.FreeCoTaskMem(fragmentName);
            }
        }

        private void Resize(uint width, uint height, TextureView* outlineMask)
        {
            ReleaseTargets();
            _width = width;
            _height = height;
            _outlineMask = outlineMask;
            Target(_surfaceFormat, out _color, out _colorView);
            Target(TextureFormat.RG8Unorm, out _edges, out _edgesView);
            Target(TextureFormat.Rgba8Unorm, out _blend, out _blendView);

            _edgesGroup = Group(_edgesPipeline, [(2, (nint)_colorView)]);
            _weightsGroup = Group(_weightsPipeline, [(3, (nint)_edgesView), (4, (nint)_areaView), (5, (nint)_searchView)]);
            _presentGroup = Group(_presentPipeline, [(2, (nint)_colorView), (6, (nint)_blendView), (7, (nint)outlineMask)]);
        }

        private void Target(TextureFormat format, out Texture* texture, out TextureView* view)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = _width, Height = _height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };
            texture = Api.DeviceCreateTexture(Device, &descriptor);
            view = Api.TextureCreateView(texture, null);
            if (texture == null || view == null)
            {
                throw new InvalidOperationException($"Failed to create an SMAA {format} target.");
            }
        }

        private BindGroup* Group(RenderPipeline* pipeline, (uint Binding, nint View)[] textures)
        {
            var layout = Api.RenderPipelineGetBindGroupLayout(pipeline, 0);
            var entries = stackalloc BindGroupEntry[2 + textures.Length];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Vector4) };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };
            for (var i = 0; i < textures.Length; i++)
            {
                entries[2 + i] = new BindGroupEntry { Binding = textures[i].Binding, TextureView = (TextureView*)textures[i].View };
            }
            var descriptor = new BindGroupDescriptor { Layout = layout, EntryCount = (nuint)(2 + textures.Length), Entries = entries };
            var group = Api.DeviceCreateBindGroup(Device, &descriptor);
            Api.BindGroupLayoutRelease(layout);
            if (group == null)
            {
                throw new InvalidOperationException("Failed to create an SMAA bind group.");
            }
            return group;
        }

        private void ReleaseTargets()
        {
            if (_edgesGroup != null) { Api.BindGroupRelease(_edgesGroup); _edgesGroup = null; }
            if (_weightsGroup != null) { Api.BindGroupRelease(_weightsGroup); _weightsGroup = null; }
            if (_presentGroup != null) { Api.BindGroupRelease(_presentGroup); _presentGroup = null; }
            if (_colorView != null) { Api.TextureViewRelease(_colorView); _colorView = null; }
            if (_color != null) { Api.TextureRelease(_color); _color = null; }
            if (_edgesView != null) { Api.TextureViewRelease(_edgesView); _edgesView = null; }
            if (_edges != null) { Api.TextureRelease(_edges); _edges = null; }
            if (_blendView != null) { Api.TextureViewRelease(_blendView); _blendView = null; }
            if (_blend != null) { Api.TextureRelease(_blend); _blend = null; }
            _width = 0;
            _height = 0;
        }

        public override void Dispose()
        {
            ReleaseTargets();
            if (_edgesPipeline != null) { Api.RenderPipelineRelease(_edgesPipeline); _edgesPipeline = null; }
            if (_weightsPipeline != null) { Api.RenderPipelineRelease(_weightsPipeline); _weightsPipeline = null; }
            if (_presentPipeline != null) { Api.RenderPipelineRelease(_presentPipeline); _presentPipeline = null; }
            if (_areaView != null) { Api.TextureViewRelease(_areaView); _areaView = null; }
            if (_area != null) { Api.TextureRelease(_area); _area = null; }
            if (_searchView != null) { Api.TextureViewRelease(_searchView); _searchView = null; }
            if (_search != null) { Api.TextureRelease(_search); _search = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
            if (_sampler != null) { Api.SamplerRelease(_sampler); _sampler = null; }
            if (_shader != null) { Api.ShaderModuleRelease(_shader); _shader = null; }
        }
    }
}
