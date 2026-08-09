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
        public const int DefaultResolution = 193;

        public string PlaceName { get; }
        public LatLong Center { get; }
        public float HalfExtentMeters { get; }
        public int Zoom { get; }
        public int Resolution { get; }

        public RegionLoadState State { get; private set; } = RegionLoadState.Loading;
        public string? ErrorMessage { get; private set; }

        /// <summary>Real elevation in meters at the exact center point -- the datum the mesh's own Y values are relative to. Only meaningful once State is Ready.</summary>
        public float GroundElevationMeters { get; private set; }
        public double MetersPerSample { get; private set; }
        public double LoadSeconds { get; private set; }

        private Task<RegionTerrainMesh?>? _buildTask;
        private readonly Stopwatch _loadStopwatch = Stopwatch.StartNew();

        public RegionNode(
            ManaContext context,
            string placeName,
            LatLong center,
            float halfExtentMeters,
            int resolution = DefaultResolution)
            : base(context, Matrix4x4.Identity)
        {
            PlaceName = placeName;
            Center = center;
            HalfExtentMeters = halfExtentMeters;
            Zoom = RegionExtents.ZoomForHalfExtent(halfExtentMeters);
            Resolution = Math.Max(2, resolution);
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

            State = RegionLoadState.Ready;
        }

        /// <summary>A one-line summary for a status/HUD readout.</summary>
        public string DescribeStatus()
        {
            return State switch
            {
                RegionLoadState.Loading => $"Loading {DescribeSize()} at z{Zoom}...",
                RegionLoadState.Ready =>
                    $"{DescribeSize()} at z{Zoom} ({MetersPerSample:0.#} m/sample), ground {GroundElevationMeters:0} m, built in {LoadSeconds:0.0}s",
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
