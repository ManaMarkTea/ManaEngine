namespace ManaGeoTools.Code.GeoTiff.Terrarium
{
    /// <summary>
    /// Standard Web Mercator "slippy map" XYZ tile math (the same formulas every OSM/Leaflet-style tile
    /// client uses) -- nothing in this codebase needed this before Terrarium tile fetching, so it's
    /// written out explicitly here rather than assumed. Mercator is undefined at the poles, so latitude
    /// is always clamped to +/-MaxLatitudeDegrees before projecting.
    /// </summary>
    public static class SlippyMapMath
    {
        public const float MaxLatitudeDegrees = 85.0511287798f;

        /// <summary>Floating-point tile coordinates (NOT yet floored to an integer tile index) at zoom z.</summary>
        public static (double X, double Y) LatLongToTileCoords(float longitudeDegrees, float latitudeDegrees, int zoom)
        {
            var n = Math.Pow(2, zoom);
            var latRad = Math.Clamp(latitudeDegrees, -MaxLatitudeDegrees, MaxLatitudeDegrees) * Math.PI / 180.0;

            var x = n * (longitudeDegrees + 180.0) / 360.0;
            var y = n * (1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0;
            return (x, y);
        }

        /// <summary>The lon/lat of tile (tileX, tileY)'s north-west (upper-left) corner at zoom z.</summary>
        public static LatLong TileCornerToLatLong(int tileX, int tileY, int zoom)
        {
            var n = Math.Pow(2, zoom);
            var longitude = tileX / n * 360.0 - 180.0;
            var latRad = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * tileY / n)));
            var latitude = latRad * 180.0 / Math.PI;
            return LatLong.FromLongitudeAndLatitude((float)longitude, (float)latitude);
        }

        /// <summary>
        /// Picks the highest zoom (at or below maxZoom) whose tile coverage of the given lon/lat box
        /// stays within maxTiles -- walks zoom down from maxZoom rather than up, since a caller wants
        /// the FINEST resolution it can afford, not the coarsest that happens to fit. Falls back to
        /// zoom 0 (a single tile, always within any positive maxTiles) if nothing smaller qualifies.
        /// A bounding box wider than 180 degrees of longitude (spanning the antimeridian) isn't
        /// supported -- returns null, an explicit v1 limitation rather than silently wrapping wrong.
        /// </summary>
        public static int? PickZoom(float lonMin, float lonMax, float latMin, float latMax, int maxTiles, int maxZoom)
        {
            if (lonMax - lonMin > 180.0f)
            {
                return null;
            }

            for (var zoom = maxZoom; zoom >= 0; zoom--)
            {
                var (xMin, xMax, yMin, yMax) = TileIndexRange(lonMin, lonMax, latMin, latMax, zoom);
                var count = (long)(xMax - xMin + 1) * (yMax - yMin + 1);
                if (count <= maxTiles)
                {
                    return zoom;
                }
            }

            return 0;
        }

        /// <summary>How many tiles EnumerateTiles would yield, without enumerating them. Long, because the point of asking is that it might be huge.</summary>
        public static long CountTiles(float lonMin, float lonMax, float latMin, float latMax, int zoom)
        {
            var (xMin, xMax, yMin, yMax) = TileIndexRange(lonMin, lonMax, latMin, latMax, zoom);
            return (long)(xMax - xMin + 1) * (yMax - yMin + 1);
        }

        public static IEnumerable<(int X, int Y)> EnumerateTiles(float lonMin, float lonMax, float latMin, float latMax, int zoom)
        {
            var (xMin, xMax, yMin, yMax) = TileIndexRange(lonMin, lonMax, latMin, latMax, zoom);
            for (var y = yMin; y <= yMax; y++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    yield return (x, y);
                }
            }
        }

        // Latitude maps to Y with north at the top (smaller Y), so latMax (the box's northern edge)
        // gives the smaller Y bound, not the larger one.
        private static (int XMin, int XMax, int YMin, int YMax) TileIndexRange(float lonMin, float lonMax, float latMin, float latMax, int zoom)
        {
            var n = 1 << zoom;
            var (xMinF, yMinF) = LatLongToTileCoords(lonMin, latMax, zoom);
            var (xMaxF, yMaxF) = LatLongToTileCoords(lonMax, latMin, zoom);

            var xMin = Math.Clamp((int)Math.Floor(xMinF), 0, n - 1);
            var xMax = Math.Clamp((int)Math.Floor(xMaxF), 0, n - 1);
            var yMin = Math.Clamp((int)Math.Floor(yMinF), 0, n - 1);
            var yMax = Math.Clamp((int)Math.Floor(yMaxF), 0, n - 1);
            return (xMin, xMax, yMin, yMax);
        }
    }
}
