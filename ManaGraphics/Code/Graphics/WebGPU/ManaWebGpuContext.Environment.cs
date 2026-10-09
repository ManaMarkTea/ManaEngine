using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Loading;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void CaptureSceneEnvironmentIfNeeded(CommandEncoder* commandEncoder)
        {
            if (!UseSceneCapture)
            {
                return;
            }

            if (_activeRenderScene == null ||
                _sceneMeshes.Count == 0)
            {
                return;
            }

            var source = GetEffectiveEnvironmentReflectionSource(_activeRenderScene.EnvironmentReflectionSource);
            if (source.SourceKind != ManaEnvironmentSourceKind.SceneCapture)
            {
                return;
            }

            var refreshInterval = Math.Max(1, source.SceneCaptureRefreshIntervalFrames);
            if (_framesSinceEnvironmentCapture < refreshInterval)
            {
                _framesSinceEnvironmentCapture++;
                return;
            }

            var environmentReflectionMap = GetOrCreateEnvironmentReflectionMap();
            var environmentMap = environmentReflectionMap.EnvironmentMapResource;
            if (environmentMap.RenderTargetViews.Count < 6)
            {
                return;
            }

            var pipeline = GetOrCreateSceneCapturePipeline();
            ManaConsole.WriteDiagnostic($"WebGPU scene environment capture: position={source.SceneCapturePosition}, meshes={_sceneMeshes.Count(mesh => mesh.Instance.IncludeInEnvironmentCapture)}.");

            for (var face = 0; face < 6; face++)
            {
                var captureCamera = CreateSceneCaptureCamera(source, face);
                EnsureSceneCaptureDepthResources(environmentMap.Width);
                RenderSceneCaptureFace(commandEncoder, (TextureView*)environmentMap.RenderTargetViews[face], pipeline, captureCamera, face);

            }

            DispatchEnvironmentPrefilter(commandEncoder, environmentReflectionMap);
            _framesSinceEnvironmentCapture = 0;
        }

        /// <summary>
        /// Draws the computed sky (Day, Day and Night, Space) into the reflection and irradiance cubes and
        /// prefilters it, so reflections show the sky on screen rather than the photo. Redrawn when the sky has
        /// moved, at most every <see cref="SkyRedrawFrames"/> frames.
        /// </summary>
        private void DrawSkyIntoEnvironmentIfNeeded(CommandEncoder* commandEncoder)
        {
            var scene = _activeRenderScene;
            var map = _environmentReflectionMap;
            if (scene == null || map == null || _clampSampler == null || scene.Sky.Mode == ManaSkyMode.Photo)
            {
                return;
            }

            var sky = scene.Sky with { TowardSun = SkyTowardSun(scene) };
            map.FramesSinceSkyDrawn++;
            if (map.DrawnSky is { } drawn && (!SkyMoved(drawn, sky) || map.FramesSinceSkyDrawn < SkyRedrawFrames))
            {
                return;
            }

            var environmentMap = map.EnvironmentMapResource;
            var prefilteredMap = map.PrefilteredReflectionMapResource;
            if (environmentMap.RenderTargetViews.Count < 6 || prefilteredMap.TextureView == null)
            {
                return;
            }

            _environmentSkybox ??= new ManaWebGpuSkybox(
                _api, _device, _queue, null, SkyShaderPath, 6, MoonTexturePath);

            for (var face = 0; face < 6; face++)
            {
                var camera = CreateCubeFaceCamera(face);
                // Bound only to fill the layout -- a computed sky never samples it -- and not a cube drawn into here.
                var unusedView = (TextureView*)prefilteredMap.TextureView;
                DrawSkyFace(commandEncoder, (TextureView*)environmentMap.RenderTargetViews[face], camera, sky, unusedView, face);
            }

            DispatchEnvironmentPrefilter(commandEncoder, map);
            map.DrawnSky = sky;
            map.HasPhoto = false;
            map.FramesSinceSkyDrawn = 0;
        }

        private const int SkyRedrawFrames = 20;

        private static bool SkyMoved(ManaSky.State drawn, ManaSky.State sky)
        {
            var moonPhaseDelta = MathF.Abs(drawn.MoonPhase - sky.MoonPhase);
            moonPhaseDelta = MathF.Min(moonPhaseDelta, 1.0f - moonPhaseDelta);
            return drawn.Mode != sky.Mode ||
                Vector3.Dot(drawn.TowardSun, sky.TowardSun) < 0.99985f ||
                (sky.Mode == ManaSkyMode.DayNightCycle && Vector3.Dot(drawn.TowardMoon, sky.TowardMoon) < 0.99985f) ||
                MathF.Abs(drawn.Daylight - sky.Daylight) > 0.01f ||
                drawn.ConstellationLines != sky.ConstellationLines ||
                (sky.Mode == ManaSkyMode.DayNightCycle && Vector3.Dot(drawn.CelestialEast, sky.CelestialEast) < 0.99985f) ||
                moonPhaseDelta > 0.001f;
        }

        private void DrawSkyFace(CommandEncoder* commandEncoder, TextureView* faceView, ManaCameraData camera, ManaSky.State sky, TextureView* unusedView, int face)
        {
            var colorAttachment = new RenderPassColorAttachment
            {
                View = faceView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0.0, 0.0, 0.0, 1.0)
            };

            var renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment
            };

            var renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &renderPassDescriptor);
            _environmentSkybox!.Draw(
                renderPass, TextureFormat.Rgba8UnormSrgb, _clampSampler, unusedView, camera, camera.Projection,
                sky.TowardSun, sky, ManaSky.Seconds, face);
            _api.RenderPassEncoderEnd(renderPass);
            _api.RenderPassEncoderRelease(renderPass);
        }

        /// <summary>
        /// A camera looking out of one cube face, oriented as WebGPU samples a cube: face order +X -X +Y -Y +Z -Z,
        /// with each face's screen right and up taken from the cube sampling rules (as GetCubeDirection writes them).
        /// </summary>
        private static ManaCameraData CreateCubeFaceCamera(int face)
        {
            var (forward, right, up) = face switch
            {
                0 => (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
                1 => (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                2 => (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
                3 => (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
                4 => (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
                _ => (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY)
            };

            // View space looks down -Z: rows of the basis, written as System.Numerics' row-vector matrix.
            var back = -forward;
            var view = new Matrix4x4(
                right.X, up.X, back.X, 0.0f,
                right.Y, up.Y, back.Y, 0.0f,
                right.Z, up.Z, back.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * 0.5f, 1.0f, 0.1f, 100.0f);
            return new ManaCameraData(view, projection, Vector3.Zero, 0.1f, 100.0f);
        }

        private void RenderSceneCaptureFace(
            CommandEncoder* commandEncoder,
            TextureView* faceView,
            RenderPipeline* pipeline,
            ManaCameraData captureCamera,
            int face)
        {
            if (faceView == null)
            {
                return;
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = faceView,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0.0, 0.0, 0.0, 1.0)
            };

            var depthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = _sceneCaptureDepthTextureView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };

            var renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            var renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &renderPassDescriptor);

            _api.RenderPassEncoderSetPipeline(renderPass, pipeline);
            foreach (var sceneMesh in _sceneMeshes)
            {
                if (!sceneMesh.Instance.IncludeInEnvironmentCapture)
                {
                    continue;
                }

                var captureUniformBuffer = sceneMesh.GetSceneCaptureUniformBuffer(face);
                var captureBindGroup = sceneMesh.GetSceneCaptureBindGroup(face);
                UpdateSceneCaptureUniform(sceneMesh.Instance, captureUniformBuffer, captureCamera);
                _api.RenderPassEncoderSetBindGroup(renderPass, 0, captureBindGroup, 0, null);
                sceneMesh.Mesh.Draw(renderPass);
            }

            _api.RenderPassEncoderEnd(renderPass);
            _api.RenderPassEncoderRelease(renderPass);
        }

        private void DispatchEnvironmentPrefilter(CommandEncoder* commandEncoder, ManaWebGpuEnvironmentReflectionMap map)
        {
            var compute = GetOrCreateEnvironmentPrefilterCompute();
            compute.Dispatch(commandEncoder, map.EnvironmentMapResource, map.PrefilteredReflectionMapResource);
            compute.DispatchIrradiance(commandEncoder, map.PrefilteredReflectionMapResource, map.IrradianceMapResource);
        }

        private void DispatchEnvironmentPrefilterNow(ManaWebGpuEnvironmentReflectionMap environmentReflectionMap)
        {
            GetOrCreateEnvironmentPrefilterCompute().DispatchNow(
                environmentReflectionMap.EnvironmentMapResource,
                environmentReflectionMap.PrefilteredReflectionMapResource,
                environmentReflectionMap.IrradianceMapResource);
        }

        private ManaWebGpuEnvironmentPrefilterCompute GetOrCreateEnvironmentPrefilterCompute()
        {
            if (_environmentPrefilterCompute != null)
            {
                return _environmentPrefilterCompute;
            }

            var shaderPath = ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "environment_prefilter_compute.wgsl"));
            _environmentPrefilterCompute = new ManaWebGpuEnvironmentPrefilterCompute(_api, _device, _queue, shaderPath);
            return _environmentPrefilterCompute;
        }

        private volatile ManaWebGpuEnvironmentReflectionMap? _pendingEnvironmentMap;
        private volatile bool _isEnvironmentLoading;
        // Set while a photo loads, and left set if it failed, so it is not retried every frame.
        private volatile bool _environmentPhotoStarted;

        public ManaLoadingViewModel EnvironmentLoading { get; } = new();

        public bool IsEnvironmentInitializationPending =>
            _environmentReflectionMap == null ||
            _isEnvironmentLoading ||
            _pendingEnvironmentMap != null;

        private void EnsureEnvironmentInitializationStarted()
        {
            // Normally set by RefreshRenderScene, but that's skipped while SuspendSceneWork is on (see
            // its call site in ClearFrame), which is exactly the window this method most needs to run in
            // -- the loading screen waits on the environment map, so it has to be able to start without
            // the mesh upload/reconcile work RefreshRenderScene also does.
            _activeRenderScene ??= _renderSceneProvider?.Invoke();
            if (_activeRenderScene != null)
            {
                _ = GetOrCreateEnvironmentReflectionMap();
            }
        }

        public void CheckPendingEnvironmentMap()
        {
            if (_pendingEnvironmentMap != null)
            {
                ReleaseEnvironmentReflectionMap();
                _environmentReflectionMap = _pendingEnvironmentMap;
                var environmentPath = TryResolveAssetPath(_environmentReflectionMap.Source.SourcePath) ?? _environmentReflectionMap.Source.SourcePath;
                _pendingEnvironmentMap = null;
                _isEnvironmentLoading = false;
                EnvironmentLoading.Complete("WebGPU environment maps ready");

                ReleaseSceneMeshes();

                // Releasing is only half the job: these meshes have to come BACK, rebuilt against the
                // new environment textures. Normally RefreshRenderScene() does that on the next frame
                // -- but it bails out immediately when there's no _renderSceneProvider, which is
                // exactly the case for a scene built from CreateDemoRenderScene() rather than supplied
                // by a game mode. Without this, the meshes vanish permanently the moment the
                // background environment-map load completes (~1 frame in), with no error anywhere:
                // no provider, no re-upload, no geometry, forever. Cost the browser port a long
                // debugging detour -- every draw-call-level diagnostic looked perfect because by the
                // time anything was screenshotted there was no draw call left to inspect.
                if (_renderSceneProvider == null && _activeRenderScene != null)
                {
                    UploadSceneMeshes(_activeRenderScene);
                }

                ManaConsole.WriteDiagnostic($"WebGPU environment reflection targets swapped: {environmentPath}");
            }
        }

        private ManaWebGpuEnvironmentReflectionMap GetOrCreateEnvironmentReflectionMap()
        {
            var source = GetEffectiveEnvironmentReflectionSource(_activeRenderScene?.EnvironmentReflectionSource);
            var environmentPath = TryResolveAssetPath(source.SourcePath) ?? source.SourcePath;

            // The pending-map swap + ReleaseSceneMeshes() used to be duplicated here as well as in
            // CheckPendingEnvironmentMap() (called once per frame, before RefreshRenderScene(), in
            // ClearFrame()). This method is called per-mesh from CreateSceneMesh() during
            // ReconcileSceneMeshes()'s reconciliation loop — if the background environment-map load
            // finished while a large batch of meshes was still being reconciled, this second copy
            // fired mid-loop and disposed every ManaWebGpuSceneMesh in _sceneMeshes (including ones
            // already re-added by reference into that same loop's nextSceneMeshes list via the reuse
            // path), which then got committed back into _sceneMeshes at the end of the loop as
            // disposed-but-still-resident meshes — drawn next frame with destroyed WebGPU buffer/bind
            // group handles, crashing the native wgpu backend with "Buffer ... does not exist"
            // ("wgpu_render_pass_execute_bundles" panic). Only CheckPendingEnvironmentMap() should
            // ever perform this swap now, so it can only happen once per frame, before reconciliation
            // starts, never reentrantly mid-loop.

            var wantsPhoto = _activeRenderScene?.Sky.Mode == ManaSkyMode.Photo;
            if (_environmentReflectionMap != null &&
                HasSameEnvironmentSource(_environmentReflectionMap.Source, source))
            {
                if (wantsPhoto && !_environmentReflectionMap.HasPhoto && !_isEnvironmentLoading)
                {
                    StartEnvironmentPhotoLoad(_environmentReflectionMap, source.SourcePath, environmentPath);
                }

                return _environmentReflectionMap;
            }

            if (!_isEnvironmentLoading)
            {
                _isEnvironmentLoading = true;
                EnvironmentLoading.Start("Preparing WebGPU environment maps", 0.0f);
                _environmentReflectionMap = ManaWebGpuEnvironmentReflectionMap.CreateDummy(_api, _device, _queue, source);

                // The photo first (downloaded, on the web), then the cubes; a continuation because this class is unsafe and cannot await.
                var photo = wantsPhoto
                    ? ManaEnvironmentPhoto.FindAsync(source.SourcePath, environmentPath)
                    : Task.FromResult<string?>(null);
                photo.ContinueWith(found =>
                {
                    if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
                    {
                        System.Threading.Thread.CurrentThread.Name = "WebGPU env map load";
                    }

                    try
                    {
                        var photoPath = found.Result;
                        var map = ManaWebGpuEnvironmentReflectionMap.Create(
                            _api, _device, _queue, source, photoPath, EnvironmentLoading);
                        EnvironmentLoading.Report(new ManaLoadingProgress("Dispatching environment prefilter compute shader", 0.97f));
                        DispatchEnvironmentPrefilterNow(map);
                        _pendingEnvironmentMap = map;
                        EnvironmentLoading.Report(new ManaLoadingProgress("Waiting to activate WebGPU environment maps", 0.99f));
                    }
                    catch (Exception ex)
                    {
                        ManaConsole.WriteDiagnostic($"Failed to load WebGPU environment map: {ex}");
                        _isEnvironmentLoading = false;
                        EnvironmentLoading.Fail($"WebGPU environment initialization failed: {ex.Message}");
                    }
                }, TaskScheduler.Default);
            }

            return _environmentReflectionMap!;
        }

        /// <summary>
        /// The Photo sky, chosen once the map exists: the photo (downloaded, on the web) is decoded into the map's
        /// cubes in the background. Tried once; a missing photo leaves the sky's own reflections up.
        /// </summary>
        private void StartEnvironmentPhotoLoad(ManaWebGpuEnvironmentReflectionMap map, string reference, string resolvedPath)
        {
            if (_environmentPhotoStarted)
            {
                return;
            }

            _environmentPhotoStarted = true;
            ManaEnvironmentPhoto.FindAsync(reference, resolvedPath).ContinueWith(found =>
            {
                try
                {
                    map.WritePhoto(_api, _queue, found.Result ?? resolvedPath);
                    DispatchEnvironmentPrefilterNow(map);
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic($"Environment photo unavailable: {ex.Message}");
                }
                finally
                {
                    _environmentPhotoStarted = !map.HasPhoto;
                }
            }, TaskScheduler.Default);
        }

        private static ManaEnvironmentReflectionSource GetEffectiveEnvironmentReflectionSource(ManaEnvironmentReflectionSource? source)
        {
            source ??= new ManaEnvironmentReflectionSource();

            return new ManaEnvironmentReflectionSource
            {
                SourceKind = ManaEnvironmentSourceKind.SceneCapture,
                SourcePath = source.SourcePath,
                Projection = source.Projection,
                BuildReflectionCubeMap = source.BuildReflectionCubeMap,
                EnvironmentCubeSize = UseMobileRendering ? Math.Min(source.EnvironmentCubeSize, 128) : source.EnvironmentCubeSize,
                IrradianceCubeSize = UseMobileRendering ? Math.Min(source.IrradianceCubeSize, 16) : source.IrradianceCubeSize,
                PrefilterCubeSize = UseMobileRendering ? Math.Min(source.PrefilterCubeSize, 128) : source.PrefilterCubeSize,
                BrdfLutSize = source.BrdfLutSize,
                SceneCapturePosition = source.SceneCapturePosition,
                SceneCaptureNearPlane = source.SceneCaptureNearPlane,
                SceneCaptureFarPlane = source.SceneCaptureFarPlane,
                SceneCaptureRefreshIntervalFrames = source.SceneCaptureRefreshIntervalFrames
            };
        }

        private static bool IsEnvironmentFlagEnabled(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameEnvironmentSource(ManaEnvironmentReflectionSource current, ManaEnvironmentReflectionSource incoming)
        {
            return current.SourceKind == incoming.SourceKind &&
                string.Equals(current.SourcePath, incoming.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                current.Projection == incoming.Projection &&
                current.BuildReflectionCubeMap == incoming.BuildReflectionCubeMap &&
                current.EnvironmentCubeSize == incoming.EnvironmentCubeSize &&
                current.IrradianceCubeSize == incoming.IrradianceCubeSize &&
                current.PrefilterCubeSize == incoming.PrefilterCubeSize &&
                current.BrdfLutSize == incoming.BrdfLutSize &&
                current.SceneCapturePosition == incoming.SceneCapturePosition &&
                Math.Abs(current.SceneCaptureNearPlane - incoming.SceneCaptureNearPlane) < 0.0001f &&
                Math.Abs(current.SceneCaptureFarPlane - incoming.SceneCaptureFarPlane) < 0.0001f &&
                current.SceneCaptureRefreshIntervalFrames == incoming.SceneCaptureRefreshIntervalFrames;
        }

        private void ReleaseEnvironmentReflectionMap()
        {
            if (_environmentReflectionMap == null)
            {
                return;
            }

            _environmentReflectionMap.Dispose();
            _environmentReflectionMap = null;
            _framesSinceEnvironmentCapture = int.MaxValue;
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * MathF.PI / 180.0f;
        }

        private static ManaCameraData CreateSceneCaptureCamera(ManaEnvironmentReflectionSource source, int face)
        {
            var position = source.SceneCapturePosition;
            var nearPlane = Math.Max(0.001f, source.SceneCaptureNearPlane);
            var farPlane = Math.Max(nearPlane + 1.0f, source.SceneCaptureFarPlane);
            var direction = face switch
            {
                0 => Vector3.UnitX,
                1 => -Vector3.UnitX,
                2 => Vector3.UnitY,
                3 => -Vector3.UnitY,
                4 => Vector3.UnitZ,
                _ => -Vector3.UnitZ
            };
            var up = face switch
            {
                2 => -Vector3.UnitZ,
                3 => Vector3.UnitZ,
                _ => Vector3.UnitY
            };

            var view = Matrix4x4.CreateLookAt(position, position + direction, up);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * 0.5f, 1.0f, nearPlane, farPlane);
            return new ManaCameraData(view, projection, position, nearPlane, farPlane);
        }

        private Matrix4x4 GetAspectCorrectedProjection(ManaCameraData camera)
        {
            if (_framebufferSize.Y <= 0)
            {
                return camera.Projection;
            }

            return camera.Projection;
        }

    }

    /// <summary>The environment photo on disk -- on the web, fetched from the asset library the first time it is asked for.</summary>
    internal static class ManaEnvironmentPhoto
    {
        public static async Task<string?> FindAsync(string reference, string resolvedPath)
        {
            if (File.Exists(resolvedPath) || ManaCore.Assets.ManaAssetCatalog.Current is not { } catalog ||
                catalog.LocalFileOf(reference) is not { } catalogPath)
            {
                return resolvedPath;
            }

            await catalog.EnsureAsync(reference);
            return catalogPath;
        }
    }
}
