using System.Diagnostics;
using Mana.Code;
using Mana.Code.Nodes;

namespace ManaGeoTools.Code.Region
{
    public enum RegionLoadState
    {
        Loading,
        Ready,
        Failed
    }

    /// <summary>
    /// A single real-world place loaded into the scene in plain cartesian space at TRUE scale -- a
    /// (2 * HalfExtentMeters) square of terrain around a lat/long, 1 scene unit == 1 meter, sitting
    /// wherever the node is placed rather than on the globe.
    ///
    /// The counterpart to PlanetNode, and deliberately much smaller: the planet is a streaming
    /// quadtree that splits, merges and re-sources patches as the camera moves, because it has to cover
    /// the whole Earth at every scale. A region covers one fixed box at one fixed detail level chosen up
    /// front by RegionExtents, so it has no LOD, no splitting and no per-frame work at all beyond
    /// ProcessPendingLoad -- it builds once and is then static. That's the whole point of it: somewhere
    /// to stand at human scale, where a meter in the scene is a meter on the ground.
    ///
    /// Construction returns immediately with an empty node; the tile fetch and mesh build run on a
    /// background thread and get adopted into the scene graph by ProcessPendingLoad on the main thread,
    /// the same split PlanetNode uses for its own background work.
    /// </summary>
    public sealed class RegionNode : ManaSceneNode
    {
        // Resolution is normally derived from the source data rather than fixed: RegionExtents ties zoom
        // to extent so the sample count across the box is roughly constant, and matching it means a mesh
        // that neither throws away detail that was fetched nor interpolates a lot of vertices that carry
        // no new information. The bounds are backstops for the extremes (very high latitudes, where the
        // Mercator sample spacing tightens up, and rounding at the ladder's edges).
        public const int MinResolution = 65;
        public const int MaxResolution = 385;

        public string PlaceName { get; }
        public LatLong Center { get; }
        public float HalfExtentMeters { get; }
        public int Zoom { get; }

        /// <summary>Vertex grid size per side. Only final once State is Ready -- it's derived from the fetched source data unless the constructor was given an explicit override.</summary>
        public int Resolution { get; private set; }

        public RegionLoadState State { get; private set; } = RegionLoadState.Loading;
        public string? ErrorMessage { get; private set; }

        /// <summary>Real elevation in meters at the exact center point -- the datum the mesh's own Y values are relative to. Only meaningful once State is Ready.</summary>
        public float GroundElevationMeters { get; private set; }
        public double MetersPerSample { get; private set; }
        public double LoadSeconds { get; private set; }

        private Task<RegionTerrainMesh?>? _buildTask;
        private readonly int? _resolutionOverride;
        private readonly Stopwatch _loadStopwatch = Stopwatch.StartNew();

        public RegionNode(
            ManaContext context,
            string placeName,
            LatLong center,
            float halfExtentMeters,
            int? resolution = null)
            : base(context, Matrix4x4.Identity)
        {
            PlaceName = placeName;
            Center = center;
            HalfExtentMeters = Math.Clamp(halfExtentMeters, RegionExtents.MinHalfExtentMeters, RegionExtents.MaxHalfExtentMeters);
            Zoom = RegionExtents.ZoomForHalfExtent(HalfExtentMeters);
            _resolutionOverride = resolution;

            // A provisional value so the property is never nonsense while the build is in flight; the
            // real one is derived from the fetched data in BuildAsync.
            Resolution = resolution ?? MinResolution;
            Name = $"Region: {placeName}";

            _buildTask = Task.Run(BuildAsync);
        }

        private async Task<RegionTerrainMesh?> BuildAsync()
        {
            if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
            {
                System.Threading.Thread.CurrentThread.Name = "Region terrain build";
            }

            var (lonMin, lonMax, latMin, latMax) = RegionExtents.ComputeBounds(Center, HalfExtentMeters);
            var heightField = await RegionHeightField.FetchAsync(lonMin, lonMax, latMin, latMax, Center.Latitude, Zoom);
            if (heightField == null)
            {
                return null;
            }

            MetersPerSample = heightField.MetersPerSample;

            // One mesh vertex per source sample: below that the fetched detail is thrown away, above it
            // the extra vertices only interpolate data that isn't there. Clamped, and overridable.
            var samplesAcross = (2.0 * HalfExtentMeters) / Math.Max(heightField.MetersPerSample, 0.001);
            Resolution = _resolutionOverride
                ?? Math.Clamp((int)Math.Round(samplesAcross) + 1, MinResolution, MaxResolution);

            // Sampled from the same stitched field the mesh uses, so the center vertex lands at exactly
            // y == 0 rather than at some near-zero value from a separately-rounded lookup.
            var groundElevation = heightField.SampleMeters(Center.Longitude, Center.Latitude);
            GroundElevationMeters = groundElevation;

            // Mesh construction off the main thread is fine -- ManaMesh.SetCpuData is documented
            // thread-safe, and this touches nothing else (see PlanetPatchMesh.ApplyHeightData). Only the
            // AddChild in ProcessPendingLoad has to wait for the main thread.
            return new RegionTerrainMesh(Context, Center, HalfExtentMeters, Resolution, heightField, groundElevation);
        }

        /// <summary>
        /// Main thread only. Adopts the finished mesh into the scene graph. Cheap no-op once the region
        /// is Ready or Failed, so it's safe to call unconditionally every frame.
        /// </summary>
        public void ProcessPendingLoad()
        {
            if (_buildTask == null || !_buildTask.IsCompleted)
            {
                return;
            }

            var task = _buildTask;
            _buildTask = null;
            _loadStopwatch.Stop();
            LoadSeconds = _loadStopwatch.Elapsed.TotalSeconds;

            if (task.IsFaulted)
            {
                State = RegionLoadState.Failed;
                ErrorMessage = task.Exception?.GetBaseException().Message ?? "Region build failed.";
                Console.WriteLine($"[Region] Build for '{PlaceName}' failed: {task.Exception}");
                return;
            }

            var mesh = task.Result;
            if (mesh == null)
            {
                State = RegionLoadState.Failed;
                ErrorMessage = $"No elevation tiles available at zoom {Zoom} for this location.";
                return;
            }

            var meshNode = new ManaMeshSceneNode(Context, mesh) { Name = "Region Terrain" };
            meshNode.MeshRenderer.Name = $"Region Terrain z{Zoom}";
            AddChild(meshNode);
            meshNode.UpdateTransform();

            AddRegionSun();
            State = RegionLoadState.Ready;
        }

        // The scene's default lights sit on a 256-unit grid with power sized for that distance, so at a
        // region's scale and offset they contribute essentially nothing (1/d^2) and the terrain ends up
        // lit only by ambient IBL -- which reads as uniformly glossy. This gives the region its own sun,
        // with intensity undoing the fixed 256*256 power scale ManaWorld applies to collected lights.
        private void AddRegionSun()
        {
            var offset = new Vector3(HalfExtentMeters * 0.6f, HalfExtentMeters * 3.0f, -HalfExtentMeters * 0.6f);
            var sun = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(offset)) { Name = "Region Sun" };
            sun.Components.Add(new ManaLightSourceComponent(sun)
            {
                Intensity = offset.LengthSquared() / (256.0f * 256.0f),
                Color = Colors.White
            });

            AddChild(sun);
            sun.UpdateTransform();
        }

        /// <summary>A one-line summary for a status/HUD readout.</summary>
        public string DescribeStatus()
        {
            return State switch
            {
                RegionLoadState.Loading => $"Loading {DescribeSize()} at z{Zoom}...",
                RegionLoadState.Ready =>
                    $"{DescribeSize()} at z{Zoom} ({MetersPerSample:0.#} m/sample, {Resolution}x{Resolution} verts), ground {GroundElevationMeters:0} m, built in {LoadSeconds:0.0}s",
                _ => ErrorMessage ?? "Region failed to load."
            };
        }

        private string DescribeSize()
        {
            var sideMeters = 2f * HalfExtentMeters;
            return sideMeters >= 1000f ? $"{sideMeters / 1000f:0.##} km box" : $"{sideMeters:0} m box";
        }
    }
}
