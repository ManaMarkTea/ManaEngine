using ManaGeoTools.Code.GeoTiff.Terrarium;

namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// Every Terrarium tile covering a region's bounding box, stitched into one contiguous row-major
    /// elevation grid in slippy-map pixel space at a single zoom, with bilinear sampling on top.
    ///
    /// Why not just query TerrariumHeightTile.GetHeightAtLatLong per point, the way the planet's CPU
    /// sampling path does: that's a nearest-pixel lookup, and it also can't see across a tile boundary.
    /// At planet scale (a patch spanning tens of kilometers of scene at a heavily shrunk radius) neither
    /// matters. At a region's 1:1 meter scale both are immediately visible -- source pixels are ~19m
    /// apart at zoom 13 while mesh vertices are only a few meters apart, so nearest sampling produces
    /// literal terraced steps, and a region box straddling a tile seam is the common case, not the
    /// exception. Stitching first and interpolating after fixes both.
    /// </summary>
    public sealed class RegionHeightField
    {
        private const int TileSize = TerrariumHeightTile.TileSize;

        private readonly int _zoom;
        private readonly int _tileXMin;
        private readonly int _tileYMin;
        private readonly int _width;
        private readonly int _height;
        private readonly float[] _samples;

        public int Zoom => _zoom;
        public int TileCount { get; }

        /// <summary>Real-world meters between adjacent source samples at this field's zoom and latitude -- purely informational (e.g. a UI readout of how detailed the loaded region actually is).</summary>
        public double MetersPerSample { get; }

        private RegionHeightField(int zoom, int tileXMin, int tileYMin, int tilesWide, int tilesHigh, float[] samples, float centerLatitudeDegrees)
        {
            _zoom = zoom;
            _tileXMin = tileXMin;
            _tileYMin = tileYMin;
            _width = tilesWide * TileSize;
            _height = tilesHigh * TileSize;
            _samples = samples;
            TileCount = tilesWide * tilesHigh;

            var worldCircumferenceMeters = 2.0 * Math.PI * Planet.PlanetLoader.EarthRadiusMeters;
            var cosLatitude = Math.Cos(centerLatitudeDegrees * Math.PI / 180.0);
            MetersPerSample = worldCircumferenceMeters * cosLatitude / (Math.Pow(2, zoom) * TileSize);
        }

        /// <summary>
        /// Fetches and stitches every tile covering the box. Returns null if ANY tile is unavailable --
        /// a region is small enough that a missing tile would be a visible hole or cliff right in the
        /// middle of it, so failing the whole load (and telling the user) beats silently filling gaps
        /// with a made-up elevation.
        /// </summary>
        public static async Task<RegionHeightField?> FetchAsync(
            float lonMin,
            float lonMax,
            float latMin,
            float latMax,
            float centerLatitudeDegrees,
            int zoom)
        {
            var tiles = SlippyMapMath.EnumerateTiles(lonMin, lonMax, latMin, latMax, zoom).ToList();
            if (tiles.Count == 0)
            {
                return null;
            }

            var tileXMin = tiles.Min(tile => tile.X);
            var tileXMax = tiles.Max(tile => tile.X);
            var tileYMin = tiles.Min(tile => tile.Y);
            var tileYMax = tiles.Max(tile => tile.Y);
            var tilesWide = tileXMax - tileXMin + 1;
            var tilesHigh = tileYMax - tileYMin + 1;

            // A region's tile count is bounded by construction (RegionExtents ties zoom to extent so the
            // box is only ever a handful of tiles across), so all of them can be in flight at once
            // without the concurrency throttle PlanetNode's unbounded up-res streaming needs.
            var fetched = await Task.WhenAll(tiles.Select(tile => TerrariumTileFetcher.FetchTileAsync(zoom, tile.X, tile.Y)));

            var width = tilesWide * TileSize;
            var samples = new float[width * tilesHigh * TileSize];
            for (var i = 0; i < fetched.Length; i++)
            {
                var tile = fetched[i];
                if (tile == null)
                {
                    return null;
                }

                var destinationX = (tile.TileX - tileXMin) * TileSize;
                var destinationY = (tile.TileY - tileYMin) * TileSize;
                var source = tile.HeightData.Span;
                for (var row = 0; row < TileSize; row++)
                {
                    source.Slice(row * TileSize, TileSize)
                        .CopyTo(samples.AsSpan(((destinationY + row) * width) + destinationX, TileSize));
                }
            }

            return new RegionHeightField(zoom, tileXMin, tileYMin, tilesWide, tilesHigh, samples, centerLatitudeDegrees);
        }

        /// <summary>Bilinearly interpolated elevation in meters at the given lon/lat.</summary>
        public float SampleMeters(float longitudeDegrees, float latitudeDegrees)
        {
            var (tileCoordX, tileCoordY) = SlippyMapMath.LatLongToTileCoords(longitudeDegrees, latitudeDegrees, _zoom);

            // -0.5 shifts from "pixel index space" to "pixel CENTER space": sample n covers the span
            // [n, n+1) and its value belongs at n+0.5, so interpolating between neighbours without this
            // would bias every lookup half a pixel north-west.
            var x = ((tileCoordX - _tileXMin) * TileSize) - 0.5;
            var y = ((tileCoordY - _tileYMin) * TileSize) - 0.5;

            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = (float)(x - x0);
            var fy = (float)(y - y0);

            var x0c = Math.Clamp(x0, 0, _width - 1);
            var x1c = Math.Clamp(x0 + 1, 0, _width - 1);
            var y0c = Math.Clamp(y0, 0, _height - 1);
            var y1c = Math.Clamp(y0 + 1, 0, _height - 1);

            var topLeft = _samples[(y0c * _width) + x0c];
            var topRight = _samples[(y0c * _width) + x1c];
            var bottomLeft = _samples[(y1c * _width) + x0c];
            var bottomRight = _samples[(y1c * _width) + x1c];

            var top = float.Lerp(topLeft, topRight, fx);
            var bottom = float.Lerp(bottomLeft, bottomRight, fx);
            return float.Lerp(top, bottom, fy);
        }
    }
}
