namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Per-vertex "biome" color ramp for PlanetPatchMesh. There's no global equirectangular color texture
    /// wired up (no Image/texture-loading pipeline in ManaGeoTools — see the GeoTIFF port notes), so
    /// this fakes a planet-like look with vertex color instead: real elevation (where a GeoTiffLibrary
    /// tile covers a vertex) gets a classic ocean/beach/grass/rock/snow hypsometric tint; everywhere
    /// else (most of the sphere, since the demo heightmap only covers a small region) falls back to a
    /// latitude-based gradient (equatorial blue-green, polar ice) so the whole planet reads as a
    /// planet, not just the small textured patch.
    /// </summary>
    public static class PlanetColors
    {
        private static readonly Vector4 DeepOcean = new(0.05f, 0.12f, 0.35f, 1f);
        private static readonly Vector4 ShallowOcean = new(0.10f, 0.30f, 0.55f, 1f);
        private static readonly Vector4 Beach = new(0.76f, 0.70f, 0.50f, 1f);
        private static readonly Vector4 Grassland = new(0.25f, 0.45f, 0.18f, 1f);
        private static readonly Vector4 Forest = new(0.14f, 0.32f, 0.14f, 1f);
        private static readonly Vector4 Rock = new(0.45f, 0.40f, 0.36f, 1f);
        private static readonly Vector4 Snow = new(0.95f, 0.96f, 0.98f, 1f);

        private static readonly Vector4 EquatorOcean = new(0.06f, 0.28f, 0.42f, 1f);
        private static readonly Vector4 MidLatitudeOcean = new(0.08f, 0.20f, 0.40f, 1f);
        private static readonly Vector4 PolarIce = new(0.88f, 0.93f, 0.97f, 1f);

        /// <summary>
        /// heightMeters: real elevation in meters if a heightmap tile covers this vertex, null
        /// otherwise. latitudeDegrees: -90..90.
        /// </summary>
        public static Vector4 GetBiomeColor(float? heightMeters, float latitudeDegrees)
        {
            var absLatitude01 = Math.Clamp(MathF.Abs(latitudeDegrees) / 90f, 0f, 1f);

            if (heightMeters.HasValue)
            {
                var h = heightMeters.Value;
                Vector4 color;
                if (h < -50f)
                {
                    color = DeepOcean;
                }
                else if (h < 0f)
                {
                    color = Vector4.Lerp(ShallowOcean, DeepOcean, MathF.Min(-h / 50f, 1f));
                }
                else if (h < 20f)
                {
                    color = Vector4.Lerp(Beach, ShallowOcean, 1f - (h / 20f));
                }
                else if (h < 600f)
                {
                    color = Vector4.Lerp(Beach, Grassland, h / 600f);
                }
                else if (h < 1600f)
                {
                    color = Vector4.Lerp(Grassland, Forest, (h - 600f) / 1000f);
                }
                else if (h < 3000f)
                {
                    color = Vector4.Lerp(Forest, Rock, (h - 1600f) / 1400f);
                }
                else
                {
                    color = Vector4.Lerp(Rock, Snow, MathF.Min((h - 3000f) / 1500f, 1f));
                }

                // Blend toward polar ice near the poles regardless of elevation, same as the no-data
                // fallback below, so tiles near a pole don't look out of place next to the rest of
                // the sphere.
                var poleBlend = MathF.Max(0f, (absLatitude01 - 0.75f) / 0.25f);
                return Vector4.Lerp(color, PolarIce, poleBlend);
            }

            var oceanColor = Vector4.Lerp(EquatorOcean, MidLatitudeOcean, MathF.Min(absLatitude01 / 0.75f, 1f));
            var polarBlend = MathF.Max(0f, (absLatitude01 - 0.75f) / 0.25f);
            return Vector4.Lerp(oceanColor, PolarIce, polarBlend);
        }
    }
}
