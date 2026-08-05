namespace ManaGeoTools.Code.GeoTiff
{
    public sealed class GeoTiffLibrary
    {
        public List<GeoTiff> GeoTiffs { get; } = new();

        public GeoTiffLibrary(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine("No folder found for GeoTiffs");
                return;
            }

            var allFiles = Directory.GetFiles(folderPath);
            foreach (var file in allFiles)
            {
                if (!file.ToLowerInvariant().EndsWith(".tif"))
                {
                    continue;
                }

                var geoTiff = new GeoTiff(file);
                GeoTiffs.Add(geoTiff);
                Console.WriteLine($"Loaded {file} @ {geoTiff.Area}");
            }
        }

        public GeoTiff? FindCurrentTile(LatLong point)
        {
            GeoTiff? outputTile = null;
            foreach (var geoTiff in GeoTiffs)
            {
                if (geoTiff.Area.Contains(point))
                {
                    outputTile = geoTiff;
                }
            }

            return outputTile;
        }

        // Decoding a tile (Deflate + floating-point predictor) is CPU-bound and each GeoTiff/
        // GeoTiffReader instance owns its own file handle and state, so tiles are safe to decode
        // concurrently — this is a straightforward win when a caller needs many tiles loaded up
        // front (e.g. building a whole-globe patch grid) rather than one at a time.
        //
        // onTileLoaded, if given, fires once per tile as soon as that individual tile finishes —
        // from whichever worker thread decoded it, not necessarily the calling thread — enabling a
        // caller to progressively apply each tile's data (e.g. swap a placeholder mesh patch for a
        // real one) rather than waiting for every tile in the library to finish.
        public void LoadAllImageData(Action<GeoTiff>? onTileLoaded = null)
        {
            Parallel.ForEach(GeoTiffs, tile =>
            {
                if (!tile.IsDetailLoaded)
                {
                    tile.LoadImageData();
                }
                onTileLoaded?.Invoke(tile);
            });
        }
    }
}
