using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : ManaGraphicsDiagnosticsProvider, IDisposable
    {
        public bool UseSceneCapture = false;

        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly IInputContext? _input;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
#if !MANA_BROWSER
        private readonly RequestAdapterCallback _requestAdapterCallback;
        private readonly RequestDeviceCallback _requestDeviceCallback;
#endif
        private readonly ErrorCallback _errorCallback;
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
        // Resolved once, right after _device/_queue exist -- wgpu-native's own extension for pumping
        // async callbacks (BufferMapAsync, QueueOnSubmittedWorkDone) to actually fire. Falls back to the
        // portable InstanceProcessEvents if this build's WebGPU implementation doesn't expose it.
        private Wgpu? _wgpuNativeExtension;
#endif
        private readonly ManaWebGpuReadbackRegistry _readbackRegistry = new();
        private volatile bool _disposed;
        private Texture* _depthTexture;
        private TextureView* _depthTextureView;
        private Texture* _handleDepthTexture;
        private TextureView* _handleDepthTextureView;
        private TextureFormat _surfaceFormat;
        private readonly TextureFormat _depthFormat = TextureFormat.Depth24Plus;
        private Vector2D<int> _framebufferSize;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _configured;
        private readonly List<ManaWebGpuSceneMesh> _sceneMeshes = new();
        private readonly Dictionary<string, ManaWebGpuSceneMesh> _sceneMeshMap = new();
        private readonly Queue<DeferredSceneMeshDisposal> _deferredSceneMeshDisposals = new();
        // Work handed in from background threads that must run on the render thread instead -- see
        // EnqueueRenderThreadWork for why submitting from anywhere else deadlocks wgpu-native.
        private readonly ConcurrentQueue<Action> _renderThreadWork = new();
        // Not readonly: set from FinishInitialization, which for MANA_BROWSER runs after the
        // constructor returns (see ManaWebGpuContext.BrowserAsync.cs), not inside it.
        private ManaWebGpuMeshTextureCache _meshTextures = null!;
        private ManaRenderScene? _activeRenderScene;
        private ulong _frameSerial;
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
        private RenderPipeline* _outlineMaskAlwaysOnTopPipeline;
        // Debug overlays drawn after the main mesh pass; add a ManaWebGpuOverlayPass subclass and list it
        // here. Pipelines are keyed by overlay name and built on first use.
        private readonly ManaWebGpuOverlayPass[] _overlayPasses =
        {
            new ManaWebGpuWireframeOverlay(),
            new ManaWebGpuNormalLinesOverlay()
        };
        private readonly Dictionary<string, nint> _overlayPipelines = new(StringComparer.Ordinal);
        private ManaWebGpuHdrPostProcessor? _hdrPostProcessor;
        private ManaWebGpuEnvironmentPrefilterCompute? _environmentPrefilterCompute;
        private ManaWebGpuHeightSampleCompute? _heightSampleCompute;
        private Texture* _sceneCaptureDepthTexture;
        private TextureView* _sceneCaptureDepthTextureView;
        private uint _sceneCaptureDepthSize;
        private Sampler* _demoSampler;
        private Sampler* _clampSampler;
        private Sampler* _nearestSampler;
        private ManaWebGpuEnvironmentReflectionMap? _environmentReflectionMap;
        private int _framesSinceEnvironmentCapture = int.MaxValue;
        private readonly Dictionary<string, nint> _shaderModules = new();
        private readonly Dictionary<string, nint> _pipelines = new();
#if !MANA_BROWSER
#endif
        private ManaWebGpuUiRenderer? _uiRenderer;
        private ManaWebGpuSkybox? _skybox;
        private ManaWebGpuSkybox? _environmentSkybox;

        /// <summary>
        /// The ManaUI tree to draw over the scene, or null when the host has not built one.
        /// <para>
        /// The app layer owns the root and records its draw list during its own render phase; this context
        /// only submits whatever was last recorded. Keeping layout out of the present path means a UI bug
        /// cannot stall the GPU timeline, and it keeps the dependency pointing the right way -- ManaGraphics
        /// knows about ManaUI, never about Mana.
        /// </para>
        /// </summary>
        public ManaUI.UiRoot? UiRoot { get; set; }

        /// <summary>
        /// Skips walking, uploading and drawing the scene for one frame -- everything else (clear, tone
        /// map, ManaUI) still runs.
        /// <para>
        /// Set by the app layer, the same way <see cref="UiRoot"/> is, rather than this context knowing
        /// what "loading" means: OpenGL already never touches the scene while a game mode is loading
        /// (BaseGameMode.Render's IsLoading branch simply does not call DrawWorld), but this backend's
        /// equivalent, RefreshRenderScene, ran unconditionally every frame with no such gate. On a load
        /// fast enough to add most of a scene within the first couple of seconds, the very first frame that
        /// could have presented a loading screen instead spent over a second uploading meshes and compiling
        /// shaders for content nobody could see yet, arriving late enough that the screen was visible for
        /// only the tail end of the load it was supposed to cover. This is that gate.
        /// </para>
        /// </summary>
        public bool SuspendSceneWork { get; set; }

        // Per-frame CPU-side timing for the last completed ClearFrame call, in milliseconds.
        // Read by the app layer (which owns Mana/ManaWorld) once per frame to feed a live perf HUD —
        // ManaGraphics can't call into Mana directly (wrong dependency direction), so it's exposed
        // here instead of pushed.
        public double LastCollectMs { get; private set; }
        public double LastDrawMs { get; private set; }
        public double LastSubmitMs { get; private set; }
        public double LastFrameTotalMs { get; private set; }

        private readonly record struct DeferredSceneMeshDisposal(ulong DisposeAfterFrame, ManaWebGpuSceneMesh SceneMesh);

#if !MANA_BROWSER
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WgpuLogCallback(WgpuLogLevel level, string message, nint userdata);

        private enum WgpuLogLevel : int
        {
            Error = 1,
            Warn = 2,
            Info = 3,
            Debug = 4,
            Trace = 5,
        }

        [DllImport("wgpu_native", EntryPoint = "wgpuSetLogLevel", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void WgpuSetLogLevel(WgpuLogLevel level);

        [DllImport("wgpu_native", EntryPoint = "wgpuSetLogCallback", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void WgpuSetLogCallback(WgpuLogCallback callback, nint userdata);

        private static readonly WgpuLogCallback NativeLogCallback = OnNativeLog;
#endif

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
            _api = WebGPU.GetApi();
#endif
#if !MANA_BROWSER
            _requestAdapterCallback = OnRequestAdapter;
            _requestDeviceCallback = OnRequestDevice;
#endif
            _errorCallback = OnError;

            ManaConsole.WriteDiagnostic("Creating WebGPU instance.");
#if MANA_BROWSER
            // No wgpu-native-style backend selection in-browser -- the browser's own WebGPU
            // implementation picks its backend internally. Deliberately a real null pointer, NOT
            // `&(new InstanceDescriptor())`: a pointer to a zeroed-but-real struct silently hung
            // forever inside Emscripten's legacy wgpuCreateInstance (confirmed by isolating the
            // exact call via a raw function pointer probe -- null succeeded instantly, a pointer to
            // an identical zeroed struct never returned). Matches the M0 spike's own
            // wgpuCreateInstance(NULL) call, which is the only shape ever actually proven to work.
            _instance = _api.CreateInstance((InstanceDescriptor*)null);
#else
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MANA_WGPU_NATIVE_TRACE")))
            {
                EnableNativeBackendTracing();
            }
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
            _surface = WebGPUSurface.CreateWebGPUSurface(window, _api, _instance);
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
            // MANA_BROWSER: adapter/device request is genuinely asynchronous in a real browser
            // (navigator.gpu.requestAdapter() resolves on a JS microtask, not synchronously within
            // this call the way wgpu-native's desktop implementation does) -- a constructor can't
            // await that, and this whole class is declared `unsafe`, which C# doesn't allow an
            // `await` to appear within at all (regardless of whether the specific method touches a
            // pointer). See ManaWebGpuContext.BrowserAsync.cs's CompleteBrowserInitializationAsync,
            // a separate non-unsafe partial declaration of this same class, which callers must
            // await before this context is usable.
        }

        private void FinishInitialization(string? requestedBackend, IWindow window)
        {
            ManaConsole.WriteDiagnostic("Configuring WebGPU queue and surface.");
            _queue = _api.DeviceGetQueue(_device);
            _meshTextures = new ManaWebGpuMeshTextureCache(_api, _device, _queue);
#if !MANA_BROWSER
            if (!_api.TryGetDeviceExtension(_device, out _wgpuNativeExtension))
            {
                ManaConsole.WriteDiagnostic("WGPU native extension unavailable; falling back to InstanceProcessEvents for async GPU callback pumping.");
            }
#endif
#if !MANA_BROWSER
            _api.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
#else
            // Skipped in the browser build: this callback's delivery goes through the same
            // getWasmTableEntry-dependent dynCall path as the adapter/device request callbacks
            // that ManaBrowserWebGpuCallbackBridge already had to bypass (see mana_webgpu.c),
            // and hitting it here just spams "getWasmTableEntry(...) is not a function" on every
            // validation error instead of ever calling _errorCallback. Not worth its own EM_JS
            // bridge: the browser's real WebGPU implementation already prints validation errors
            // straight to the devtools console on its own, unconditionally, regardless of
            // whether an uncaptured-error callback is registered at all.
#endif
            _surfaceFormat = _api.SurfaceGetPreferredFormat(_surface, _adapter);
#if MANA_BROWSER
            // getPreferredCanvasFormat() is always non-sRGB, but the canvas is configured with the
            // sRGB variant in viewFormats (see mana_configure_surface_impl), so everything that
            // renders to the surface targets the sRGB view and gets the linear-to-sRGB transfer on
            // write -- matching desktop, where wgpu-native hands back an Srgb surface directly.
            _surfaceFormat = _surfaceFormat switch
            {
                TextureFormat.Bgra8Unorm => TextureFormat.Bgra8UnormSrgb,
                TextureFormat.Rgba8Unorm => TextureFormat.Rgba8UnormSrgb,
                _ => _surfaceFormat
            };
#endif
            ConfigureSurface(window.FramebufferSize);
            if (_renderSceneProvider != null || IsDemoMeshPipelineEnabled())
            {
                if (CanCreateDemoMeshPipeline(requestedBackend))
                {
                    try
                    {
                        // Shader compilation, synchronous, on this thread -- another candidate for time
                        // spent before the first frame can present, alongside the adapter/device request.
                        ManaConsole.WriteDiagnosticElapsed("Creating scene mesh pipeline (shader compile).");
                        CreateDemoMeshPipeline();
                        ManaConsole.WriteDiagnosticElapsed("Scene mesh pipeline ready.");
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
#if !MANA_BROWSER
            LogWebGpuDiagnostics(requestedBackend, _instanceBackend);
#endif
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            ConfigureSurface(framebufferSize);
        }

        /// <summary>
        /// Submits the ManaUI draw list recorded by the app this frame. Created lazily so a project that
        /// never assigns <see cref="UiRoot"/> pays nothing for the pipeline or its atlas texture.
        /// </summary>
        private ManaWebGpuUiBackdrop? _uiBackdrop;

        /// <summary>
        /// Whether anything in this frame's UI asked for a blurred backdrop. Read before the pass is split,
        /// because splitting it is the cost being avoided.
        /// </summary>
        private bool WantsBackdrop()
        {
            ManaUI.UiBackdropStats.Supported = true;

            if (UiRoot?.DrawList.BackdropRequested != true)
            {
                return false;
            }

            ManaUI.UiBackdropStats.Requested++;
            return true;
        }

        /// <summary>
        /// Skipped whenever there is nothing sensible to draw yet -- no active scene, no environment map --
        /// which is the same state a frame is in for the first few frames after startup while the real
        /// environment is still loading in the background. The scene pass's own clear is what shows through
        /// on those frames, exactly as it always has.
        /// </summary>
        private void DrawSkybox(RenderPassEncoder* renderPass)
        {
            if (_activeRenderScene == null || _clampSampler == null)
            {
                return;
            }

            var environment = _environmentReflectionMap;
            if (environment == null || environment.EnvironmentMapResource.TextureView == null)
            {
                return;
            }

            _skybox ??= new ManaWebGpuSkybox(
                _api,
                _device,
                _queue,
                _depthFormat,
                ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "skybox.wgsl")),
                moonPath: MoonTexturePath);

            _skybox.Draw(
                renderPass,
                GetSceneColorFormat(),
                _clampSampler,
                environment.EnvironmentMapResource.TextureView,
                _activeRenderScene.Camera,
                GetAspectCorrectedProjection(_activeRenderScene.Camera),
                SkyTowardSun(_activeRenderScene),
                _activeRenderScene.Sky,
                ManaSky.Seconds);
        }

        private static string? MoonTexturePath => TryResolveAssetPath(Path.Combine("Assets", "Textures", "Sky", "moon_lroc_1k.jpg"));

        // A still sky's sun sits where the scene's own sun is, if it has one (its direction is the way the light
        // travels); a day-night cycle moves its own.
        private static Vector3 SkyTowardSun(ManaRenderScene scene)
        {
            return scene.HasSun && scene.Sky.Mode != ManaSkyMode.DayNightCycle ? -scene.SunDirection : scene.Sky.TowardSun;
        }

        private void CaptureUiBackdrop(CommandEncoder* commandEncoder, Texture* frame)
        {
            _uiBackdrop ??= new ManaWebGpuUiBackdrop(_api, _device, _surfaceFormat);
            _uiBackdrop.Capture(commandEncoder, frame, _window.FramebufferSize);

            ManaUI.UiBackdropStats.Captured++;
            _uiRenderer?.SetBackdrop(_uiBackdrop.View);
        }

        private void DrawManaUi(RenderPassEncoder* renderPass)
        {
            var root = UiRoot;
            // The scene draws across the whole window under the panels, so leaving them out is a clean picture.
            if (root == null || _pendingScreenshot is { WithoutUi: true })
            {
                return;
            }

            if (_uiRenderer == null)
            {
                try
                {
                    // Another lazily-built pipeline, and another candidate for the gap between "the loading
                    // screen could show" and "the loading screen actually did": this is the first time
                    // anything asked to draw ManaUI at all, which for a mode with the loading overlay is
                    // the very first loading frame.
                    ManaConsole.WriteDiagnosticElapsed("Creating the ManaUI renderer (first draw request).");
                    _uiRenderer = new ManaWebGpuUiRenderer(_api, _device, _queue, _surfaceFormat);
                    ManaConsole.WriteDiagnosticElapsed("ManaUI renderer ready; submitting its first draw list.");
                }
                catch (Exception ex)
                {
                    // A failed UI pipeline must not take the whole renderer down; drop the root so the
                    // failure is reported once rather than every frame.
                    ManaConsole.WriteDiagnostic($"ManaUI renderer failed to initialise, disabling it: {ex.Message}");
                    UiRoot = null;
                    return;
                }
            }

            // Bound here as well as at capture time: the renderer is built lazily on its first frame, which
            // is after the capture ran, so a workspace that opens with blur already on would otherwise show
            // one frame with nothing behind it.
            if (_uiBackdrop != null)
            {
                _uiRenderer.SetBackdrop(_uiBackdrop.View);
                ManaUI.UiBackdropStats.Bound = _uiBackdrop.View != null;
            }

            var framebufferSize = _window.FramebufferSize;
            _uiRenderer.Render(
                renderPass,
                root.DrawList,
                root.Atlas,
                root.Images,
                root.Size,
                (uint)Math.Max(0, framebufferSize.X),
                (uint)Math.Max(0, framebufferSize.Y));
        }

#if !MANA_BROWSER
        // Opt-in only (MANA_WGPU_NATIVE_TRACE env var) -- Trace is the noisiest level across the whole
        // wgpu-native stack (wgpu_core/wgpu_hal/wgpu), and the native log callback fires synchronously,
        // routed through ManaLog's single global lock, for every internal wgpu-native operation on
        // whatever thread wgpu-native's own Rust internals call it from. With multiple threads submitting
        // GPU work concurrently (e.g. PlanetNode's background split doing several GPU compute dispatches
        // while the main thread renders), that produces enough simultaneous native->managed callback
        // traffic to make the app appear to hang at the next SurfacePresent/QueueSubmit -- confirmed as
        // the cause of an intermittent "deadlock on patch click" report. Only enable this for deep
        // GPU-driver debugging, not by default.
        private static void EnableNativeBackendTracing()
        {
            try
            {
                Environment.SetEnvironmentVariable("RUST_LOG", "wgpu_core=trace,wgpu_hal=trace,wgpu=trace");
                Environment.SetEnvironmentVariable("RUST_BACKTRACE", "1");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Failed to configure Rust tracing environment variables: {ex.Message}");
            }

            try
            {
                WgpuSetLogLevel(WgpuLogLevel.Trace);
                WgpuSetLogCallback(NativeLogCallback, IntPtr.Zero);
                ManaConsole.WriteDiagnostic("[WebGPU] Enabled native backend tracing.");
            }
            catch (DllNotFoundException ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Native wgpu tracing library is unavailable: {ex.Message}");
            }
            catch (EntryPointNotFoundException ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Native wgpu tracing entry points are unavailable: {ex.Message}");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Failed to enable native backend tracing: {ex.Message}");
            }
        }

        private static void OnNativeLog(WgpuLogLevel level, string message, nint userdata)
        {
            var levelName = level switch
            {
                WgpuLogLevel.Error => "error",
                WgpuLogLevel.Warn => "warn",
                WgpuLogLevel.Info => "info",
                WgpuLogLevel.Debug => "debug",
                _ => "trace"
            };

            ManaConsole.WriteDiagnostic($"[wgpu:{levelName}] {message}");
        }
#endif

        // Callable from any thread; runs FIFO on the render thread via DrainRenderThreadWork.
        // wgpu-native takes device locks in opposite orders for surface-present and queue-submit, so a
        // background-thread submit overlapping SurfacePresent deadlocks both threads.
        private void EnqueueRenderThreadWork(Action work)
        {
            _renderThreadWork.Enqueue(work);

            // Dispose()'s drain may already have run; flush inline so a racing item still cancels.
            if (_disposed)
            {
                DrainRenderThreadWork();
            }
        }

        // Render thread only. Each item checks _disposed itself so Dispose()'s drain cancels rather
        // than submits.
        private void DrainRenderThreadWork()
        {
            while (_renderThreadWork.TryDequeue(out var work))
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic($"[WebGPU] Deferred render-thread work failed: {ex}");
                }
            }
        }

        // Backs IHeightSamplingComputeDevice.SampleHeightsAsync. Callable from any thread; only the
        // encode+submit is marshalled to the render thread (see EnqueueRenderThreadWork), the readback
        // still completes off it.
        public Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            if (_disposed)
            {
                return Task.FromCanceled<float[]>(new CancellationToken(true));
            }

            // RunContinuationsAsynchronously keeps caller continuations off the render thread mid-drain.
            var completion = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueRenderThreadWork(() =>
            {
                if (_disposed)
                {
                    completion.TrySetCanceled();
                    return;
                }

                try
                {
                    _heightSampleCompute ??= new ManaWebGpuHeightSampleCompute(
                        _api, _device, _queue, _readbackRegistry,
                        ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "height_sample_compute.wgsl")));

                    LinkTaskResult(
                        _heightSampleCompute.SampleHeightsAsync(lonLatPointsDegrees, tilesNewestFirst),
                        completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            return completion.Task;
        }

        // ContinueWith rather than await: this class is `unsafe` at the type level, which bans await.
        private static void LinkTaskResult<T>(Task<T> source, TaskCompletionSource<T> destination)
        {
            source.ContinueWith(
                static (completed, state) =>
                {
                    var target = (TaskCompletionSource<T>)state!;
                    if (completed.IsCanceled)
                    {
                        target.TrySetCanceled();
                    }
                    else if (completed.Exception != null)
                    {
                        target.TrySetException(completed.Exception.InnerExceptions);
                    }
                    else
                    {
                        target.TrySetResult(completed.Result);
                    }
                },
                destination,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private bool _loggedFirstPresent;
        private bool _loggedFirstSceneWork;

        public void ClearFrame(Color clearColor)
        {
            // Pumps any in-flight ManaWebGpuBufferReadback's native BufferMapAsync callback -- must run
            // every frame, unconditionally, before either early-return path below, so a pending readback
            // never stalls while the window is minimized/mid-resize/reconfiguring. Cheap (near-no-op) when
            // nothing's pending, so not worth gating behind a pending-count check.
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

            _frameSerial++;
            DrainDeferredSceneMeshDisposals();

            // Above the !_configured early-return so deferred work still progresses while minimized,
            // and above this frame's own submit/present so it never races it.
            DrainRenderThreadWork();

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
            // Explicit ternary, NOT `_hdrPostProcessor?.SceneTextureView`: `?.` yields a nullable result,
            // and a pointer type can't be made nullable (CS8978). Some Roslyn versions miss that here and
            // some don't, so the null-conditional form builds on one machine and fails on the next.
            var hdrSceneTextureView = _hdrPostProcessor != null ? _hdrPostProcessor.SceneTextureView : null;
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

            // See SuspendSceneWork's own doc comment. RefreshRenderScene is where a fast-running background
            // load turns into a synchronous stall on the render thread: it walks whatever the scene has
            // grown to since last frame and uploads all of it right here, and on a load that outpaces the
            // frame loop that can mean minutes of normal per-frame uploads compressed into whichever single
            // frame happens to run next. The scene pass still clears below either way, so skipping this
            // costs nothing but an empty frame -- exactly what a loading screen is drawn over.
            if (!SuspendSceneWork)
            {
                if (!_loggedFirstSceneWork)
                {
                    _loggedFirstSceneWork = true;

                    // Closes the loop with the rest of the startup timeline: whatever gap remains between
                    // "first frame presented" and this is the loading screen actually having its moment on
                    // screen before the scene catches up behind it.
                    ManaConsole.WriteDiagnosticElapsed("First unsuspended RefreshRenderScene (scene upload resumes).");
                }

                RefreshRenderScene();
            }

            UpdateShadowCascades();
            UpdateFrameUniform();
            LastCollectMs = collectStopwatch.Elapsed.TotalMilliseconds;

            // Not gated on SuspendSceneWork like the block above: the loading screen's own completion
            // waits on IsEnvironmentInitializationPending (see VoxelEditorGameMode.CanCompleteLoading),
            // so suspending this too meant the environment map could never start loading while the
            // screen that's waiting on it was up -- loading waiting on loading, forever. Kicking off the
            // Task.Run load here only needs the active render scene, not the mesh upload/reconcile work
            // RefreshRenderScene does, so it's safe to run every frame regardless of suspension.
            EnsureEnvironmentInitializationStarted();

            if (!SuspendSceneWork)
            {
                using (ManaFrameProfiler.Measure("GPU: Environment Capture"))
                {
                    DrawSkyIntoEnvironmentIfNeeded(commandEncoder);
                    CaptureSceneEnvironmentIfNeeded(commandEncoder);
                }

                using (ManaFrameProfiler.Measure("GPU: Shadow Cascades"))
                {
                    RenderShadowCascades(commandEncoder);
                }
            }

            var sceneRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &sceneRenderPassDescriptor);
            var drawStopwatch = Stopwatch.StartNew();

            if (!SuspendSceneWork)
            {
                // First, so ordinary depth-tested geometry draws over it: the sky is a background, not
                // something meshes need to sort against.
                DrawSkybox(sceneRenderPass);
                DrawDemoMesh(sceneRenderPass);
                DrawOverlays(sceneRenderPass);
            }

            _api.RenderPassEncoderEnd(sceneRenderPass);
            if (!SuspendSceneWork && _sceneMeshes.Any(mesh => mesh.Instance.AlwaysOnTop))
            {
                // Keep the scene color, but depth-test handles against one another, not the scene.
                sceneColorAttachment.LoadOp = LoadOp.Load;
                depthStencilAttachment.View = _handleDepthTextureView;
                var handlePass = _api.CommandEncoderBeginRenderPass(commandEncoder, &sceneRenderPassDescriptor);
                foreach (var mesh in _sceneMeshes.Where(mesh => mesh.Instance.AlwaysOnTop))
                {
                    DrawSceneMesh(handlePass, mesh);
                }
                _api.RenderPassEncoderEnd(handlePass);
                _api.RenderPassEncoderRelease(handlePass);
            }
            LastDrawMs = drawStopwatch.Elapsed.TotalMilliseconds;

            if (useHdrPost)
            {
                using (ManaFrameProfiler.Measure("GPU: Outline + HDR Post"))
                {
                    DrawOutlineMask(commandEncoder);
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

            var toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
            using (ManaFrameProfiler.Measure("GPU: Tone Map"))
            {
                _hdrPostProcessor?.Draw(toneMapRenderPass);
            }

            // A blurred backdrop needs the frame as it stands, and a fragment cannot read the target it is
            // writing. So the pass ends here, the frame is copied out and reduced, and the UI draws in a
            // second pass that loads what the first one left. Skipped entirely unless something asked for
            // blur, which keeps the ordinary frame exactly one pass as before.
            if (WantsBackdrop())
            {
                _api.RenderPassEncoderEnd(toneMapRenderPass);
                CaptureUiBackdrop(commandEncoder, surfaceTexture.Texture);

                surfaceColorAttachment.LoadOp = LoadOp.Load;
                toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
            }

            // The UI draws last, over the tone-mapped scene.
            using (ManaFrameProfiler.Measure("GPU: Draw ManaUI"))
            {
                DrawManaUi(toneMapRenderPass);
            }

            _api.RenderPassEncoderEnd(toneMapRenderPass);

            var pendingScreenshotCopy = EncodePendingScreenshotIfRequested(commandEncoder, surfaceTexture.Texture, _window.FramebufferSize);
            var pendingEnvironmentDump = EncodeEnvironmentDump(commandEncoder);

            var commandBufferDescriptor = new CommandBufferDescriptor();
            var submitStopwatch = Stopwatch.StartNew();
            var commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
            _api.QueueSubmit(_queue, 1, &commandBuffer);
            _api.SurfacePresent(_surface);

            if (pendingScreenshotCopy is { } encodedScreenshotCopy)
            {
                FinishPendingScreenshotReadback(encodedScreenshotCopy);
            }

            if (pendingEnvironmentDump is { } environmentDump)
            {
                FinishEnvironmentDump(environmentDump);
            }

            if (!_loggedFirstPresent)
            {
                _loggedFirstPresent = true;

                // The end of the trail: the moment a frame was actually handed to the window, as opposed to
                // recorded, submitted or merely attempted. Everything before this in the log was a guess at
                // why the window looked blank; this is when it stopped being one.
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

            _api.CommandBufferRelease(commandBuffer);
            _api.CommandEncoderRelease(commandEncoder);
            _api.RenderPassEncoderRelease(sceneRenderPass);
            _api.RenderPassEncoderRelease(toneMapRenderPass);
            _api.TextureViewRelease(textureView);
            _api.TextureRelease(surfaceTexture.Texture);

            LastFrameTotalMs = frameStopwatch.Elapsed.TotalMilliseconds;

            // Handed to the profiler as well as the World Perf panel: the panel shows this frame, the
            // profiler keeps the peak, and a hitch is only ever visible in the peak.
            ManaFrameProfiler.Record("GPU: Scene Collect", LastCollectMs);
            ManaFrameProfiler.Record("GPU: Draw Meshes", LastDrawMs);
            ManaFrameProfiler.Record("GPU: Submit + Present", LastSubmitMs);
            ManaFrameProfiler.Record("GPU: ClearFrame Total", LastFrameTotalMs);
        }

        public void Dispose()
        {
            if (_disposed) return;
#if !MANA_BROWSER
            foreach (var surface in _uiSurfaces.ToArray()) surface.Dispose();
#endif
            // Must happen before _queue/_device are released below: once polling stops, no native
            // BufferMapAsync callback will ever fire again, so any still-pending readback awaiter would
            // otherwise hang forever instead of getting a clean cancellation.
            _disposed = true;
            // Before FaultAllPending: an item that slipped through can still register a readback.
            DrainRenderThreadWork();
            _readbackRegistry.FaultAllPending();

            DrainDeferredSceneMeshDisposals(force: true);

            UiRoot = null;
            _uiRenderer?.Dispose();
            _uiRenderer = null;

            ReleaseDemoResources();
            _skybox?.Dispose();
            _skybox = null;
            _environmentSkybox?.Dispose();
            _environmentSkybox = null;

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
