using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : ManaGraphicsDiagnosticsProvider, IDisposable
    {
        public bool UseSceneCapture = false;

        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly IInputContext? _input;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly RequestAdapterCallback _requestAdapterCallback;
        private readonly RequestDeviceCallback _requestDeviceCallback;
        private readonly ErrorCallback _errorCallback;
        private readonly string? _requestedBackend;
        private readonly InstanceBackend _instanceBackend;

        private Instance* _instance;
        private Surface* _surface;
        private Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private Texture* _depthTexture;
        private TextureView* _depthTextureView;
        private TextureFormat _surfaceFormat;
        private readonly TextureFormat _depthFormat = TextureFormat.Depth24Plus;
        private Vector2D<int> _framebufferSize;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _configured;
        private readonly List<ManaWebGpuSceneMesh> _sceneMeshes = new();
        private readonly Dictionary<string, ManaWebGpuSceneMesh> _sceneMeshMap = new();
        private readonly Dictionary<string, ManaWebGpuMeshTexture> _meshTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private ManaRenderScene? _activeRenderScene;
        private Vector3 _demoMeshCenter;
        private float _demoMeshScale = 1.0f;
        // group 0 -- per-frame globals (camera/time/lights), written+bound once per frame, not per mesh.
        private BindGroupLayout* _frameBindGroupLayout;
        private WebGpuBuffer* _frameUniformBuffer;
        private BindGroup* _frameBindGroup;
        // group 1 -- per-object data (model/material) + per-mesh textures.
        private BindGroupLayout* _objectBindGroupLayout;
        private PipelineLayout* _demoPipelineLayout;
        // group 2 -- per-material custom uniforms (opt-in, e.g. water ripples).
        private BindGroupLayout* _customUniformBindGroupLayout;
        private PipelineLayout* _customUniformPipelineLayout;
        private BindGroupLayout* _sceneCaptureBindGroupLayout;
        private PipelineLayout* _sceneCapturePipelineLayout;
        private RenderPipeline* _sceneCapturePipeline;
        private RenderPipeline* _outlineMaskPipeline;
        private RenderPipeline* _wireframePipeline;
        private ManaWebGpuHdrPostProcessor? _hdrPostProcessor;
        private ManaWebGpuEnvironmentPrefilterCompute? _environmentPrefilterCompute;
        private Texture* _sceneCaptureDepthTexture;
        private TextureView* _sceneCaptureDepthTextureView;
        private uint _sceneCaptureDepthSize;
        private Sampler* _demoSampler;
        private Sampler* _clampSampler;
        private ManaWebGpuEnvironmentReflectionMap? _environmentReflectionMap;
        private int _framesSinceEnvironmentCapture = int.MaxValue;
        private readonly Dictionary<string, nint> _shaderModules = new();
        private readonly Dictionary<string, nint> _pipelines = new();
        private ManaWebGpuImGuiController? _imGuiController;

        // Per-frame CPU-side timing for the last completed ClearFrame call, in milliseconds.
        // Read by the app layer (which owns Mana/ManaWorld) once per frame to feed a live perf HUD —
        // ManaGraphics can't call into Mana directly (wrong dependency direction), so it's exposed
        // here instead of pushed.
        public double LastCollectMs { get; private set; }
        public double LastDrawMs { get; private set; }
        public double LastSubmitMs { get; private set; }
        public double LastFrameTotalMs { get; private set; }

        public ManaWebGpuContext(IWindow window, IInputContext? input, string? requestedBackend, Func<ManaRenderScene>? renderSceneProvider = null) 
        {
            _window = window;
            _input = input;
            _renderSceneProvider = renderSceneProvider;
            _requestedBackend = requestedBackend;

            ManaConsole.WriteDiagnostic("Creating Silk WebGPU API.");
            _api = WebGPU.GetApi();
            _requestAdapterCallback = OnRequestAdapter;
            _requestDeviceCallback = OnRequestDevice;
            _errorCallback = OnError;

            ManaConsole.WriteDiagnostic("Creating WebGPU instance.");
            _instanceBackend = ParseInstanceBackend(requestedBackend);
            var instanceExtras = new InstanceExtras
            {
                Chain = new ChainedStruct
                {
                    SType = (SType)NativeSType.STypeInstanceExtras
                },
                Backends = _instanceBackend
            };

            var instanceDescriptor = new InstanceDescriptor
            {
                NextInChain = &instanceExtras.Chain
            };

            _instance = _api.CreateInstance(&instanceDescriptor);
            if (_instance == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU instance.");
            }

            ManaConsole.WriteDiagnostic("Creating WebGPU surface.");
            _surface = WebGPUSurface.CreateWebGPUSurface(window, _api, _instance);
            if (_surface == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU surface.");
            }

            ManaConsole.WriteDiagnostic("Requesting WebGPU adapter.");
            RequestAdapter(requestedBackend);
            ManaConsole.WriteDiagnostic("Requesting WebGPU device.");
            RequestDevice();

            ManaConsole.WriteDiagnostic("Configuring WebGPU queue and surface.");
            _queue = _api.DeviceGetQueue(_device);
            _api.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
            _surfaceFormat = _api.SurfaceGetPreferredFormat(_surface, _adapter);
            ConfigureSurface(window.FramebufferSize);
            if (_renderSceneProvider != null || IsDemoMeshPipelineEnabled())
            {
                if (CanCreateDemoMeshPipeline(requestedBackend))
                {
                    try
                    {
                        CreateDemoMeshPipeline();
                    }
                    catch (Exception ex)
                    {
                        ManaConsole.WriteDiagnostic("WebGPU mesh pipeline failed; continuing with clear-only renderer.");
                        ManaConsole.WriteDiagnostic(ex);
                        ReleaseDemoResources();
                    }
                }
                else
                {
                    ManaConsole.WriteDiagnostic("WebGPU mesh pipeline disabled on this backend. On Windows, the default backend is now Vulkan; explicit DX11/DX12 requests still keep the mesh pipeline off while those paths remain unstable.");
                }
            }
            else
            {
                ManaConsole.WriteDiagnostic("WebGPU mesh pipeline disabled. Set MANA_WEBGPU_DEMO_MESH=1 to test the fallback demo pipeline on the current backend.");
            }
            ManaConsole.WriteDiagnostic($"WebGPU surface configured: {window.FramebufferSize.X}x{window.FramebufferSize.Y}, format={_surfaceFormat}.");
            LogWebGpuDiagnostics(requestedBackend, _instanceBackend);
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            ConfigureSurface(framebufferSize);
        }

        public IManaImGuiController CreateImGuiController(IInputContext input)
        {
            _imGuiController = new ManaWebGpuImGuiController(_api, _device, _queue, _surfaceFormat, _window, input);
            return _imGuiController;
        }

        public void ClearFrame(Color clearColor)
        {
            if (!_configured)
            {
                return;
            }

            CheckPendingEnvironmentMap();

            var surfaceTexture = new SurfaceTexture();
            _api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
            if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success || surfaceTexture.Texture == null)
            {
                if (surfaceTexture.Texture != null)
                {
                    _api.TextureRelease(surfaceTexture.Texture);
                }

                // Acquire can transiently fail during startup/resize; reconfigure and
                // retry on the next frame instead of getting stuck on a blank surface.
                ConfigureSurface(_window.FramebufferSize);

                return;
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = _surfaceFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            var textureView = _api.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
            var hdrSceneTextureView = _hdrPostProcessor?.SceneTextureView;
            if (textureView == null)
            {
                if (textureView != null)
                {
                    _api.TextureViewRelease(textureView);
                }

                _api.TextureRelease(surfaceTexture.Texture);

                return;
            }

            var sceneTargetView = hdrSceneTextureView != null ? hdrSceneTextureView : textureView;
            var useHdrPost = _hdrPostProcessor != null && hdrSceneTextureView != null;

            var sceneColorAttachment = new RenderPassColorAttachment
            {
                View = sceneTargetView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            var depthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = _depthTextureView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };

            var sceneRenderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &sceneColorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            var frameStopwatch = Stopwatch.StartNew();

            var commandEncoderDescriptor = new CommandEncoderDescriptor();
            var commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
            var collectStopwatch = Stopwatch.StartNew();
            RefreshRenderScene();
            UpdateFrameUniform();
            LastCollectMs = collectStopwatch.Elapsed.TotalMilliseconds;
            EnsureEnvironmentInitializationStarted();
            CaptureSceneEnvironmentIfNeeded(commandEncoder);

            var sceneRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &sceneRenderPassDescriptor);
            var drawStopwatch = Stopwatch.StartNew();
            DrawDemoMesh(sceneRenderPass);
            DrawWireframeOverlay(sceneRenderPass);
            _api.RenderPassEncoderEnd(sceneRenderPass);
            LastDrawMs = drawStopwatch.Elapsed.TotalMilliseconds;

            if (useHdrPost)
            {
                DrawOutlineMask(commandEncoder);
                _hdrPostProcessor?.Process(commandEncoder);
            }

            var surfaceColorAttachment = new RenderPassColorAttachment
            {
                View = textureView,
                LoadOp = useHdrPost ? LoadOp.Clear : LoadOp.Load,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            var toneMapRenderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &surfaceColorAttachment
            };

            var toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
            _hdrPostProcessor?.Draw(toneMapRenderPass);
            _imGuiController?.Render(toneMapRenderPass);
            _api.RenderPassEncoderEnd(toneMapRenderPass);

            var commandBufferDescriptor = new CommandBufferDescriptor();
            var submitStopwatch = Stopwatch.StartNew();
            var commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
            _api.QueueSubmit(_queue, 1, &commandBuffer);
            _api.SurfacePresent(_surface);
            LastSubmitMs = submitStopwatch.Elapsed.TotalMilliseconds;

            _api.CommandBufferRelease(commandBuffer);
            _api.CommandEncoderRelease(commandEncoder);
            _api.RenderPassEncoderRelease(sceneRenderPass);
            _api.RenderPassEncoderRelease(toneMapRenderPass);
            _api.TextureViewRelease(textureView);
            _api.TextureRelease(surfaceTexture.Texture);

            LastFrameTotalMs = frameStopwatch.Elapsed.TotalMilliseconds;
        }

        public void Dispose()
        {
            ReleaseDemoResources();

            if (_surface != null && _configured)
            {
                _api.SurfaceUnconfigure(_surface);
                _configured = false;
            }

            ReleaseDepthResources();

            if (_queue != null)
            {
                _api.QueueRelease(_queue);
                _queue = null;
            }

            if (_device != null)
            {
                _api.DeviceRelease(_device);
                _device = null;
            }

            if (_adapter != null)
            {
                _api.AdapterRelease(_adapter);
                _adapter = null;
            }

            if (_surface != null)
            {
                _api.SurfaceRelease(_surface);
                _surface = null;
            }

            if (_instance != null)
            {
                _api.InstanceRelease(_instance);
                _instance = null;
            }
        }
    }
}
