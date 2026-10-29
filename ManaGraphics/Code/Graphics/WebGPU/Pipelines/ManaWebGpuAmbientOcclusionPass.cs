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
    /// (ssao.wgsl) and a depth-aware blur (ssao_blur.wgsl). The PBR shader darkens only its ambient light by it (frame
    /// group binding 10), so sun and lamps are left as they are. Off (and nothing drawn) at strength 0 and on phones.
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
        private BindGroup* _aoGroup;
        private BindGroup* _blurGroup;
        private Texture* _depth;
        private TextureView* _depthView;
        private Texture* _raw;
        private TextureView* _rawView;
        private Texture* _final;
        private TextureView* _finalView;
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
        public override int PipelineCount => _drawer.PipelineCount + 2;
        public override int ShaderModuleCount => _drawer.PipelineCount + 2;

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
            var halfWidth = (_width + 1) / 2;
            var halfHeight = (_height + 1) / 2;
            var parameters = new Params
            {
                Projection = camera.Projection,
                InverseProjection = inverse,
                Size = new Vector4(_width, _height, halfWidth, halfHeight),
                Settings = new Vector4(Math.Clamp(settings.AmbientOcclusionRadius, 0.05f, 10.0f), Math.Clamp(settings.AmbientOcclusion, 0.0f, 4.0f), frame.Number % 64,
                    // How thick a depth sample is taken to be (ssao.wgsl): thin enough that light gets behind a chair leg.
                    Math.Clamp(settings.AmbientOcclusionRadius * 0.4f, 0.05f, 2.0f))
            };
            Api.QueueWriteBuffer(Queue, _params, 0, &parameters, (nuint)sizeof(Params));

            Dispatch(frame.Encoder, _aoPipeline, _aoGroup, halfWidth, halfHeight);
            Dispatch(frame.Encoder, _blurPipeline, _blurGroup, halfWidth, halfHeight);
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
            Create(halfWidth, halfHeight, TextureFormat.Rgba8Unorm, TextureUsage.StorageBinding | TextureUsage.TextureBinding, TextureAspect.All, out _final, out _finalView);

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
            blur[3] = new BindGroupEntry { Binding = 3, TextureView = _finalView };
            var blurDescriptor = new BindGroupDescriptor { Layout = _blurLayout, EntryCount = 4, Entries = blur };
            _blurGroup = Api.DeviceCreateBindGroup(Device, &blurDescriptor);
            if (_aoGroup == null || _blurGroup == null)
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
            BindGroupLayoutEntry Storage(uint binding) => new()
            {
                Binding = binding,
                Visibility = ShaderStage.Compute,
                StorageTexture = new StorageTextureBindingLayout
                {
                    Access = StorageTextureAccess.WriteOnly, Format = TextureFormat.Rgba8Unorm, ViewDimension = TextureViewDimension.Dimension2D
                }
            };

            var ao = stackalloc BindGroupLayoutEntry[3];
            ao[0] = uniform;
            ao[1] = depth;
            ao[2] = Storage(2);
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
            blur[3] = Storage(3);
            var blurDescriptor = new BindGroupLayoutDescriptor { EntryCount = 4, Entries = blur };
            _blurLayout = Api.DeviceCreateBindGroupLayout(Device, &blurDescriptor);

            Compute("ssao.wgsl", _aoLayout, out _aoShader, out _aoPipelineLayout, out _aoPipeline);
            Compute("ssao_blur.wgsl", _blurLayout, out _blurShader, out _blurPipelineLayout, out _blurPipeline);
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
