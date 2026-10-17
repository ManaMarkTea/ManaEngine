using ManaGeoTools.Code.GeoTiff;
using ManaGeoTools.Code.GeoTiff.Terrarium;

namespace ManaGeoTools.Code.Planet
{
    // Pure coverage and source-density decisions, independent of scene mutation and tile I/O.
    internal static class PlanetPatchPlanner
    {
        // Keep requested source quality: subdivide instead of silently selecting a coarser zoom.
        public const int UpresMaxTiles = 1024;
        public const int FallbackPatchResolution = 160;
        // This limits each leaf; subdivision preserves the requested source sample density.
        public const int MaxPatchMeshResolution = 1025;
        private const int MaxDirectSourceMatchDepth = 3;

        public sealed class PatchUpresPlan
        {
            public required int Zoom { get; init; }
            public required int MeshResolution { get; init; }
            public required int SourceResolution { get; init; }
            public required double SourceMetersPerPixel { get; init; }
            public required IReadOnlyList<(int X, int Y)> TileIndices { get; init; }
        }

        public readonly record struct PlannedPatch(
            PatchKey Key,
            float MinU,
            float MaxU,
            float MinV,
            float MaxV,
            PatchUpresPlan Plan);

        public static IReadOnlyList<PlannedPatch>? CreateSourceMatchedPatchPlan(PatchKey key, PlanetPatchMesh patch, int targetZoom, bool includeTileIndices)
        {
            var plannedPatches = new List<PlannedPatch>();
            return TryAddSourceMatchedPatchPlans(
                key,
                patch.FaceNormal,
                patch.MinU,
                patch.MaxU,
                patch.MinV,
                patch.MaxV,
                targetZoom,
                includeTileIndices,
                plannedPatches)
                    ? plannedPatches
                    : null;
        }

        public static bool TryAddSourceMatchedPatchPlans(
            PatchKey key,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int maxZoom,
            bool includeTileIndices,
            List<PlannedPatch> plannedPatches,
            int depth = 0)
        {
            var plan = CreateUpresPlan(faceNormal, minU, maxU, minV, maxV, maxZoom, includeTileIndices);
            if (plan != null && plan.SourceResolution <= MaxPatchMeshResolution)
            {
                plannedPatches.Add(new PlannedPatch(key, minU, maxU, minV, maxV, plan));
                return true;
            }

            if (depth >= MaxDirectSourceMatchDepth)
            {
                return false;
            }

            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var quadMinU = (quadrant & 1) == 0 ? minU : midU;
                var quadMaxU = (quadrant & 1) == 0 ? midU : maxU;
                var quadMinV = (quadrant & 2) == 0 ? minV : midV;
                var quadMaxV = (quadrant & 2) == 0 ? midV : maxV;

                if (!TryAddSourceMatchedPatchPlans(
                    key.Child(quadrant),
                    faceNormal,
                    quadMinU,
                    quadMaxU,
                    quadMinV,
                    quadMaxV,
                    maxZoom,
                    includeTileIndices,
                    plannedPatches,
                    depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        public static PatchUpresPlan? CreateUpresPlan(
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int maxZoom,
            bool includeTileIndices)
        {
            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(faceNormal, minU, maxU, minV, maxV);
            var zoom = SlippyMapMath.PickZoom(lonMin, lonMax, latMin, latMax, UpresMaxTiles, maxZoom);
            if (!zoom.HasValue)
            {
                return null;
            }
            if (zoom.Value != maxZoom)
            {
                return null;
            }

            IReadOnlyList<(int X, int Y)> tileIndices = includeTileIndices
                ? SlippyMapMath.EnumerateTiles(lonMin, lonMax, latMin, latMax, zoom.Value).ToList()
                : Array.Empty<(int X, int Y)>();
            var sourceResolution = EstimateSourceResolutionForZoom(lonMin, lonMax, latMin, latMax, zoom.Value);
            return new PatchUpresPlan
            {
                Zoom = zoom.Value,
                MeshResolution = Math.Min(sourceResolution, MaxPatchMeshResolution),
                SourceResolution = sourceResolution,
                SourceMetersPerPixel = EstimateSourceMetersPerPixel(latMin, latMax, zoom.Value),
                TileIndices = tileIndices
            };
        }

        // Converts the streamed tile zoom into the approximate number of source samples available
        // across this patch. Terrarium tiles are 256x256 samples in Web Mercator tile space.
        public static int EstimateSourceResolutionForZoom(float lonMin, float lonMax, float latMin, float latMax, int zoom)
        {
            var (xMin, yMin) = SlippyMapMath.LatLongToTileCoords(lonMin, latMax, zoom);
            var (xMax, yMax) = SlippyMapMath.LatLongToTileCoords(lonMax, latMin, zoom);
            var pixelSpanX = Math.Abs(xMax - xMin) * TerrariumHeightTile.TileSize;
            var pixelSpanY = Math.Abs(yMax - yMin) * TerrariumHeightTile.TileSize;
            var sourceSamplesAcrossPatch = Math.Max(pixelSpanX, pixelSpanY);
            if (double.IsNaN(sourceSamplesAcrossPatch) || double.IsInfinity(sourceSamplesAcrossPatch))
            {
                return FallbackPatchResolution;
            }

            return Math.Max(2, (int)Math.Ceiling(sourceSamplesAcrossPatch) + 1);
        }

        public static double EstimateSourceMetersPerPixel(float latMin, float latMax, int zoom)
        {
            var centerLatitude = Math.Clamp((latMin + latMax) * 0.5, -SlippyMapMath.MaxLatitudeDegrees, SlippyMapMath.MaxLatitudeDegrees);
            var metersPerPixelAtEquator = 2.0 * Math.PI * PlanetLoader.EarthRadiusMeters
                / (TerrariumHeightTile.TileSize * Math.Pow(2.0, zoom));
            return Math.Max(0.0, metersPerPixelAtEquator * Math.Cos(centerLatitude * Math.PI / 180.0));
        }

        // Edge midpoints give tile coverage extra margin around curved patch boundaries.
        public static (float LonMin, float LonMax, float LatMin, float LatMax) ComputePatchLonLatBounds(PlanetPatchMesh patch)
        {
            return ComputePatchLonLatBounds(patch.FaceNormal, patch.MinU, patch.MaxU, patch.MinV, patch.MaxV);
        }

        public static (float LonMin, float LonMax, float LatMin, float LatMax) ComputePatchLonLatBounds(
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV)
        {
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            var axisB = Vector3.Cross(faceNormal, axisA);
            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;

            var sampleUVs = new[]
            {
                (minU, minV), (maxU, minV),
                (minU, maxV), (maxU, maxV),
                (midU, minV), (midU, maxV),
                (minU, midV), (maxU, midV),
                (midU, midV),
            };

            var lonMin = float.MaxValue;
            var lonMax = float.MinValue;
            var latMin = float.MaxValue;
            var latMax = float.MinValue;

            foreach (var (u, v) in sampleUVs)
            {
                var pointOnUnitCube = faceNormal + (u * axisA) + (v * axisB);
                var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);

                lonMin = Math.Min(lonMin, latLong.Longitude);
                lonMax = Math.Max(lonMax, latLong.Longitude);
                latMin = Math.Min(latMin, latLong.Latitude);
                latMax = Math.Max(latMax, latLong.Latitude);
            }

            return (lonMin, lonMax, latMin, latMax);
        }
    }
}
