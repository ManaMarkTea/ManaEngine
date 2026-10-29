using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// Screen-space ambient occlusion, before the scene pass: the opaque PBR scenery depth-only from the camera (a
    /// prepass; the scene pass makes its depth as it shades, too late), occlusion from that at half resolution
    /// (ssao.wgsl), a depth-aware blur (ssao_blur.wgsl) and accumulation over frames (ssao_temporal.wgsl), which is
    /// what stills the noise. The PBR shader darkens only its ambient light by it (frame group binding 10), so sun and
    /// lamps are left as they are. Off (and nothing drawn) at strength 0 and on phones.
    /// </summary>
    internal sealed unsafe class ManaWebGpuAmbientOcclusionPass : ManaWebGpuRenderPass
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public Matrix4x4 Projection;
            public Matrix4x4 InverseProjection;
            public Vector4 Size;
            public Vector4 Settings;
            // ssao_temporal.wgsl only; the other two read the start of the same buffer.
            public Matrix4x4 InverseView;
            public Matrix4x4 PreviousViewProjection;
            public Vector4 PreviousCamera;
        }

        private readonly ManaWebGpuSceneBindings _bindings;
        private readonly ManaWebGpuDepthOnlyDrawer _drawer;
        private readonly Matrix4x4[] _view = new Matrix4x4[1];
        private WebGpuBuffer* _params;
        private ShaderModule* _aoShader;
        private ShaderModule* _blurShader;
        private BindGroupLayout* _aoLayout;
        private BindGroupLayout* _blurLayout;
        private PipelineLayout* _aoPipelineLayout;
        private PipelineLayout* _blurPipelineLayout;
        private ComputePipeline* _aoPipeline;
        private ComputePipeline* _blurPipeline;
        private ShaderModule* _temporalShader;
        private BindGroupLayout* _temporalLayout;
        private PipelineLayout* _temporalPipelineLayout;
        private ComputePipeline* _temporalPipeline;
        private Sampler* _historySampler;
        private BindGroup* _aoGroup;
        private BindGroup* _blurGroup;
        private BindGroup* _temporalGroup;
        private Texture* _depth;
        private TextureView* _depthView;
        private Texture* _raw;
        private TextureView* _rawView;
        private Texture* _blurred;
        private TextureView* _blurredView;
        private Texture* _final;
        private TextureView* _finalView;
        private Texture* _history;
        private TextureView* _historyView;
        private Matrix4x4 _previousViewProjection;
        private Vector3 _previousCamera;
        private bool _historyValid;
        private uint _width;
        private uint _height;
        private bool _bound;

        public ManaWebGpuAmbientOcclusionPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings)
            : base(gpu)
        {
            _bindings = bindings;
            _drawer = new ManaWebGpuDepthOnlyDrawer(gpu, bindings, 1, TextureFormat.Depth32float, bias: false, "ao depth");
            try
            {
                var descriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Params) };
                _params = Api.DeviceCreateBuffer(Device, &descriptor);
                CreatePipelines();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override string ProfileName => "GPU: Ambient Occlusion";
        public override int PipelineCount => _drawer.PipelineCount + 3;
        public override int ShaderModuleCount => _drawer.PipelineCount + 3;

        /// <summary>What the prepass draws: opaque and cut-out PBR surfaces, the ones that read the occlusion back.</summary>
        public static bool Occludes(ManaRenderMeshInstance instance) =>
            !instance.IsTransparent && !instance.AlwaysOnTop && !instance.Unlit
            && (instance.ShaderName == null || instance.ShaderName == "ManaPbrShader");

        public override void Prepare(ManaWebGpuFrame frame)
        {
            var settings = frame.Scene.IrradianceSettings;
            if (settings.AmbientOcclusion <= 0.0f || ManaWebGpuDevice.UseMobileRendering || frame.Width == 0 || frame.Height == 0)
            {
                if (_bound)
                {
                    _bindings.SetAmbientOcclusion(null);
                    _bound = false;
                }
                _historyValid = false;
                return;
            }

            if (frame.Width != _width || frame.Height != _height)
            {
                Resize(frame.Width, frame.Height);
            }
            if (!_bound)
            {
                _bindings.SetAmbientOcclusion(_finalView);
                _bound = true;
            }

            var camera = frame.Scene.Camera;
            _view[0] = camera.View * camera.Projection;
            _drawer.Draw(frame, _view, _ => (nint)_depthView, Occludes);

            Matrix4x4.Invert(camera.Projection, out var inverse);
            Matrix4x4.Invert(camera.View, out var inverseView);
            var halfWidth = (_width + 1) / 2;
            var halfHeight = (_height + 1) / 2;
            var parameters = new Params
            {
                Projection = camera.Projection,
                InverseProjection = inverse,
                Size = new Vector4(_width, _height, halfWidth, halfHeight),
                Settings = new Vector4(Math.Clamp(settings.AmbientOcclusionRadius, 0.05f, 10.0f), Math.Clamp(settings.AmbientOcclusion, 0.0f, 4.0f), frame.Number % 64,
                    // How thick a depth sample is taken to be (ssao.wgsl): thin enough that light gets behind a chair leg.
                    Math.Clamp(settings.AmbientOcclusionRadius * 0.4f, 0.05f, 2.0f)),
                InverseView = inverseView,
                PreviousViewProjection = _previousViewProjection,
                PreviousCamera = new Vector4(_previousCamera, 0.0f)
            };
            if (_historyValid)
            {
                parameters.PreviousCamera.W = 1.0f;
            }
            Api.QueueWriteBuffer(Queue, _params, 0, &parameters, (nuint)sizeof(Params));

            Dispatch(frame.Encoder, _aoPipeline, _aoGroup, halfWidth, halfHeight);
            Dispatch(frame.Encoder, _blurPipeline, _blurGroup, halfWidth, halfHeight);
            Dispatch(frame.Encoder, _temporalPipeline, _temporalGroup, halfWidth, halfHeight);

            // This frame becomes the next one's history.
            var source = new ImageCopyTexture { Texture = _final, MipLevel = 0, Aspect = TextureAspect.All };
            var target = new ImageCopyTexture { Texture = _history, MipLevel = 0, Aspect = TextureAspect.All };
            var extent = new Extent3D { Width = halfWidth, Height = halfHeight, DepthOrArrayLayers = 1 };
            Api.CommandEncoderCopyTextureToTexture(frame.Encoder, &source, &target, &extent);
            _previousViewProjection = _view[0];
            _previousCamera = camera.Position;
            _historyValid = true;
        }

        private void Dispatch(CommandEncoder* encoder, ComputePipeline* pipeline, BindGroup* group, uint width, uint height)
        {
            var descriptor = new ComputePassDescriptor();
            var pass = Api.CommandEncoderBeginComputePass(encoder, &descriptor);
            try
            {
                Api.ComputePassEncoderSetPipeline(pass, pipeline);
                Api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
                Api.ComputePassEncoderDispatchWorkgroups(pass, (width + 7) / 8, (height + 7) / 8, 1);
                Api.ComputePassEncoderEnd(pass);
            }
            finally
            {
                Api.ComputePassEncoderRelease(pass);
            }
        }

        private void Resize(uint width, uint height)
        {
            ReleaseTargets();
            _width = width;
            _height = height;
            var halfWidth = (width + 1) / 2;
            var halfHeight = (height + 1) / 2;
            Create(width, height, TextureFormat.Depth32float, TextureUsage.RenderAttachment | TextureUsage.TextureBinding, TextureAspect.DepthOnly, out _depth, out _depthView);
            Create(halfWidth, halfHeight, TextureFormat.Rgba8Unorm, TextureUsage.StorageBinding | TextureUsage.TextureBinding, TextureAspect.All, out _raw, out _rawView);
            Create(halfWidth, halfHeight, TextureFormat.Rgba8Unorm, TextureUsage.StorageBinding | TextureUsage.TextureBinding, TextureAspect.All, out _blurred, out _blurredView);
            // Half floats: g carries each pixel's distance from the camera, for the next frame's same-surface test.
            Create(halfWidth, halfHeight, TextureFormat.Rgba16float, TextureUsage.StorageBinding | TextureUsage.TextureBinding | TextureUsage.CopySrc, TextureAspect.All, out _final, out _finalView);
            Create(halfWidth, halfHeight, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.CopyDst, TextureAspect.All, out _history, out _historyView);
            _historyValid = false;

            var ao = stackalloc BindGroupEntry[3];
            ao[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
            ao[1] = new BindGroupEntry { Binding = 1, TextureView = _depthView };
            ao[2] = new BindGroupEntry { Binding = 2, TextureView = _rawView };
            var aoDescriptor = new BindGroupDescriptor { Layout = _aoLayout, EntryCount = 3, Entries = ao };
            _aoGroup = Api.DeviceCreateBindGroup(Device, &aoDescriptor);

            var blur = stackalloc BindGroupEntry[4];
            blur[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
            blur[1] = new BindGroupEntry { Binding = 1, TextureView = _depthView };
            blur[2] = new BindGroupEntry { Binding = 2, TextureView = _rawView };
            blur[3] = new BindGroupEntry { Binding = 3, TextureView = _blurredView };
            var blurDescriptor = new BindGroupDescriptor { Layout = _blurLayout, EntryCount = 4, Entries = blur };
            _blurGroup = Api.DeviceCreateBindGroup(Device, &blurDescriptor);

            var temporal = stackalloc BindGroupEntry[6];
            temporal[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
            temporal[1] = new BindGroupEntry { Binding = 1, TextureView = _depthView };
            temporal[2] = new BindGroupEntry { Binding = 2, TextureView = _blurredView };
            temporal[3] = new BindGroupEntry { Binding = 3, TextureView = _historyView };
            temporal[4] = new BindGroupEntry { Binding = 4, Sampler = _historySampler };
            temporal[5] = new BindGroupEntry { Binding = 5, TextureView = _finalView };
            var temporalDescriptor = new BindGroupDescriptor { Layout = _temporalLayout, EntryCount = 6, Entries = temporal };
            _temporalGroup = Api.DeviceCreateBindGroup(Device, &temporalDescriptor);
            if (_aoGroup == null || _blurGroup == null || _temporalGroup == null)
            {
                throw new InvalidOperationException("Failed to create the ambient occlusion bind groups.");
            }
            // New textures: the frame group must bind this one, not the released one.
            _bound = false;
        }

        private void Create(uint width, uint height, TextureFormat format, TextureUsage usage, TextureAspect aspect, out Texture* texture, out TextureView* view)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = usage,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };
            texture = Api.DeviceCreateTexture(Device, &descriptor);
            var viewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = aspect
            };
            view = Api.TextureCreateView(texture, &viewDescriptor);
            if (texture == null || view == null)
            {
                throw new InvalidOperationException("Failed to create an ambient occlusion texture.");
            }
        }

        private void CreatePipelines()
        {
            var uniform = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Compute,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(Params) }
            };
            var depth = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Compute,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.Dimension2D }
            };
            BindGroupLayoutEntry Storage(uint binding, TextureFormat format) => new()
            {
                Binding = binding,
                Visibility = ShaderStage.Compute,
                StorageTexture = new StorageTextureBindingLayout
                {
                    Access = StorageTextureAccess.WriteOnly, Format = format, ViewDimension = TextureViewDimension.Dimension2D
                }
            };
            BindGroupLayoutEntry Sampled(uint binding) => new()
            {
                Binding = binding,
                Visibility = ShaderStage.Compute,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D }
            };

            var ao = stackalloc BindGroupLayoutEntry[3];
            ao[0] = uniform;
            ao[1] = depth;
            ao[2] = Storage(2, TextureFormat.Rgba8Unorm);
            var aoDescriptor = new BindGroupLayoutDescriptor { EntryCount = 3, Entries = ao };
            _aoLayout = Api.DeviceCreateBindGroupLayout(Device, &aoDescriptor);

            var blur = stackalloc BindGroupLayoutEntry[4];
            blur[0] = uniform;
            blur[1] = depth;
            blur[2] = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Compute,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D }
            };
            blur[3] = Storage(3, TextureFormat.Rgba8Unorm);
            var blurDescriptor = new BindGroupLayoutDescriptor { EntryCount = 4, Entries = blur };
            _blurLayout = Api.DeviceCreateBindGroupLayout(Device, &blurDescriptor);

            var temporal = stackalloc BindGroupLayoutEntry[6];
            temporal[0] = uniform;
            temporal[1] = depth;
            temporal[2] = Sampled(2);
            temporal[3] = Sampled(3);
            temporal[4] = new BindGroupLayoutEntry
            {
                Binding = 4,
                Visibility = ShaderStage.Compute,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
            };
            temporal[5] = Storage(5, TextureFormat.Rgba16float);
            var temporalDescriptor = new BindGroupLayoutDescriptor { EntryCount = 6, Entries = temporal };
            _temporalLayout = Api.DeviceCreateBindGroupLayout(Device, &temporalDescriptor);

            // Bilinear: history is looked up where a point was last frame, between texels.
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
            _historySampler = Api.DeviceCreateSampler(Device, &samplerDescriptor);

            Compute("ssao.wgsl", _aoLayout, out _aoShader, out _aoPipelineLayout, out _aoPipeline);
            Compute("ssao_blur.wgsl", _blurLayout, out _blurShader, out _blurPipelineLayout, out _blurPipeline);
            Compute("ssao_temporal.wgsl", _temporalLayout, out _temporalShader, out _temporalPipelineLayout, out _temporalPipeline);
        }

        private void Compute(string shader, BindGroupLayout* layout, out ShaderModule* module, out PipelineLayout* pipelineLayout, out ComputePipeline* pipeline)
        {
            module = Gpu.CreateShaderModule(shader);
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
            pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
            var entryPoint = Marshal.StringToCoTaskMemUTF8("cs_main");
            try
            {
                var descriptor = new ComputePipelineDescriptor
                {
                    Layout = pipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = (byte*)entryPoint }
                };
                pipeline = Api.DeviceCreateComputePipeline(Device, &descriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException($"Failed to create the {shader} pipeline.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(entryPoint);
            }
        }

        private void ReleaseTargets()
        {
            if (_aoGroup != null) { Api.BindGroupRelease(_aoGroup); _aoGroup = null; }
            if (_blurGroup != null) { Api.BindGroupRelease(_blurGroup); _blurGroup = null; }
            if (_temporalGroup != null) { Api.BindGroupRelease(_temporalGroup); _temporalGroup = null; }
            if (_blurredView != null) { Api.TextureViewRelease(_blurredView); _blurredView = null; }
            if (_blurred != null) { Api.TextureRelease(_blurred); _blurred = null; }
            if (_historyView != null) { Api.TextureViewRelease(_historyView); _historyView = null; }
            if (_history != null) { Api.TextureRelease(_history); _history = null; }
            if (_depthView != null) { Api.TextureViewRelease(_depthView); _depthView = null; }
            if (_depth != null) { Api.TextureRelease(_depth); _depth = null; }
            if (_rawView != null) { Api.TextureViewRelease(_rawView); _rawView = null; }
            if (_raw != null) { Api.TextureRelease(_raw); _raw = null; }
            if (_finalView != null) { Api.TextureViewRelease(_finalView); _finalView = null; }
            if (_final != null) { Api.TextureRelease(_final); _final = null; }
            _width = 0;
            _height = 0;
        }

        public override void Dispose()
        {
            _drawer.Dispose();
            ReleaseTargets();
            if (_aoPipeline != null) { Api.ComputePipelineRelease(_aoPipeline); _aoPipeline = null; }
            if (_blurPipeline != null) { Api.ComputePipelineRelease(_blurPipeline); _blurPipeline = null; }
            if (_temporalPipeline != null) { Api.ComputePipelineRelease(_temporalPipeline); _temporalPipeline = null; }
            if (_temporalPipelineLayout != null) { Api.PipelineLayoutRelease(_temporalPipelineLayout); _temporalPipelineLayout = null; }
            if (_temporalLayout != null) { Api.BindGroupLayoutRelease(_temporalLayout); _temporalLayout = null; }
            if (_temporalShader != null) { Api.ShaderModuleRelease(_temporalShader); _temporalShader = null; }
            if (_historySampler != null) { Api.SamplerRelease(_historySampler); _historySampler = null; }
            if (_aoPipelineLayout != null) { Api.PipelineLayoutRelease(_aoPipelineLayout); _aoPipelineLayout = null; }
            if (_blurPipelineLayout != null) { Api.PipelineLayoutRelease(_blurPipelineLayout); _blurPipelineLayout = null; }
            if (_aoLayout != null) { Api.BindGroupLayoutRelease(_aoLayout); _aoLayout = null; }
            if (_blurLayout != null) { Api.BindGroupLayoutRelease(_blurLayout); _blurLayout = null; }
            if (_aoShader != null) { Api.ShaderModuleRelease(_aoShader); _aoShader = null; }
            if (_blurShader != null) { Api.ShaderModuleRelease(_blurShader); _blurShader = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
        }
    }
}
