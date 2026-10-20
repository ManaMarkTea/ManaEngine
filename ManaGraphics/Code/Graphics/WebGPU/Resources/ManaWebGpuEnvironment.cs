using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Renderers;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The reflection and irradiance cubes: loaded in the background, activated at the frame boundary, prefiltered after
    /// each redraw. Exists even without scene pipelines, since loading waits on it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuEnvironment : ManaWebGpuDeviceObject
    {
        private readonly ManaWebGpuEnvironmentLoad<ManaPreparedEnvironment> _loads = new();
        private ManaWebGpuEnvironmentReflectionMap? _pending;
        private ManaEnvironmentReflectionSource? _requestedSource;
        private bool _isLoading;
        // Set while a photo loads, and left set if it failed, so it is not retried every frame.
        private bool _photoStarted;
        private ManaWebGpuEnvironmentPrefilterCompute? _prefilter;

        public ManaWebGpuEnvironment(ManaWebGpuDevice gpu, ManaLoadingViewModel loading)
            : base(gpu)
        {
            Loading = loading;
        }

        public ManaLoadingViewModel Loading { get; }

        /// <summary>The map meshes bind and the sky samples; null until the first is made.</summary>
        public ManaWebGpuEnvironmentReflectionMap? Map { get; private set; }

        public bool IsInitializationPending => Map == null || _isLoading || _pending != null;

        /// <summary>The active map (a placeholder until its load lands); starts a load when the scene's source changes.</summary>
        public ManaWebGpuEnvironmentReflectionMap EnsureMap(ManaRenderScene? scene)
        {
            var source = EffectiveSource(scene?.EnvironmentReflectionSource);
            var environmentPath = ManaWebGpuDevice.TryResolveAssetPath(source.SourcePath) ?? source.SourcePath;
            var wantsPhoto = scene?.Sky.Mode == ManaSkyMode.Photo;

            if (_requestedSource == null || !HasSameSource(_requestedSource, source))
            {
                _requestedSource = source.Clone();
                _photoStarted = false;
                _isLoading = true;
                Loading.Start("Preparing WebGPU environment maps", 0.0f);
                _loads.Start((progress, token) =>
                    ManaPreparedEnvironment.PrepareAsync(source, environmentPath, wantsPhoto, false, progress, token));
            }
            Map ??= ManaWebGpuEnvironmentReflectionMap.CreateDummy(Gpu, source);

            // Only ActivatePending swaps maps; reconciliation may already hold live mesh references.
            if (wantsPhoto && HasSameSource(Map.Source, source) && !Map.HasPhoto && !_isLoading)
            {
                StartPhotoLoad(Map, environmentPath);
            }

            return Map;
        }

        /// <summary>Frame boundary only: activates a loaded map. True when it replaced one, so bound meshes must rebuild.</summary>
        public bool ActivatePending(ManaRenderScene? scene)
        {
            DrainLoad(scene);
            if (_pending == null)
            {
                return false;
            }

            Map?.Dispose();
            Map = _pending;
            _pending = null;
            _isLoading = false;
            Loading.Complete("WebGPU environment maps ready");
            var environmentPath = ManaWebGpuDevice.TryResolveAssetPath(Map.Source.SourcePath) ?? Map.Source.SourcePath;
            ManaConsole.WriteDiagnostic($"WebGPU environment reflection targets swapped: {environmentPath}");
            return true;
        }

        /// <summary>Convolves what was just drawn into the map's environment cube, on the frame's encoder.</summary>
        public void Prefilter(CommandEncoder* commandEncoder, ManaWebGpuEnvironmentReflectionMap map)
        {
            var compute = GetOrCreatePrefilter();
            compute.Dispatch(commandEncoder, map.EnvironmentMapResource, map.PrefilteredReflectionMapResource);
            compute.DispatchIrradiance(commandEncoder, map.PrefilteredReflectionMapResource, map.IrradianceMapResource);
        }

        private void PrefilterNow(ManaWebGpuEnvironmentReflectionMap map)
        {
            GetOrCreatePrefilter().DispatchNow(map.EnvironmentMapResource, map.PrefilteredReflectionMapResource, map.IrradianceMapResource);
        }

        private ManaWebGpuEnvironmentPrefilterCompute GetOrCreatePrefilter()
        {
            return _prefilter ??= new ManaWebGpuEnvironmentPrefilterCompute(Gpu,
                ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("environment_prefilter_compute.wgsl")));
        }

        private void StartPhotoLoad(ManaWebGpuEnvironmentReflectionMap map, string resolvedPath)
        {
            if (_photoStarted)
            {
                return;
            }
            _photoStarted = true;
            var source = map.Source.Clone();
            var size = (int)map.EnvironmentMapResource.Width;
            _loads.Start((progress, token) =>
                ManaPreparedEnvironment.PrepareAsync(source, resolvedPath, true, true, progress, token, size));
        }

        // Frame boundary only: no native work or view-model mutation happens in background continuations.
        private void DrainLoad(ManaRenderScene? scene)
        {
            if (_loads.TakeProgress() is { } progress)
            {
                Loading.Report(progress);
            }
            if (_loads.TakeResult() is not { } result)
            {
                return;
            }
            if (result.Error != null)
            {
                FailLoad(result.Error);
                return;
            }
            if (result.Value is not { } prepared)
            {
                return;
            }

            ManaWebGpuEnvironmentReflectionMap? candidate = null;
            try
            {
                if (prepared.PhotoOnly)
                {
                    if (scene?.Sky.Mode == ManaSkyMode.Photo &&
                        Map is { } active &&
                        HasSameSource(active.Source, prepared.Source) && prepared.Photo != null)
                    {
                        active.WritePhoto(prepared.Photo);
                        PrefilterNow(active);
                        _photoStarted = !active.HasPhoto;
                    }
                    else
                    {
                        _photoStarted = false;
                    }
                    return;
                }

                if (prepared.Data == null)
                {
                    return;
                }
                Loading.Report(new ManaLoadingProgress("Uploading WebGPU environment textures", 0.96f));
                candidate = ManaWebGpuEnvironmentReflectionMap.Create(Gpu, prepared.Source, prepared.Data);
                PrefilterNow(candidate);
                _pending?.Dispose();
                _pending = candidate;
                candidate = null;
            }
            catch (Exception ex)
            {
                FailLoad(ex);
            }
            finally
            {
                candidate?.Dispose();
            }
        }

        private void FailLoad(Exception error)
        {
            ManaConsole.WriteDiagnostic($"Failed to load WebGPU environment map: {error}");
            if (_isLoading)
            {
                _isLoading = false;
                Loading.Fail($"WebGPU environment initialization failed: {error.Message}");
            }
        }

        /// <summary>Shutdown: no background load may publish again. Its CPU work is dropped, not joined.</summary>
        public void Retire()
        {
            _loads.Retire();
        }

        /// <summary>Releases every GPU map and pending load; the next <see cref="EnsureMap"/> starts again.</summary>
        public void Release()
        {
            _loads.Invalidate();
            _pending?.Dispose();
            _pending = null;
            _requestedSource = null;
            _isLoading = false;
            _photoStarted = false;
            Map?.Dispose();
            Map = null;
            _prefilter?.Dispose();
            _prefilter = null;
        }

        public override void Dispose()
        {
            Release();
        }

        public static ManaEnvironmentReflectionSource EffectiveSource(ManaEnvironmentReflectionSource? source)
        {
            source ??= new ManaEnvironmentReflectionSource();
            var mobile = ManaWebGpuDevice.UseMobileRendering;

            return new ManaEnvironmentReflectionSource
            {
                SourceKind = ManaEnvironmentSourceKind.SceneCapture,
                SourcePath = source.SourcePath,
                Projection = source.Projection,
                BuildReflectionCubeMap = source.BuildReflectionCubeMap,
                EnvironmentCubeSize = mobile ? Math.Min(source.EnvironmentCubeSize, 128) : source.EnvironmentCubeSize,
                IrradianceCubeSize = mobile ? Math.Min(source.IrradianceCubeSize, 16) : source.IrradianceCubeSize,
                PrefilterCubeSize = mobile ? Math.Min(source.PrefilterCubeSize, 128) : source.PrefilterCubeSize,
                BrdfLutSize = source.BrdfLutSize,
                SceneCapturePosition = source.SceneCapturePosition,
                SceneCaptureNearPlane = source.SceneCaptureNearPlane,
                SceneCaptureFarPlane = source.SceneCaptureFarPlane,
                SceneCaptureRefreshIntervalFrames = source.SceneCaptureRefreshIntervalFrames
            };
        }

        private static bool HasSameSource(ManaEnvironmentReflectionSource current, ManaEnvironmentReflectionSource incoming)
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

    internal sealed record ManaPreparedEnvironment(ManaEnvironmentReflectionSource Source, bool PhotoOnly,
        ManaWebGpuEnvironmentReflectionMap.PreparedData? Data, ManaWebGpuEnvironmentReflectionMap.PreparedTexture? Photo)
    {
        public static async Task<ManaPreparedEnvironment> PrepareAsync(ManaEnvironmentReflectionSource source,
            string resolvedPath, bool wantsPhoto, bool photoOnly, IProgress<ManaLoadingProgress> progress,
            CancellationToken cancellationToken, int? photoSize = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = wantsPhoto
                ? await ManaEnvironmentPhoto.FindAsync(source.SourcePath, resolvedPath).WaitAsync(cancellationToken).ConfigureAwait(false)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            return photoOnly
                ? new ManaPreparedEnvironment(source, true, null,
                    ManaWebGpuEnvironmentReflectionMap.PreparePhoto(path ?? resolvedPath,
                        photoSize ?? Math.Clamp(source.EnvironmentCubeSize, 16, 2048), progress, cancellationToken))
                : new ManaPreparedEnvironment(source, false,
                    ManaWebGpuEnvironmentReflectionMap.PrepareData(source, path, progress, cancellationToken), null);
        }
    }
}
