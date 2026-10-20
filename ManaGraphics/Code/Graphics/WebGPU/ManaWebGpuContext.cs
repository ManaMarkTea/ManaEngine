using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Pipelines.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Diagnostics;
using ManaGraphics.Code.Graphics.Pipelines;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;
#if MANA_BROWSER
using ManaGraphics.Code.Graphics.Browser;
#endif

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : ManaGraphicsDiagnosticsProvider, IDisposable
    {
        public bool UseSceneCapture = false;

        // Never disposed: that would free wgpu_native, which other contexts and the iOS framework still use.
        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly IInputContext? _input;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly string? _requestedBackend;
#if !MANA_BROWSER
        private readonly InstanceBackend _instanceBackend;
#endif

        private Instance* _instance;
        private Surface* _surface;
        private Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
#if !MANA_BROWSER
        // wgpu-native's callback pump; InstanceProcessEvents when a build lacks it.
        private Wgpu? _wgpuNativeExtension;
#endif
        private readonly ManaWebGpuReadbackRegistry _readbackRegistry = new();
        private volatile bool _disposed;
        // The scene's depth, and a second one the always-on-top handles depth-test against.
        private ManaWebGpuDepthTarget? _depth;
        private ManaWebGpuDepthTarget? _handleDepth;
        private TextureView* _depthTextureView => _depth == null ? null : _depth.View;
        private TextureView* _handleDepthTextureView => _handleDepth == null ? null : _handleDepth.View;
        private TextureFormat _surfaceFormat;
        private readonly TextureFormat _depthFormat = TextureFormat.Depth24Plus;
        private Vector2D<int> _framebufferSize;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _configured;
        // Made with the device; meshes are only uploaded once the scene bindings exist.
        private ManaWebGpuSceneMeshes? _meshes;
        private readonly ManaWebGpuRenderThreadQueue _renderThread = new();
        // Set in FinishInitialization, which in the browser runs after the constructor.
        private ManaWebGpuMeshTextureCache _meshTextures = null!;
        private ManaRenderScene? _activeRenderScene;
        // Set in FinishInitialization; borrowed by every pass.
        private ManaWebGpuDevice _gpu = null!;
        // In frame order; each owns its pipelines (see ManaWebGpuRenderPass).
        private readonly List<ManaWebGpuRenderPass> _passes = new();
        private readonly ManaWebGpuFrame _passFrame = new();
        private ManaWebGpuShadowMap? _shadowMap;
        private ManaWebGpuParticlePass? _particlePass;
        private ManaWebGpuThumbnailRenderer? _thumbnails;
        private ManaWebGpuFrameCapture? _frameCapture;
#if !MANA_BROWSER
        private readonly List<ManaWebGpuPanelSurface> _panelSurfaces = new();
#endif
        public long LastGpuParticleCount => _particlePass?.Drawn ?? 0;
        public long RequestedGpuParticleCount => _particlePass?.Requested ?? 0;
        // Shared layouts, samplers and frame uniform; null while the renderer is clear-only.
        private ManaWebGpuSceneBindings? _bindings;
        private ManaWebGpuHdrPostProcessor? _hdrPostProcessor;
        // Set in FinishInitialization; ManaWebGpuGraphicsDevice forwards planet height requests to it.
        internal ManaWebGpuHeightSampleCompute? HeightSampler { get; private set; }
        // Made with the device; outlives pipeline failures, since loading waits on it either way.
        private ManaWebGpuEnvironment? _environment;
        public ManaCore.Loading.ManaLoadingViewModel EnvironmentLoading { get; } = new();
        public bool IsEnvironmentInitializationPending => _environment?.IsInitializationPending ?? true;
#if !MANA_BROWSER
#endif
        private ManaWebGpuUiCompositor? _ui;

        /// <summary>
        /// The ManaUI tree to draw over the scene, or null. The app records its draw list; this only submits it, so a UI
        /// bug cannot stall the GPU timeline.
        /// </summary>
        public ManaUI.UiRoot? UiRoot { get; set; }

        /// <summary>
        /// Skips refreshing and drawing the scene this frame (clear, tone map and UI still run). The app sets it while
        /// loading, so the first frames show the loading screen instead of uploading meshes nobody can see yet.
        /// </summary>
        public bool SuspendSceneWork { get; set; }

        // CPU timings of the last frame, in milliseconds, read by the app's perf HUD.
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
#if MANA_BROWSER
            _api = new WebGPU(new ManaBrowserWebGpuNativeContext());
#else
            _api = ManaWebGpuNativeHost.Current?.CreateApi() ?? WebGPU.GetApi();
#endif

            try
            {
                ManaConsole.WriteDiagnostic("Creating WebGPU instance.");
#if MANA_BROWSER
                // A real null pointer: a pointer to a zeroed descriptor hangs Emscripten's wgpuCreateInstance.
                _instance = _api.CreateInstance((InstanceDescriptor*)null);
#else
                ManaWgpuNativeTrace.EnableIfRequested();
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
#endif
                if (_instance == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU instance.");
                }

                ManaConsole.WriteDiagnostic("Creating WebGPU surface.");
#if MANA_BROWSER
                Console.WriteLine("[ManaWebGpuContext] Calling CreateFromCanvas...");
                _surface = ManaBrowserWebGpuSurface.CreateFromCanvas(window, _api, _instance);
                Console.WriteLine($"[ManaWebGpuContext] CreateFromCanvas returned: {(nint)_surface:X}");
#else
                _surface = ManaWebGpuNativeHost.Current is { } host
                    ? host.CreateSurface(_api, _instance)
                    : WebGPUSurface.CreateWebGPUSurface(window, _api, _instance);
#endif
                if (_surface == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU surface.");
                }

#if !MANA_BROWSER
                ManaConsole.WriteDiagnosticElapsed("Requesting WebGPU adapter.");
                RequestAdapter(requestedBackend);
                ManaConsole.WriteDiagnosticElapsed("Adapter granted; requesting WebGPU device.");
                RequestDevice();
                ManaConsole.WriteDiagnosticElapsed("Device granted.");
                FinishInitialization(requestedBackend, window);
#endif
                // MANA_BROWSER: the adapter and device arrive asynchronously; callers await
                // CompleteBrowserInitializationAsync (BrowserAsync.cs) before using the context.
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void FinishInitialization(string? requestedBackend, IWindow window)
        {
            ManaConsole.WriteDiagnostic("Configuring WebGPU queue and surface.");
            _queue = _api.DeviceGetQueue(_device);
#if !MANA_BROWSER
            if (!_api.TryGetDeviceExtension(_device, out _wgpuNativeExtension))
            {
                ManaConsole.WriteDiagnostic("WGPU native extension unavailable; falling back to InstanceProcessEvents for async GPU callback pumping.");
            }
            else
            {
                PublishResourceReport();
            }
#endif
#if !MANA_BROWSER
            _api.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(&OnError), null);
#else
            // Not in the browser: the callback goes through the dynCall path that fails there, and the browser
            // prints validation errors to the console anyway.
#endif
            _surfaceFormat = _api.SurfaceGetPreferredFormat(_surface, _adapter);
#if MANA_BROWSER
            // The canvas is configured with the sRGB view format, matching desktop's sRGB surface.
            _surfaceFormat = _surfaceFormat switch
            {
                TextureFormat.Bgra8Unorm => TextureFormat.Bgra8UnormSrgb,
                TextureFormat.Rgba8Unorm => TextureFormat.Rgba8UnormSrgb,
                _ => _surfaceFormat
            };
#endif
            _gpu = new ManaWebGpuDevice(_api, _device, _queue, _surfaceFormat, _depthFormat);
            _meshTextures = new ManaWebGpuMeshTextureCache(_gpu);
            _environment = new ManaWebGpuEnvironment(_gpu, EnvironmentLoading);
            _meshes = new ManaWebGpuSceneMeshes(_gpu, _meshTextures, _environment);
            _frameCapture = new ManaWebGpuFrameCapture(_gpu, _readbackRegistry);
            _ui = new ManaWebGpuUiCompositor(_gpu);
            HeightSampler = new ManaWebGpuHeightSampleCompute(_gpu, _readbackRegistry, _renderThread);
            ConfigureSurface(window.FramebufferSize);
            if (_renderSceneProvider != null || IsDemoMeshPipelineEnabled())
            {
                if (CanCreateDemoMeshPipeline(requestedBackend))
                {
                    try
                    {
                        // Synchronous shader compilation, before the first frame can present.
                        ManaConsole.WriteDiagnosticElapsed("Creating scene mesh pipeline (shader compile).");
                        CreateScenePipelines();
                        ManaConsole.WriteDiagnosticElapsed("Scene mesh pipeline ready.");
                    }
                    catch (Exception ex)
                    {
                        ManaConsole.WriteDiagnostic("WebGPU mesh pipeline failed; continuing with clear-only renderer.");
                        ManaConsole.WriteDiagnostic(ex);
                        ReleaseScenePipelines();
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
#if !MANA_BROWSER
            LogWebGpuDiagnostics(requestedBackend, _instanceBackend);
#endif
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            ConfigureSurface(framebufferSize);
        }

        private void CreateScenePipelines()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU mesh pipeline.");

            var renderScene = _renderSceneProvider?.Invoke() ?? ManaWebGpuDemoScene.Create();

            _activeRenderScene = renderScene;
            // The frame group binds the shadow map, so the map comes first.
            _shadowMap = new ManaWebGpuShadowMap(_gpu);
            _bindings = new ManaWebGpuSceneBindings(_gpu, _shadowMap.ArrayView, _shadowMap.Sampler);
            // Before the shadows, as before anything samples the reflection cubes.
            AddPass(new ManaWebGpuEnvironmentCapturePass(_gpu, _environment!, () => UseSceneCapture));
            AddPass(new ManaWebGpuShadowPass(_gpu, _bindings, _shadowMap));
            CreateHdrPostProcessor();
            var sceneColor = GetSceneColorFormat();
            // First, so ordinary depth-tested geometry draws over it: the sky is a background.
            AddPass(new ManaWebGpuSkyPass(_gpu));
            AddPass(new ManaWebGpuMaterialPass(_gpu, _bindings, sceneColor));
            _particlePass = AddPass(new ManaWebGpuParticlePass(_gpu, sceneColor));
            AddPass(new ManaWebGpuWireframeOverlay(_gpu, _bindings, sceneColor));
            AddPass(new ManaWebGpuNormalLinesOverlay(_gpu, _bindings, sceneColor));
            AddPass(new ManaWebGpuOutlinePass(_gpu, _bindings));

            ManaConsole.WriteDiagnostic("WebGPU mesh pipeline ready.");
            if (renderScene.Meshes.Count > 0)
            {
                _meshes!.Upload(renderScene, _bindings);
            }
        }

        private void CreateHdrPostProcessor()
        {
            if (_hdrPostProcessor != null)
            {
                return;
            }

            var shaderPath = ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("hdr_tonemap.wgsl"));
            _hdrPostProcessor = new ManaWebGpuHdrPostProcessor(_gpu, _surfaceFormat, shaderPath);
            _hdrPostProcessor.Resize(_framebufferSize);
        }

        private TextureFormat GetSceneColorFormat()
        {
            return _hdrPostProcessor?.SceneFormat ?? _surfaceFormat;
        }

        // Shutdown and failed pipeline initialization both come through here; anything may still be null.
        private void ReleaseScenePipelines()
        {
            _hdrPostProcessor?.Dispose();
            _hdrPostProcessor = null;
            _thumbnails?.Dispose();
            _thumbnails = null;

            // Meshes first: they bind the environment and shadow views the owners below release.
            _meshes?.ReleaseAll(immediate: true);
            // Passes build over the shared bindings, and the frame group binds the shadow map.
            ReleasePasses();
            _bindings?.Dispose();
            _bindings = null;
            _shadowMap?.Dispose();
            _shadowMap = null;
            _environment?.Release();
        }

        private void ConfigureSurface(Vector2D<int> framebufferSize)
        {
            if (_device == null || _surface == null || framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            {
                return;
            }

            if (_configured)
            {
                _api.SurfaceUnconfigure(_surface);
            }

            var configuration = new SurfaceConfiguration
            {
                Device = _device,
                Format = _surfaceFormat,
                // CopySrc so the UI's blur can snapshot the frame; free unless used.
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y,
                PresentMode = PresentMode.Fifo,
                AlphaMode = CompositeAlphaMode.Opaque
            };

            _api.SurfaceConfigure(_surface, &configuration);
            _framebufferSize = framebufferSize;
            CreateDepthResources(framebufferSize);
            _hdrPostProcessor?.Resize(framebufferSize);
            _configured = true;
        }

        private void ReleaseDepthResources()
        {
            _handleDepth?.Dispose();
            _handleDepth = null;
            _depth?.Dispose();
            _depth = null;
        }

        private void CreateDepthResources(Vector2D<int> framebufferSize)
        {
            ReleaseDepthResources();

            var width = (uint)framebufferSize.X;
            var height = (uint)framebufferSize.Y;
            // Assigned one at a time: if the second cannot be made the first is still released with the rest.
            _depth = new ManaWebGpuDepthTarget(_gpu, width, height, "depth");
            _handleDepth = new ManaWebGpuDepthTarget(_gpu, width, height, "handle depth");
        }

        /// <summary>
        /// Activates a loaded environment map at the frame boundary. Meshes bound to the old map are rebuilt: a provider's
        /// scene on its next refresh, a provider-less demo scene here.
        /// </summary>
        public void CheckPendingEnvironmentMap()
        {
            if (_disposed || _environment == null || !_environment.ActivatePending(_activeRenderScene))
            {
                return;
            }

            _meshes?.ReleaseAll();
            if (_renderSceneProvider == null && _activeRenderScene != null && _bindings != null)
            {
                _meshes!.Upload(_activeRenderScene, _bindings);
            }
        }

        // Also while scene work is suspended: the loading screen waits on the environment map.
        private void EnsureEnvironmentInitializationStarted()
        {
            _activeRenderScene ??= _renderSceneProvider?.Invoke();
            if (_activeRenderScene != null)
            {
                _environment?.EnsureMap(_activeRenderScene);
            }
        }

        private void RefreshRenderScene()
        {
            var renderScene = _renderSceneProvider?.Invoke();
            // Without scene pipelines there is nothing to draw meshes with, or to build their bindings from.
            if (renderScene == null || _bindings == null)
            {
                return;
            }

            IReadOnlyList<ManaRenderMeshInstance> renderableMeshes;
            using (ManaFrameProfiler.Measure("GPU: Gather Renderables"))
            {
                renderableMeshes = ManaWebGpuSceneMeshes.GetRenderableMeshes(renderScene);
            }

            _activeRenderScene = renderScene;
            using (ManaFrameProfiler.Measure("GPU: Reconcile Scene Meshes"))
            {
                _meshes!.Reconcile(renderableMeshes, renderScene, _bindings!);
            }
        }

        private T AddPass<T>(T pass) where T : ManaWebGpuRenderPass
        {
            _passes.Add(pass);
            return pass;
        }

        private void ReleasePasses()
        {
            for (var i = _passes.Count - 1; i >= 0; i--)
            {
                _passes[i].Dispose();
            }
            _passes.Clear();
        }

        // Null while the renderer is clear-only or has no scene yet.
        private ManaWebGpuFrame? PassFrame(CommandEncoder* encoder)
        {
            if (_bindings == null || _activeRenderScene == null)
            {
                return null;
            }

            _passFrame.Encoder = encoder;
            _passFrame.Scene = _activeRenderScene;
            _passFrame.Meshes = _meshes!.Meshes;
            _passFrame.Bindings = _bindings;
            _passFrame.SceneColorFormat = GetSceneColorFormat();
            _passFrame.Depth = _depthTextureView;
            _passFrame.HandleDepth = _handleDepthTextureView;
            _passFrame.Projection = _activeRenderScene.Camera.Projection;
            _passFrame.Time = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            _passFrame.SceneTarget = null;
            _passFrame.OutlineMask = null;
            var environment = _environment?.Map;
            _passFrame.EnvironmentMap = environment == null ? null : environment.EnvironmentMapResource.TextureView;
            _passFrame.SceneSuspended = SuspendSceneWork;
            return _passFrame;
        }

        /// <summary>
        /// Renders <paramref name="instances"/> to a square straight-alpha RGBA buffer, or null when there is nothing to
        /// draw. Callable from any thread.
        /// </summary>
        public Task<byte[]?> RenderThumbnailAsync(IReadOnlyList<ManaRenderMeshInstance> instances, int thumbnailSize)
        {
            if (_disposed || thumbnailSize <= 0)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _renderThread.Enqueue(() =>
            {
                if (_disposed)
                {
                    completion.TrySetResult(null);
                    return;
                }

                if (_bindings == null)
                {
                    completion.TrySetException(new InvalidOperationException("WebGPU scene pipelines are unavailable for thumbnails."));
                    return;
                }

                _thumbnails ??= new ManaWebGpuThumbnailRenderer(_gpu, _readbackRegistry, _meshTextures);
                _thumbnails.EncodeAndSubmit(instances, (uint)thumbnailSize, _bindings, completion);
            });
            return completion.Task;
        }

        /// <summary>Saves the next frame as a PNG; <paramref name="withoutUi"/> leaves ManaUI out. One at a time.</summary>
        public Task CaptureFrameAsync(string path, bool withoutUi = false)
        {
            return _disposed || _frameCapture == null
                ? Task.FromCanceled(new CancellationToken(true))
                : _frameCapture.CaptureFrameAsync(path, withoutUi);
        }

        /// <summary>Writes the reflection cube's faces to <c>{prefix}.face{0-5}.png</c> after the next frame.</summary>
        public void DumpEnvironmentFaces(string prefix)
        {
            if (!_disposed)
            {
                _frameCapture?.DumpEnvironmentFaces(prefix);
            }
        }

#if !MANA_BROWSER
        /// <summary>A UI-only presentation surface for a native panel window, sharing this device and queue.</summary>
        public ManaUI.IUiWindowRenderer CreateUiSurface(IWindow window)
        {
            var surface = new ManaWebGpuPanelSurface(_gpu, _instance, _adapter, window, closed => _panelSurfaces.Remove(closed));
            _panelSurfaces.Add(surface);
            return surface;
        }
#endif

        private bool _loggedFirstPresent;
        private bool _loggedFirstSceneWork;

        public void ClearFrame(Color clearColor)
        {
            // Pumps readback callbacks every frame, before the early returns, so a minimized window never stalls one.
#if MANA_BROWSER
            _api.InstanceProcessEvents(_instance);
#else
            if (_wgpuNativeExtension != null)
            {
                _wgpuNativeExtension.DevicePoll(_device, false, null);
            }
            else
            {
                _api.InstanceProcessEvents(_instance);
            }
#endif

            _meshes?.BeginFrame();

            // Before the early return, so deferred work progresses while minimized; before this frame's submit, so it never races it.
            _renderThread.Drain();

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

                // Acquire can fail during startup or resize; reconfigure and retry next frame.
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
            // Not `?.`: a pointer cannot be nullable (CS8978), and some compilers miss that.
            var hdrSceneTextureView = _hdrPostProcessor != null ? _hdrPostProcessor.SceneTextureView : null;
            if (textureView == null)
            {
                _api.TextureRelease(surfaceTexture.Texture);

                return;
            }

            CommandEncoder* commandEncoder = null;
            RenderPassEncoder* sceneRenderPass = null;
            RenderPassEncoder* toneMapRenderPass = null;
            CommandBuffer* commandBuffer = null;
            ManaWebGpuFrameCapture.EncodedCopies? copies = null;
            try
            {
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
                commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
                var collectStopwatch = Stopwatch.StartNew();

                // See SuspendSceneWork: during a fast load this is where the frame would stall uploading.
                if (!SuspendSceneWork)
                {
                    if (!_loggedFirstSceneWork)
                    {
                        _loggedFirstSceneWork = true;

                        // Marks when the scene starts uploading behind the loading screen.
                        ManaConsole.WriteDiagnosticElapsed("First unsuspended RefreshRenderScene (scene upload resumes).");
                    }

                    RefreshRenderScene();
                }

                _shadowMap?.UpdateCascades(_activeRenderScene);
                // The bindings are made with the shadow map, so it exists whenever they do.
                if (_activeRenderScene != null && _bindings != null)
                {
                    _bindings.WriteFrame(_queue, ManaWebGpuFrameUniform.For(
                        _activeRenderScene, _shadowMap!, (float)(DateTime.UtcNow - _startTime).TotalSeconds));
                }
                LastCollectMs = collectStopwatch.Elapsed.TotalMilliseconds;

                // Not gated on SuspendSceneWork: loading waits on the environment map, so it must start meanwhile.
                EnsureEnvironmentInitializationStarted();

                var frame = PassFrame(commandEncoder);
                if (frame != null && !SuspendSceneWork)
                {
                    foreach (var pass in _passes)
                    {
                        using (ManaFrameProfiler.Measure(pass.ProfileName))
                        {
                            pass.Prepare(frame);
                        }
                    }
                }

                sceneRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &sceneRenderPassDescriptor);
                var drawStopwatch = Stopwatch.StartNew();

                if (frame != null)
                {
                    frame.SceneTarget = sceneTargetView;
                    frame.SceneSuspended = SuspendSceneWork;
                    frame.OutlineMask = useHdrPost ? _hdrPostProcessor!.OutlineMaskView : null;
                }

                if (!SuspendSceneWork)
                {
                    if (frame != null)
                    {
                        foreach (var pass in _passes)
                        {
                            pass.DrawScene(frame, sceneRenderPass);
                        }
                    }
                }

                _api.RenderPassEncoderEnd(sceneRenderPass);
                if (frame != null)
                {
                    foreach (var pass in _passes)
                    {
                        pass.AfterScene(frame);
                    }
                }
                LastDrawMs = drawStopwatch.Elapsed.TotalMilliseconds;

                if (useHdrPost)
                {
                    using (ManaFrameProfiler.Measure("GPU: Outline + HDR Post"))
                    {
                        _hdrPostProcessor?.Process(commandEncoder);
                    }
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

                toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
                using (ManaFrameProfiler.Measure("GPU: Tone Map"))
                {
                    _hdrPostProcessor?.Draw(toneMapRenderPass);
                }

                // Blur needs the finished frame, which the pass is writing: end it, copy and reduce the frame, then draw
                // the UI in a second pass. Only when something asked for blur.
                if (ManaWebGpuUiCompositor.WantsBackdrop(UiRoot))
                {
                    _api.RenderPassEncoderEnd(toneMapRenderPass);
                    _api.RenderPassEncoderRelease(toneMapRenderPass);
                    toneMapRenderPass = null;
                    _ui!.CaptureBackdrop(commandEncoder, surfaceTexture.Texture, _framebufferSize);

                    surfaceColorAttachment.LoadOp = LoadOp.Load;
                    toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
                }

                // The UI draws last, over the tone-mapped scene.
                using (ManaFrameProfiler.Measure("GPU: Draw ManaUI"))
                {
                    // Left out of a clean screenshot: the scene across the whole window.
                    if (UiRoot is { } root && _frameCapture?.WithoutUi != true && !_ui!.Draw(toneMapRenderPass, root, _framebufferSize))
                    {
                        UiRoot = null;
                    }
                }

                _api.RenderPassEncoderEnd(toneMapRenderPass);

                copies = _frameCapture?.Encode(commandEncoder, surfaceTexture.Texture, _framebufferSize, _environment?.Map);

                var commandBufferDescriptor = new CommandBufferDescriptor();
                var submitStopwatch = Stopwatch.StartNew();
                commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                _api.QueueSubmit(_queue, 1, &commandBuffer);
                _api.SurfacePresent(_surface);

                // Submitted: each staging buffer now belongs to its readback.
                if (copies is { } submitted)
                {
                    copies = null;
                    _frameCapture!.Finish(submitted);
                }

                if (!_loggedFirstPresent)
                {
                    _loggedFirstPresent = true;

                    // The first frame actually handed to the window.
                    ManaConsole.WriteDiagnosticElapsed("First frame presented to the window.");
                }

#if MANA_BROWSER
                _api.InstanceProcessEvents(_instance);
#else
                if (_wgpuNativeExtension != null)
                {
                    _wgpuNativeExtension.DevicePoll(_device, false, null);
                }
                else
                {
                    _api.InstanceProcessEvents(_instance);
                }
#endif

                LastSubmitMs = submitStopwatch.Elapsed.TotalMilliseconds;
                LastFrameTotalMs = frameStopwatch.Elapsed.TotalMilliseconds;
            }
            finally
            {
                // A frame that throws mid-encode is abandoned, never submitted: its copies settle here.
                if (copies is { } unsubmitted)
                {
                    _frameCapture!.Abandon(unsubmitted);
                }

                if (commandBuffer != null)
                {
                    _api.CommandBufferRelease(commandBuffer);
                }
                if (sceneRenderPass != null)
                {
                    _api.RenderPassEncoderRelease(sceneRenderPass);
                }
                if (toneMapRenderPass != null)
                {
                    _api.RenderPassEncoderRelease(toneMapRenderPass);
                }
                if (commandEncoder != null)
                {
                    _api.CommandEncoderRelease(commandEncoder);
                }
                _api.TextureViewRelease(textureView);
                _api.TextureRelease(surfaceTexture.Texture);
            }

            // The profiler keeps the peak, where a hitch shows.
            ManaFrameProfiler.Record("GPU: Scene Collect", LastCollectMs);
            ManaFrameProfiler.Record("GPU: Draw Meshes", LastDrawMs);
            ManaFrameProfiler.Record("GPU: Submit + Present", LastSubmitMs);
            ManaFrameProfiler.Record("GPU: ClearFrame Total", LastFrameTotalMs);
        }

        public void Dispose()
        {
            if (_disposed) return;
#if !MANA_BROWSER
            foreach (var surface in _panelSurfaces.ToArray())
            {
                surface.Dispose();
            }
#endif
            // Retire readbacks while the device is live; late native callbacks see no registered request.
            _disposed = true;
#if !MANA_BROWSER
            RetractResourceReport();
#endif
            _frameCapture?.Close();
#if MANA_BROWSER
            CancelBrowserInitialization();
#endif
            _environment?.Retire();
            // Before FaultAllPending: an item that slipped through can still register a readback.
            _renderThread.Close();
            _readbackRegistry.FaultAllPending();
            HeightSampler?.Dispose();

            _meshes?.DrainDeferred(force: true);

            UiRoot = null;
            _ui?.Dispose();

            ReleaseScenePipelines();
            // After every mesh and thumbnail has returned its references.
            _meshTextures?.Dispose();

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
#if !MANA_BROWSER
            // A view over _api's library for the released device, not a separate owner.
            _wgpuNativeExtension = null;
#endif

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

            // _api and _wgpuNativeExtension stay: the native library outlives the context.
        }
    }
}
