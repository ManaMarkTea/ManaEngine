using static ManaGeoTools.Code.Planet.PlanetPatchPlanner;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    // Geometry and source measurements only; callers own synchronization and scene integration.
    internal static class PlanetPatchGeometry
    {
        public readonly record struct PatchBoundsSphere(Vector3 LocalCenter, float LocalRadius);

        public static void ApplyHeightData(
            PlanetPatchMesh patch, GeoTiffLibrary library, int resolution, float[,]? precomputedHeights = null)
        {
            if (precomputedHeights != null)
            {
                patch.ApplyHeightDataWithResolution(library, resolution, precomputedHeights);
            }
            else
            {
                patch.ApplyHeightDataWithResolution(library, resolution);
            }
        }

        public static int EstimateGeoTiffSourceResolutionForPatch(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var pixelSize = tile.Area.PixelSize;
            if (pixelSize.X <= 0f || pixelSize.Y <= 0f)
            {
                return FallbackPatchResolution;
            }

            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(patch);
            var lonSpanPixels = Math.Abs(lonMax - lonMin) / pixelSize.X;
            var latSpanPixels = Math.Abs(latMax - latMin) / pixelSize.Y;
            var sourceSamplesAcrossPatch = Math.Max(lonSpanPixels, latSpanPixels);
            if (float.IsNaN(sourceSamplesAcrossPatch) || float.IsInfinity(sourceSamplesAcrossPatch))
            {
                return FallbackPatchResolution;
            }

            return Math.Max(2, (int)MathF.Ceiling(sourceSamplesAcrossPatch) + 1);
        }

        public static double EstimateGeoTiffMetersPerPixel(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var pixelSize = tile.Area.PixelSize;
            if (pixelSize.X <= 0f || pixelSize.Y <= 0f)
            {
                return 0.0;
            }

            var (_, _, latMin, latMax) = ComputePatchLonLatBounds(patch);
            var centerLatitude = Math.Clamp((latMin + latMax) * 0.5, -90.0f, 90.0f);
            var metersPerDegree = 2.0 * Math.PI * PlanetLoader.EarthRadiusMeters / 360.0;
            var latMetersPerPixel = metersPerDegree * pixelSize.Y;
            var lonMetersPerPixel = metersPerDegree * pixelSize.X * Math.Cos(centerLatitude * Math.PI / 180.0);
            return Math.Max(0.0, Math.Max(latMetersPerPixel, lonMetersPerPixel));
        }

        // Corner/center probes avoid a misleading longitude box across the antimeridian or poles.
        public static bool TileOverlapsPatch(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var axisA = new Vector3(patch.FaceNormal.Y, patch.FaceNormal.Z, patch.FaceNormal.X);
            var axisB = Vector3.Cross(patch.FaceNormal, axisA);
            var midU = (patch.MinU + patch.MaxU) * 0.5f;
            var midV = (patch.MinV + patch.MaxV) * 0.5f;

            var sampleUVs = new[]
            {
                (patch.MinU, patch.MinV), (patch.MaxU, patch.MinV),
                (patch.MinU, patch.MaxV), (patch.MaxU, patch.MaxV),
                (midU, midV),
            };

            foreach (var (u, v) in sampleUVs)
            {
                var pointOnUnitCube = patch.FaceNormal + (u * axisA) + (v * axisB);
                var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                if (tile.Area.Contains(latLong))
                {
                    return true;
                }
            }

            return false;
        }

        // Cached per leaf: UV coverage stays fixed through an in-place height update.
        public static PatchBoundsSphere ComputePatchLocalBoundsSphere(
            Vector3 faceNormal, float minU, float maxU, float minV, float maxV, float radius)
        {
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            var axisB = Vector3.Cross(faceNormal, axisA);
            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;

            var centerOnUnitCube = faceNormal + (midU * axisA) + (midV * axisB);
            var center = CubeSphere.WarpCubeToSphere(centerOnUnitCube) * radius;

            var cornerUVs = new[] { (minU, minV), (maxU, minV), (minU, maxV), (maxU, maxV) };
            var boundsRadius = 0f;
            foreach (var (u, v) in cornerUVs)
            {
                var cornerOnUnitCube = faceNormal + (u * axisA) + (v * axisB);
                var corner = CubeSphere.WarpCubeToSphere(cornerOnUnitCube) * radius;
                boundsRadius = MathF.Max(boundsRadius, Vector3.Distance(center, corner));
            }

            return new PatchBoundsSphere(center, boundsRadius);
        }
    }
}
