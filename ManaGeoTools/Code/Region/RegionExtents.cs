using ManaGeoTools.Code.GeoTiff.Terrarium;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// The size/detail rule for a RegionNode, plus the local tangent-plane math its mesh is built on.
    ///
    /// A region is a square box of (2 * HalfExtentMeters) on a side centered on a lat/long, built at
    /// 1 scene unit == 1 real meter. Size and source zoom are independent choices, both the caller's.
    ///
    /// Zoom used to be derived from size (one level down per doubling of the box) to hold tile count
    /// constant. Dropped: fetching is fast enough that silently trading resolution for tiles was a bad
    /// deal. Cost now scales with area and with zoom, four-fold per step of either.
    /// </summary>
    public static class RegionExtents
    {
        // Finest zoom Terrarium serves everywhere, and where PlanetNode's up-res tops out
        // (PlanetNode.UpresMaxZoom) -- so a region the planet has already flown to is usually all cached.
        public const int DefaultZoom = 13;

        // Below z8 one tile is wider than the largest region, so going lower changes nothing. 14/15 are
        // offered but only published in places -- a gap surfaces as a failed load naming the zoom.
        public const int MinZoom = 8;
        public const int MaxZoom = 15;

        public const float MinHalfExtentMeters = 500f;

        // 64km across is where the local-tangent-plane model stops being trustworthy: the mesh corrects
        // the sphere's vertical drop, but still treats east/north as parallel everywhere.
        public const float MaxHalfExtentMeters = 32000f;

        // Memory budget, enforced by RegionHeightField: the stitched grid is 256KB per tile, so ~134MB at
        // the limit. Tile count grows with area, with zoom, and with latitude (a tile covers cos(lat) of
        // its equatorial ground), so the largest box at the highest zoom is not loadable everywhere.
        public const int MaxTilesPerRegion = 2048;

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

        public static int ClampZoom(int? zoom)
        {
            return Math.Clamp(zoom ?? DefaultZoom, MinZoom, MaxZoom);
        }

        /// <summary>Exact tile count (the index range, not an area estimate) so the UI can price a region before requesting it.</summary>
        public static long EstimateTileCount(LatLong center, float halfExtentMeters, int zoom)
        {
            var (lonMin, lonMax, latMin, latMax) = ComputeBounds(center, halfExtentMeters);
            return SlippyMapMath.CountTiles(lonMin, lonMax, latMin, latMax, zoom);
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
