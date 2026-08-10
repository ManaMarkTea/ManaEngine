using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Sea-level constants and bathymetry sampling shared by the two ocean meshes -- the whole-globe
    /// PlanetOceanMesh shell and the camera-following PlanetNearOceanMesh. Both have to agree on
    /// exactly what counts as ocean and how deep it reads, or the near patch's coastline and color
    /// would not line up with the shell it fades into.
    /// </summary>
    public static class OceanSurface
    {
        // Sea level is 0m by definition (matches PlanetColors' own `h < 0f` ocean check) -- not a
        // tunable, since "ocean" and "sea level" are the same thing here.
        public const float SeaLevelMeters = 0f;

        // Assumed depth for open-ocean points with no height-data coverage at all (distinct from a
        // point whose tile says it's below sea level) -- roughly Earth's actual mean ocean depth, so
        // uncovered deep ocean reads as deep rather than defaulting to a bright shoreline color.
        public const float DefaultOceanDepthMeters = 4000f;

        // How quickly the depth factor (fed to the shaders as the deep/shallow color mix amount)
        // approaches 1.0 as depth increases -- an exponential falloff rather than a hard linear clamp
        // so the shoreline-to-open-ocean transition looks like a gradient, not a band. At this scale,
        // ~200m (shelf depth) is already visibly past the shallow end and ~4-5x this value is
        // essentially fully "deep".
        private const float DepthFalloffMeters = 1200f;

        /// <summary>
        /// Null means "no data at all" (as opposed to real data that happens to be at/above sea
        /// level), so callers can tell the two cases apart -- e.g. DefaultOceanDepthMeters only
        /// applies to the former.
        /// </summary>
        public static float? SampleHeightMeters(GeoTiffLibrary? heightLibrary, Vector3 pointOnUnitSphere)
        {
            if (heightLibrary == null)
            {
                return null;
            }

            var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
            var tile = heightLibrary.FindCurrentTile(latLong);
            return tile?.GetHeightAtLatLong(latLong);
        }

        /// <summary>No height data at all is treated as ocean, the same fallback PlanetColors uses.</summary>
        public static bool IsOcean(float? rawHeightMeters)
        {
            return !rawHeightMeters.HasValue || rawHeightMeters.Value < SeaLevelMeters;
        }

        /// <summary>0 = shoreline (or no data-backed depth), 1 = deep open ocean.</summary>
        public static float ComputeDepthFactor(float? rawHeightMeters)
        {
            var depthMeters = rawHeightMeters.HasValue
                ? MathF.Max(0f, -rawHeightMeters.Value)
                : DefaultOceanDepthMeters;
            return 1f - MathF.Exp(-depthMeters / DepthFalloffMeters);
        }
    }
}
