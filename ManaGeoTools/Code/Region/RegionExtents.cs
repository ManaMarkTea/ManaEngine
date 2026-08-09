using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// The size/detail rule for a RegionNode, plus the local tangent-plane math its mesh is built on.
    ///
    /// A region is always a square box of (2 * HalfExtentMeters) on a side centered on a lat/long, built
    /// at 1 scene unit == 1 real meter. Source detail is tied to size rather than chosen independently:
    /// 500m half-extent is served by zoom 13, and every doubling of the half-extent drops one zoom level
    /// (1km -> z12, 2km -> z11, ...). That keeps the SAMPLE COUNT across the box roughly constant no
    /// matter how large an area is asked for, so tile count, fetch time and vertex budget all stay flat
    /// -- a bigger region trades resolution for coverage instead of getting proportionally more
    /// expensive. Zoom 13 is also exactly where PlanetNode's own up-res streaming tops out
    /// (PlanetNode.UpresMaxZoom), so a 500m region over a place the planet has already flown to is
    /// usually served entirely from TerrariumTileFetcher's on-disk cache with no network round trip.
    /// </summary>
    public static class RegionExtents
    {
        public const float BaseHalfExtentMeters = 500f;
        public const int BaseZoom = 13;

        // Upper bound is the Terrarium dataset's own maximum zoom, not a budget choice. The lower bound
        // is a budget choice: below it the box is wide enough that a flat tangent plane stops being a
        // reasonable approximation of the curved surface anyway.
        public const int MinZoom = 6;
        public const int MaxZoom = 15;

        public static float MinHalfExtentMeters => HalfExtentForZoom(MaxZoom);
        public static float MaxHalfExtentMeters => HalfExtentForZoom(MinZoom);

        /// <summary>Meters per degree of latitude on the sphere the rest of this codebase assumes (PlanetLoader.EarthRadiusMeters).</summary>
        public const float MetersPerDegreeLatitude = PlanetLoader.EarthRadiusMeters * MathF.PI / 180f;

        /// <summary>
        /// Meters per degree of longitude at the given latitude -- shrinks to zero at the poles, which is
        /// why this takes a latitude and MetersPerDegreeLatitude doesn't. Evaluated once at the region's
        /// center and held constant across the whole box (rather than re-evaluated per row): over a box
        /// this small the variation is far below the source data's own sample spacing.
        /// </summary>
        public static float MetersPerDegreeLongitude(float latitudeDegrees)
        {
            var cosLatitude = MathF.Cos(latitudeDegrees * MathF.PI / 180f);
            // Guard the poles so a region requested there degenerates into a very wide longitude span
            // instead of dividing by zero.
            return MetersPerDegreeLatitude * MathF.Max(cosLatitude, 1e-4f);
        }

        public static int ZoomForHalfExtent(float halfExtentMeters)
        {
            var doublings = MathF.Log2(MathF.Max(halfExtentMeters, 1f) / BaseHalfExtentMeters);
            return Math.Clamp(BaseZoom - (int)MathF.Round(doublings), MinZoom, MaxZoom);
        }

        public static float HalfExtentForZoom(int zoom)
        {
            return BaseHalfExtentMeters * MathF.Pow(2f, BaseZoom - Math.Clamp(zoom, MinZoom, MaxZoom));
        }

        /// <summary>The lon/lat bounding box of a region, from its center and half-extent.</summary>
        public static (float LonMin, float LonMax, float LatMin, float LatMax) ComputeBounds(
            LatLong center,
            float halfExtentMeters)
        {
            var latitudeSpan = halfExtentMeters / MetersPerDegreeLatitude;
            var longitudeSpan = halfExtentMeters / MetersPerDegreeLongitude(center.Latitude);
            return (
                center.Longitude - longitudeSpan,
                center.Longitude + longitudeSpan,
                center.Latitude - latitudeSpan,
                center.Latitude + latitudeSpan);
        }
    }
}
