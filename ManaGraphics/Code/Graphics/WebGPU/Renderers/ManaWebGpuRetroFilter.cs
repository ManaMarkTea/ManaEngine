using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// The retro screen filter (retro_filter.wgsl, <see cref="ManaRetroFilterSettings"/>): the tone-mapped scene into a
    /// texture of the window's format, then -- drawn into the window in the tone-map pass, in place of the tone map and
    /// any anti-aliasing -- read back in big pixels, dithered to a few colours and worn like an old CRT or VHS tape.
    /// </summary>
    internal sealed unsafe class ManaWebGpuRetroFilter : ManaWebGpuDeviceObject
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public Vector4 Screen;
            public Vector4 Look;
            public Vector4 Wear;
            public Vector4 Colour;
        }

        private readonly TextureFormat _surfaceFormat;
        private ShaderModule* _shader;
        private WebGpuBuffer* _params;
        private RenderPipeline* _pipeline;
        private Texture* _color;
        private TextureView* _colorView;
        private BindGroup* _group;
        private TextureView* _outlineMask;
        private TextureView* _retroMask;
        private uint _width;
        private uint _height;

        public ManaWebGpuRetroFilter(ManaWebGpuDevice gpu, TextureFormat surfaceFormat)
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
        public bool Ready => _group != null;

        /// <summary>Tone-maps the scene into the filter's own texture and sets this frame's look.</summary>
        /// <param name="mask">Where a scoped filter applies (ManaWebGpuPostEffectMaskPass); null filters the whole screen.</param>
        public void Encode(CommandEncoder* encoder, ManaWebGpuHdrPostProcessor hdr, ManaRetroFilterSettings settings, TextureView* mask,
            uint width, uint height, float seconds)
        {
            if (width == 0 || height == 0 || hdr.OutlineMaskView == null)
            {
                return;
            }
            // The shader always reads a mask; unscoped, any texture will do and the outline mask is at hand.
            var scoped = mask != null;
            if (!scoped)
            {
                mask = hdr.OutlineMaskView;
            }
            if (width != _width || height != _height || hdr.OutlineMaskView != _outlineMask || mask != _retroMask)
            {
                Resize(width, height, hdr.OutlineMaskView, mask);
            }
            var scopedFlag = 0.0f;
            if (scoped)
            {
                scopedFlag = 1.0f;
            }

            // The texture decodes to linear in an sRGB window; the shader cuts colours in gamma space either way.
            var decodesToLinear = 0.0f;
            if (_surfaceFormat.ToString().EndsWith("Srgb", StringComparison.Ordinal))
            {
                decodesToLinear = 1.0f;
            }
            var values = new Params
            {
                Screen = new Vector4(width, height, settings.PixelSize(height), seconds % 3600.0f),
                Look = new Vector4(Math.Clamp(settings.ColorLevels, 2, 256), settings.Dither, settings.Scanlines, settings.ChromaticAberration),
                Wear = new Vector4(settings.Noise, settings.Glitch, settings.Vignette, settings.Curvature),
                Colour = new Vector4(settings.Saturation, decodesToLinear, scopedFlag, 0.0f)
            };
            Api.QueueWriteBuffer(Queue, _params, 0, &values, (nuint)sizeof(Params));

            hdr.EncodeTonemap(encoder, _colorView);
        }

        public void DrawPresent(RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetPipeline(pass, _pipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _group, 0, null);
            Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
        }

        private void Create()
        {
            _shader = Gpu.CreateShaderModule("retro_filter.wgsl");
            var bufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Params) };
            _params = Api.DeviceCreateBuffer(Device, &bufferDescriptor);

            var vertex = Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentName = Marshal.StringToCoTaskMemUTF8("fs_present");
            try
            {
                var target = new ColorTargetState { Format = _surfaceFormat, Blend = null, WriteMask = ColorWriteMask.All };
                var fragment = new FragmentState { Module = _shader, EntryPoint = (byte*)fragmentName, TargetCount = 1, Targets = &target };
                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = null,
                    Vertex = new VertexState { Module = _shader, EntryPoint = (byte*)vertex },
                    Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    Fragment = &fragment
                };
                _pipeline = Api.DeviceCreateRenderPipeline(Device, &descriptor);
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertex);
                Marshal.FreeCoTaskMem(fragmentName);
            }
            if (_params == null || _pipeline == null)
            {
                throw new InvalidOperationException("Failed to create the retro filter pipeline.");
            }
        }

        private void Resize(uint width, uint height, TextureView* outlineMask, TextureView* retroMask)
        {
            ReleaseTargets();
            _width = width;
            _height = height;
            _outlineMask = outlineMask;
            _retroMask = retroMask;

            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = _surfaceFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _color = Api.DeviceCreateTexture(Device, &textureDescriptor);
            _colorView = Api.TextureCreateView(_color, null);
            if (_color == null || _colorView == null)
            {
                throw new InvalidOperationException("Failed to create the retro filter's colour target.");
            }

            var layout = Api.RenderPipelineGetBindGroupLayout(_pipeline, 0);
            var entries = stackalloc BindGroupEntry[4];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
            entries[1] = new BindGroupEntry { Binding = 1, TextureView = _colorView };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = outlineMask };
            entries[3] = new BindGroupEntry { Binding = 3, TextureView = retroMask };
            var groupDescriptor = new BindGroupDescriptor { Layout = layout, EntryCount = 4, Entries = entries };
            _group = Api.DeviceCreateBindGroup(Device, &groupDescriptor);
            Api.BindGroupLayoutRelease(layout);
            if (_group == null)
            {
                throw new InvalidOperationException("Failed to create the retro filter's bind group.");
            }
        }

        private void ReleaseTargets()
        {
            if (_group != null) { Api.BindGroupRelease(_group); _group = null; }
            if (_colorView != null) { Api.TextureViewRelease(_colorView); _colorView = null; }
            if (_color != null) { Api.TextureRelease(_color); _color = null; }
            _width = 0;
            _height = 0;
            _retroMask = null;
        }

        public override void Dispose()
        {
            ReleaseTargets();
            if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
            if (_shader != null) { Api.ShaderModuleRelease(_shader); _shader = null; }
        }
    }
}
