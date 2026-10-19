using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : ManaGraphicsDiagnosticsProvider, IDisposable
    {
        public bool UseSceneCapture = false;

        // Never disposed: that would free wgpu_native, which the static log hooks, other contexts and the
        // iOS framework still use. Only the GPU objects made through it are this context's to release.
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
        // Resolved once, right after _device/_queue exist -- wgpu-native's own extension for pumping
        // async callbacks (BufferMapAsync, QueueOnSubmittedWorkDone) to actually fire. Falls back to the
        // portable InstanceProcessEvents if this build's WebGPU implementation doesn't expose it.
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
        // Work handed in from background threads that must run on the render thread instead -- see
        // EnqueueRenderThreadWork for why submitting from anywhere else deadlocks wgpu-native.
        private readonly ConcurrentQueue<Action> _renderThreadWork = new();
        // Not readonly: set from FinishInitialization, which for MANA_BROWSER runs after the
        // constructor returns (see ManaWebGpuContext.BrowserAsync.cs), not inside it.
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
        private ManaWebGpuHeightSampleCompute? _heightSampleCompute;
        // Made with the device; outlives pipeline failures, since loading waits on it either way.
        private ManaWebGpuEnvironment? _environment;
        public ManaCore.Loading.ManaLoadingViewModel EnvironmentLoading { get; } = new();
        public bool IsEnvironmentInitializationPending => _environment?.IsInitializationPending ?? true;
#if !MANA_BROWSER
#endif
        private ManaWebGpuUiCompositor? _ui;

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
                // No wgpu-native-style backend selection in-browser -- the browser's own WebGPU
                // implementation picks its backend internally. Deliberately a real null pointer, NOT
                // `&(new InstanceDescriptor())`: a pointer to a zeroed-but-real struct silently hung
                // forever inside Emscripten's legacy wgpuCreateInstance (confirmed by isolating the
                // exact call via a raw function pointer probe -- null succeeded instantly, a pointer to
                // an identical zeroed struct never returned). Matches the M0 spike's own
                // wgpuCreateInstance(NULL) call, which is the only shape ever actually proven to work.
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
                // MANA_BROWSER: adapter/device request is genuinely asynchronous in a real browser
                // (navigator.gpu.requestAdapter() resolves on a JS microtask, not synchronously within
                // this call the way wgpu-native's desktop implementation does) -- a constructor can't
                // await that, and this whole class is declared `unsafe`, which C# doesn't allow an
                // `await` to appear within at all (regardless of whether the specific method touches a
                // pointer). See ManaWebGpuContext.BrowserAsync.cs's CompleteBrowserInitializationAsync,
                // a separate non-unsafe partial declaration of this same class, which callers must
                // await before this context is usable.
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
            _meshTextures = new ManaWebGpuMeshTextureCache(_api, _device, _queue);
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
            _gpu = new ManaWebGpuDevice(_api, _device, _queue, _surfaceFormat, _depthFormat);
            _environment = new ManaWebGpuEnvironment(_gpu, EnvironmentLoading);
            _meshes = new ManaWebGpuSceneMeshes(_gpu, _meshTextures, _environment);
            _frameCapture = new ManaWebGpuFrameCapture(_gpu, _readbackRegistry);
            _ui = new ManaWebGpuUiCompositor(_gpu);
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
            _hdrPostProcessor = new ManaWebGpuHdrPostProcessor(_api, _device, _surfaceFormat, shaderPath);
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
                // CopySrc as well, so the composited frame can be snapshotted for the UI's blurred backdrop.
                // Nothing copies it unless a panel asks for blur; the usage flag alone costs nothing.
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
            _depth = new ManaWebGpuDepthTarget(_api, _device, _depthFormat, width, height, "depth");
            _handleDepth = new ManaWebGpuDepthTarget(_api, _device, _depthFormat, width, height, "handle depth");
        }

        /// <summary>
        /// Activates a loaded environment map at the frame boundary. Meshes bound to the old one are released and
        /// come back against the new textures: the provider's scene on its next refresh, or a provider-less demo
        /// scene uploaded again here -- without that, its meshes vanished the moment the first map landed.
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

        // Also while scene work is suspended: the loading screen waits on the environment map, so it must start
        // without the mesh upload RefreshRenderScene would otherwise do first.
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
                        ManaWebGpuDevice.ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "height_sample_compute.wgsl")));

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

        /// <summary>
        /// Renders <paramref name="instances"/> (already loaded, world transforms composed) to a square
        /// straight-alpha RGBA buffer, or completes with null when there was nothing to draw. Callable from any
        /// thread; the GPU work runs on the render thread (see <see cref="EnqueueRenderThreadWork"/>).
        /// </summary>
        public Task<byte[]?> RenderThumbnailAsync(IReadOnlyList<ManaRenderMeshInstance> instances, int thumbnailSize)
        {
            if (_disposed || thumbnailSize <= 0)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueRenderThreadWork(() =>
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

        /// <summary>
        /// Saves the next frame this context renders to <paramref name="path"/> as a PNG. Render-thread only, one
        /// capture in flight at a time; <paramref name="withoutUi"/> leaves ManaUI out of that frame.
        /// </summary>
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
        public ManaWebGpuPanelSurface CreateUiSurface(IWindow window)
        {
            var surface = new ManaWebGpuPanelSurface(_api, _instance, _adapter, _device, _queue, window, closed => _panelSurfaces.Remove(closed));
            _panelSurfaces.Add(surface);
            return surface;
        }
#endif

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

            _meshes?.BeginFrame();

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

                _shadowMap?.UpdateCascades(_activeRenderScene);
                // The bindings are made with the shadow map, so it exists whenever they do.
                if (_activeRenderScene != null && _bindings != null)
                {
                    _bindings.WriteFrame(_queue, ManaWebGpuFrameUniform.For(
                        _activeRenderScene, _shadowMap!, (float)(DateTime.UtcNow - _startTime).TotalSeconds));
                }
                LastCollectMs = collectStopwatch.Elapsed.TotalMilliseconds;

                // Not gated on SuspendSceneWork like the block above: the loading screen's own completion
                // waits on IsEnvironmentInitializationPending (see VoxelEditorGameMode.CanCompleteLoading),
                // so suspending this too meant the environment map could never start loading while the
                // screen that's waiting on it was up -- loading waiting on loading, forever. Kicking off the
                // Task.Run load here only needs the active render scene, not the mesh upload/reconcile work
                // RefreshRenderScene does, so it's safe to run every frame regardless of suspension.
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

                // A blurred backdrop needs the frame as it stands, and a fragment cannot read the target it is
                // writing. So the pass ends here, the frame is copied out and reduced, and the UI draws in a
                // second pass that loads what the first one left. Skipped entirely unless something asked for
                // blur, which keeps the ordinary frame exactly one pass as before.
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
            DrainRenderThreadWork();
            _readbackRegistry.FaultAllPending();
            _heightSampleCompute?.Dispose();
            _heightSampleCompute = null;

            _meshes?.DrainDeferred(force: true);

            UiRoot = null;
            _ui?.Dispose();

            ReleaseScenePipelines();

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

            // _api and _wgpuNativeExtension are deliberately not disposed: the native library outlives the context
            // (see Plans/RendererResourceOwnership.md, binding-wrapper contract).
        }
    }
}
