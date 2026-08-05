using System.Globalization;
using Mana.Code;
using Mana.Code.Nodes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A planet built from a grid of PlanetPatchMesh tiles (the "loads in tiles" alternative to
    /// PlanetLoader's single whole-sphere mesh). Builds instantly as a smooth placeholder sphere (no
    /// GeoTIFF decode on construction); call LoadHeightDataAsync to kick off background decoding that
    /// progressively swaps each patch to real elevation/color as its tile finishes — the caller isn't
    /// blocked, and patches already loaded keep rendering while others are still pending. Also owns
    /// HeightExaggeration as a live, reboundable setting (e.g. for an Inspector slider): changing it
    /// rebuilds every patch's geometry immediately.
    /// </summary>
    public sealed class PlanetNode : ManaSceneNode
    {
        public float Radius { get; }
        public int Resolution { get; }
        public float PatchSizeDegrees { get; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        // Serializes patch rebuilds: a background tile finishing and a main-thread HeightExaggeration
        // edit could otherwise race on the same patch's DynamicGeometryContainer/CpuData.
        private readonly object _rebuildLock = new();
        private readonly Dictionary<(float Latitude, float Longitude), PlanetPatchMesh> _patches = new();

        private float _heightExaggeration;

        public float HeightExaggeration
        {
            get => _heightExaggeration;
            set
            {
                _heightExaggeration = value;
                RebuildAllPatches();
            }
        }

        public PlanetNode(
            ManaContext context,
            float minLatitudeDegrees,
            float maxLatitudeDegrees,
            float minLongitudeDegrees,
            float maxLongitudeDegrees,
            float patchSizeDegrees,
            float radius,
            int resolution,
            float heightExaggeration)
            : base(context, Matrix4x4.Identity)
        {
            Radius = radius;
            Resolution = resolution;
            PatchSizeDegrees = patchSizeDegrees;
            _heightExaggeration = heightExaggeration;
            Name = "Planet";

            var heightScale = ComputeHeightScale();
            for (var lat = minLatitudeDegrees; lat < maxLatitudeDegrees; lat += patchSizeDegrees)
            {
                for (var lon = minLongitudeDegrees; lon < maxLongitudeDegrees; lon += patchSizeDegrees)
                {
                    var mesh = new PlanetPatchMesh(
                        context, lat, lon, patchSizeDegrees, radius, resolution, heightScale, heightLibrary: null);
                    var node = new ManaMeshSceneNode(context, mesh) { Name = PatchName(lat, lon) };
                    AddChild(node);
                    _patches[(lat, lon)] = mesh;
                }
            }
        }

        /// <summary>
        /// Kicks off background decoding of every tile in heightLibrary; returns immediately without
        /// waiting on it. Each patch swaps from smooth placeholder to real elevation/color as soon as
        /// its own tile finishes, independently of the others.
        /// </summary>
        public void LoadHeightDataAsync(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            Task.Run(() => heightLibrary.LoadAllImageData(ApplyTileToOverlappingPatches));
        }

        private void ApplyTileToOverlappingPatches(ManaGeoTools.Code.GeoTiff.GeoTiff tile)
        {
            lock (_rebuildLock)
            {
                foreach (var (key, patch) in _patches)
                {
                    if (TileOverlapsPatch(tile, key.Latitude, key.Longitude))
                    {
                        patch.ApplyHeightData(HeightLibrary!);
                    }
                }
            }
        }

        // A bounding-box overlap test, not just "does the tile contain the patch's center" — a tile
        // can graze only a patch's edge (exactly what happens at two patches' shared boundary) without
        // containing its center. Missing that case left neighboring patches' edge columns never
        // refreshed, producing a visible seam between an updated patch and its untouched neighbor even
        // though PlanetPatchMesh's own per-vertex sampling would have matched them if both were rebuilt.
        private bool TileOverlapsPatch(ManaGeoTools.Code.GeoTiff.GeoTiff tile, float patchLatitudeDegrees, float patchLongitudeDegrees)
        {
            var tileMinLon = tile.Area.UpperLeft.Longitude;
            var tileMaxLon = tile.Area.LowerRight.Longitude;
            var tileMinLat = tile.Area.LowerLeft.Latitude;
            var tileMaxLat = tile.Area.UpperRight.Latitude;

            var patchMinLon = patchLongitudeDegrees;
            var patchMaxLon = patchLongitudeDegrees + PatchSizeDegrees;
            var patchMinLat = patchLatitudeDegrees;
            var patchMaxLat = patchLatitudeDegrees + PatchSizeDegrees;

            return tileMinLon <= patchMaxLon && tileMaxLon >= patchMinLon &&
                   tileMinLat <= patchMaxLat && tileMaxLat >= patchMinLat;
        }

        private void RebuildAllPatches()
        {
            lock (_rebuildLock)
            {
                var heightScale = ComputeHeightScale();
                foreach (var patch in _patches.Values)
                {
                    patch.Rebuild(heightScale);
                }
            }
        }

        private float ComputeHeightScale()
        {
            return (Radius / PlanetLoader.EarthRadiusMeters) * _heightExaggeration;
        }

        private static string PatchName(float latitudeDegrees, float longitudeDegrees)
        {
            return string.Format(CultureInfo.InvariantCulture, "Patch_{0}_{1}", latitudeDegrees, longitudeDegrees);
        }
    }
}
