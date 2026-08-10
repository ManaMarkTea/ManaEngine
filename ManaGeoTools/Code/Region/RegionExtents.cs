using ManaGeoTools.Code.GeoTiff.Terrarium;
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
        public const float BaseHalfExtentMeters = 2000;
        public const int BaseZoom = 13;

        // Upper bound is the Terrarium dataset's own maximum zoom, not a budget choice.
        //
        // The lower bound caps how large a region may be, and 9 (a 64km box at the base extent above) is
        // where the local-tangent-plane model stops being trustworthy rather than where performance gives
        // out. RegionTerrainMesh does correct for the sphere's vertical drop, which is what would
        // otherwise dominate -- 80m at the corners of a 64km box -- but it still treats the east and
        // north axes as parallel everywhere, and meridian convergence grows with the box. 64km comfortably
        // spans a large lake or a mountain range, which is what regions are for; wanting more than that is
        // really wanting the planet.
        public const int MinZoom = 9;
        public const int MaxZoom = 15;

        public static float MinHalfExtentMeters => HalfExtentForZoom(MaxZoom);
        public static float MaxHalfExtentMeters => HalfExtentForZoom(MinZoom);

        /// <summary>
        /// Ground distance between adjacent source samples for a Terrarium tile at this zoom and latitude
        /// -- i.e. the real resolution limit of a region built here, regardless of how dense its mesh is.
        /// Shared so the UI can preview it before a fetch and RegionHeightField can report it after.
        /// </summary>
        public static double MetersPerSample(int zoom, float latitudeDegrees)
        {
            var circumferenceMeters = 2.0 * Math.PI * PlanetLoader.EarthRadiusMeters;
            var cosLatitude = Math.Cos(latitudeDegrees * Math.PI / 180.0);
            return circumferenceMeters * cosLatitude / (Math.Pow(2, zoom) * TerrariumHeightTile.TileSize);
        }

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
