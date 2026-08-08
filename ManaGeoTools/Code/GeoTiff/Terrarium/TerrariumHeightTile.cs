namespace ManaGeoTools.Code.GeoTiff.Terrarium
{
    /// <summary>
    /// One decoded 256x256 Terrarium elevation tile (see TerrariumTileFetcher for how the raw PNG gets
    /// turned into `heights`), usable anywhere an IHeightTile is -- see IHeightTile's doc comment.
    /// GetHeightAtLatLong reprojects the query point through the same Web Mercator math used to fetch
    /// this tile in the first place, rather than relying on LatLongArea.PixelSize/GeoTiff's
    /// linear-in-latitude pixel math (SlippyMapMath.cs's doc comment explains why that doesn't apply
    /// to a Mercator tile).
    /// </summary>
    public sealed class TerrariumHeightTile : IHeightTile
    {
        public const int TileSize = 256;

        public int Zoom { get; }
        public int TileX { get; }
        public int TileY { get; }
        public LatLongArea Area { get; }

        private readonly float[] _heights;

        // Raw row-major TileSize x TileSize sample data -- exposed for the GPU height-sampling path
        // (see IHeightSamplingComputeDevice/PlanetNode.UpresPatchAsync), which needs to upload a tile's
        // actual pixels rather than query them one point at a time.
        public ReadOnlyMemory<float> HeightData => _heights;

        public TerrariumHeightTile(int zoom, int tileX, int tileY, float[] heights)
        {
            if (heights.Length != TileSize * TileSize)
            {
                throw new ArgumentException($"Expected {TileSize * TileSize} height samples, got {heights.Length}.", nameof(heights));
            }

            Zoom = zoom;
            TileX = tileX;
            TileY = tileY;
            _heights = heights;

            var upperLeft = SlippyMapMath.TileCornerToLatLong(tileX, tileY, zoom);
            var lowerRight = SlippyMapMath.TileCornerToLatLong(tileX + 1, tileY + 1, zoom);
            var upperRight = LatLong.FromLongitudeAndLatitude(lowerRight.Longitude, upperLeft.Latitude);
            var lowerLeft = LatLong.FromLongitudeAndLatitude(upperLeft.Longitude, lowerRight.Latitude);
            Area = new LatLongArea(upperLeft, upperRight, lowerLeft, lowerRight);
        }

        public float GetHeightAtLatLong(LatLong location)
        {
            var (tileCoordX, tileCoordY) = SlippyMapMath.LatLongToTileCoords(location.Longitude, location.Latitude, Zoom);
            var localX = (int)((tileCoordX - TileX) * TileSize);
            var localY = (int)((tileCoordY - TileY) * TileSize);

            var pX = Math.Clamp(localX, 0, TileSize - 1);
            var pY = Math.Clamp(localY, 0, TileSize - 1);
            return _heights[pY * TileSize + pX];
        }
    }
}
