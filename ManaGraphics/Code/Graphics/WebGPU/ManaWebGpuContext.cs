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
    public unsafe sealed class ManaWebGpuContext : ManaGraphicsDiagnosticsProvider, IDisposable
    {
        public bool UseSceneCapture = false;

        // Never disposed: that would free wgpu_native, which other contexts and the iOS framework still use.
        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly IInputContext? _input;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly string? _requestedBackend;
        // Made first and released last; null only if its own construction failed.
        private ManaWebGpuNativeDevice? _native;
        private ManaWebGpuDiagnostics? _diagnostics;
#if !MANA_BROWSER
        private ManaWebGpuResourceReport? _resourceReport;
#endif
        private readonly ManaWebGpuReadbackRegistry _readbackRegistry = new();
        private volatile bool _disposed;
        private TextureFormat _surfaceFormat;
        private readonly TextureFormat _depthFormat = TextureFormat.Depth24Plus;
        private readonly DateTime _startTime = DateTime.UtcNow;
        // Made with the device; meshes are only uploaded once the scene bindings exist.
        private ManaWebGpuSceneMeshes? _meshes;
        private readonly ManaWebGpuRenderThreadQueue _renderThread = new();
        // Set in FinishInitialization, which in the browser runs after the constructor.
        private ManaWebGpuMeshTextureCache _meshTextures = null!;
        private ManaRenderScene? _activeRenderScene;
        // Set in FinishInitialization; borrowed by every pass.
        private ManaWebGpuDevice _gpu = null!;
        // Set in FinishInitialization: the window's swap chain and depth targets.
        private ManaWebGpuWindowSurface? _windowSurface;
        // The scene pipelines and passes; null while the renderer is clear-only.
        private ManaWebGpuSceneRenderer? _scene;
        private ManaWebGpuThumbnailRenderer? _thumbnails;
        private ManaWebGpuFrameCapture? _frameCapture;
#if !MANA_BROWSER
        private readonly List<ManaWebGpuPanelSurface> _panelSurfaces = new();
#endif
        public long LastGpuParticleCount => _scene?.ParticlesDrawn ?? 0;
        public long RequestedGpuParticleCount => _scene?.ParticlesRequested ?? 0;
        // Set in FinishInitialization; ManaWebGpuGraphicsDevice forwards planet height requests to it.
        internal ManaWebGpuHeightSampleCompute? HeightSampler { get; private set; }
        // Made with the device; outlives pipeline failures, since loading waits on it either way.
        private ManaWebGpuEnvironment? _environment;
        public ManaCore.Loading.ManaLoadingViewModel EnvironmentLoading { get; } = new();
        public bool IsEnvironmentInitializationPending => _environment?.IsInitializationPending ?? true;
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
                _native = new ManaWebGpuNativeDevice(_api, window, requestedBackend);
#if !MANA_BROWSER
                FinishInitialization(requestedBackend, window);
#endif
                // MANA_BROWSER: the adapter and device arrive asynchronously; callers await
                // CompleteBrowserInitializationAsync before using the context.
            }
            catch
            {
                Dispose();
                throw;
            }
        }

#if MANA_BROWSER
        /// <summary>Awaited once after constructing a browser context, before anything else uses it.</summary>
        public Task CompleteBrowserInitializationAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed || _native == null, this);
            return _native.CompleteAsync(() => FinishInitialization(_requestedBackend, _window), Dispose);
        }
#endif

        private void FinishInitialization(string? requestedBackend, IWindow window)
        {
            ManaConsole.WriteDiagnostic("Configuring WebGPU queue and surface.");
            var native = _native!;
#if !MANA_BROWSER
            if (native.NativeExtension != null)
            {
                _resourceReport = new ManaWebGpuResourceReport(native);
            }
#endif
            _diagnostics = new ManaWebGpuDiagnostics(_api, native);
            _surfaceFormat = native.PreferredSurfaceFormat();
            _gpu = new ManaWebGpuDevice(_api, native.Device, native.Queue, _surfaceFormat, _depthFormat);
            _meshTextures = new ManaWebGpuMeshTextureCache(_gpu);
            _environment = new ManaWebGpuEnvironment(_gpu, EnvironmentLoading);
            _meshes = new ManaWebGpuSceneMeshes(_gpu, _meshTextures, _environment);
            _frameCapture = new ManaWebGpuFrameCapture(_gpu, _readbackRegistry);
            _ui = new ManaWebGpuUiCompositor(_gpu);
            HeightSampler = new ManaWebGpuHeightSampleCompute(_gpu, _readbackRegistry, _renderThread);
            _windowSurface = new ManaWebGpuWindowSurface(_gpu, native.Surface);
            _windowSurface.Configure(window.FramebufferSize);
            if (_renderSceneProvider != null || ManaWebGpuSceneRenderer.DemoRequested)
            {
                if (ManaWebGpuSceneRenderer.SupportsBackend(requestedBackend))
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
            _diagnostics.LogStartup(DiagnosticsState());
            CaptureInitializationSnapshot();
#endif
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            if (_windowSurface?.Configure(framebufferSize) == true)
            {
                _scene?.Resize(framebufferSize);
            }
        }

        private void CreateScenePipelines()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU mesh pipeline.");
            var renderScene = _renderSceneProvider?.Invoke() ?? ManaWebGpuDemoScene.Create();
            _activeRenderScene = renderScene;
            _scene = new ManaWebGpuSceneRenderer(_gpu, _environment!, _meshes!, _windowSurface!.Size, () => UseSceneCapture);
            ManaConsole.WriteDiagnostic("WebGPU mesh pipeline ready.");
            if (renderScene.Meshes.Count > 0)
            {
                _meshes!.Upload(renderScene, _scene.Bindings);
            }
        }

        // Shutdown and failed pipeline initialization both come through here; anything may still be null.
        private void ReleaseScenePipelines()
        {
            _thumbnails?.Dispose();
            _thumbnails = null;
            // Meshes first: they bind the environment and shadow views the owners below release.
            _meshes?.ReleaseAll(immediate: true);
            _scene?.Dispose();
            _scene = null;
            _environment?.Release();
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
            if (_renderSceneProvider == null && _activeRenderScene != null && _scene != null)
            {
                _meshes!.Upload(_activeRenderScene, _scene.Bindings);
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
            if (renderScene == null || _scene == null)
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
                _meshes!.Reconcile(renderableMeshes, renderScene, _scene.Bindings);
            }
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

                if (_scene == null)
                {
                    completion.TrySetException(new InvalidOperationException("WebGPU scene pipelines are unavailable for thumbnails."));
                    return;
                }

                _thumbnails ??= new ManaWebGpuThumbnailRenderer(_gpu, _readbackRegistry, _meshTextures);
                _thumbnails.EncodeAndSubmit(instances, (uint)thumbnailSize, _scene.Bindings, completion);
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
            var surface = new ManaWebGpuPanelSurface(_gpu, _native!.Instance, _native.Adapter, window, closed => _panelSurfaces.Remove(closed));
            _panelSurfaces.Add(surface);
            return surface;
        }
#endif

        private bool _loggedFirstPresent;
        private bool _loggedFirstSceneWork;

        protected override ManaGraphicsDiagnosticsSnapshot CaptureSnapshotCore()
        {
            return _diagnostics?.Capture(DiagnosticsState()) ?? ManaGraphicsDiagnosticsSnapshot.Empty;
        }

        private ManaWebGpuDiagnosticsState DiagnosticsState()
        {
            return new ManaWebGpuDiagnosticsState(
                _requestedBackend,
                _window.Size,
                _window.FramesPerSecond,
                _window.UpdatesPerSecond,
                _input,
                _windowSurface?.Size ?? default,
                _surfaceFormat,
                _depthFormat,
                _scene?.SceneColorFormat ?? _surfaceFormat,
                _scene != null,
                _renderSceneProvider != null,
                _windowSurface?.IsConfigured == true,
                UseSceneCapture,
                _activeRenderScene?.EnvironmentReflectionSource,
                _environment?.Map != null,
                RequestedGpuParticleCount,
                LastGpuParticleCount,
                _meshes?.Meshes ?? [],
                _meshTextures?.Count ?? 0,
                (_thumbnails?.ShaderModuleCount ?? 0) + (_scene?.ShaderModuleCount ?? 0),
                (_thumbnails?.PipelineCount ?? 0) + (_scene?.PipelineCount ?? 0));
        }

        public void ClearFrame(Color clearColor)
        {
            // Before the early returns, so a minimized window never stalls a readback.
            _native!.PumpCallbacks();
            _meshes?.BeginFrame();

            // Before the early return, so deferred work progresses while minimized; before this frame's submit, so it never races it.
            _renderThread.Drain();

            if (_windowSurface is not { IsConfigured: true } surface)
            {
                return;
            }

            CheckPendingEnvironmentMap();

            if (!surface.TryAcquire(out var surfaceTexture, out var surfaceView))
            {
                Resize(_window.FramebufferSize);
                return;
            }

            var size = surface.Size;
            CommandEncoder* encoder = null;
            RenderPassEncoder* toneMapPass = null;
            CommandBuffer* commandBuffer = null;
            ManaWebGpuFrameCapture.EncodedCopies? copies = null;
            try
            {
                var frameStopwatch = Stopwatch.StartNew();
                var encoderDescriptor = new CommandEncoderDescriptor();
                encoder = _api.DeviceCreateCommandEncoder(_native!.Device, &encoderDescriptor);

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

                var seconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
                if (_scene != null && _activeRenderScene != null)
                {
                    _scene.UpdateFrame(_activeRenderScene, seconds);
                }
                LastCollectMs = collectStopwatch.Elapsed.TotalMilliseconds;

                // Not gated on SuspendSceneWork: loading waits on the environment map, so it must start meanwhile.
                EnsureEnvironmentInitializationStarted();

                // Without a scene the tone-map pass clears the surface itself.
                var drewToSurface = _scene != null && _activeRenderScene != null &&
                    _scene.Encode(encoder, _activeRenderScene, surface, surfaceView, clearColor, SuspendSceneWork, seconds);
                var drewScene = _scene != null && _activeRenderScene != null;
                LastDrawMs = _scene?.LastDrawMs ?? 0;

                // The Render Targets viewer: only on frames whose UI draws from its picture.
                if (drewScene && !SuspendSceneWork && UiRoot is { } previewRoot
                    && previewRoot.DrawList.DrawsTexture(ManaRenderTargetPreview.TextureId))
                {
                    var preview = _scene!.EncodePreview(encoder, _activeRenderScene!, surface);
                    if (preview != null)
                    {
                        _ui!.SetTexture(ManaRenderTargetPreview.TextureId, preview);
                    }
                }

                var surfaceAttachment = new RenderPassColorAttachment
                {
                    View = surfaceView,
                    LoadOp = drewToSurface ? LoadOp.Load : LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = clearColor
                };
                var toneMapDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = &surfaceAttachment
                };

                toneMapPass = _api.CommandEncoderBeginRenderPass(encoder, &toneMapDescriptor);
                if (drewScene && !drewToSurface)
                {
                    using (ManaFrameProfiler.Measure("GPU: Tone Map"))
                    {
                        _scene!.DrawToneMap(toneMapPass);
                    }
                }

                // Blur needs the finished frame, which the pass is writing: end it, copy and reduce the frame, then draw
                // the UI in a second pass. Only when something asked for blur.
                if (ManaWebGpuUiCompositor.WantsBackdrop(UiRoot))
                {
                    _api.RenderPassEncoderEnd(toneMapPass);
                    _api.RenderPassEncoderRelease(toneMapPass);
                    toneMapPass = null;
                    _ui!.CaptureBackdrop(encoder, surfaceTexture, size);

                    surfaceAttachment.LoadOp = LoadOp.Load;
                    toneMapPass = _api.CommandEncoderBeginRenderPass(encoder, &toneMapDescriptor);
                }

                using (ManaFrameProfiler.Measure("GPU: Draw ManaUI"))
                {
                    // Left out of a clean screenshot: the scene across the whole window.
                    if (UiRoot is { } root && _frameCapture?.WithoutUi != true && !_ui!.Draw(toneMapPass, root, size))
                    {
                        UiRoot = null;
                    }
                }

                _api.RenderPassEncoderEnd(toneMapPass);
                copies = _frameCapture?.Encode(encoder, surfaceTexture, size, _environment?.Map);

                var submitStopwatch = Stopwatch.StartNew();
                var commandBufferDescriptor = new CommandBufferDescriptor();
                commandBuffer = _api.CommandEncoderFinish(encoder, &commandBufferDescriptor);
                _api.QueueSubmit(_native.Queue, 1, &commandBuffer);
                surface.Present();

                // Submitted: each staging buffer now belongs to its readback.
                if (copies is { } submitted)
                {
                    copies = null;
                    _frameCapture!.Finish(submitted);
                }

                if (!_loggedFirstPresent)
                {
                    _loggedFirstPresent = true;
                    ManaConsole.WriteDiagnosticElapsed("First frame presented to the window.");
                }

                _native!.PumpCallbacks();
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
                if (toneMapPass != null)
                {
                    _api.RenderPassEncoderRelease(toneMapPass);
                }
                if (encoder != null)
                {
                    _api.CommandEncoderRelease(encoder);
                }
                _api.TextureViewRelease(surfaceView);
                _api.TextureRelease(surfaceTexture);
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
            _resourceReport?.Dispose();
            _resourceReport = null;
#endif
            _frameCapture?.Close();
#if MANA_BROWSER
            _native?.CancelInitialization();
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

            _windowSurface?.Dispose();

            _native?.Dispose();
            _native = null;

            // _api stays: the native library outlives the context.
        }
    }
}
