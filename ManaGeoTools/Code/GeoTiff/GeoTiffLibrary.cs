namespace ManaGeoTools.Code.GeoTiff
{
    public sealed class GeoTiffLibrary
    {
        // IHeightTile rather than GeoTiff so callers can inject other tile sources (e.g. an
        // on-demand-fetched Terrarium tile, see Terrarium/TerrariumHeightTile.cs) into the same
        // lookup pipeline without every consumer needing to know which kind of tile answered a given
        // query -- see IHeightTile's doc comment.
        private readonly List<IHeightTile> _tiles = new();
        public IReadOnlyList<IHeightTile> Tiles => _tiles;

        // FindCurrentTile runs once per vertex of every patch's GenerateData() -- tens of thousands of
        // calls for a single up-res'd (160-resolution) patch -- and this library's tile count grows
        // into the hundreds-to-thousands as more patches get on-demand up-res'd (each fetch can add up
        // to ~1000 Terrarium tiles). A plain linear scan over every tile for every single query became
        // a real bottleneck once the library grew large: a multi-second GenerateData() call, which in
        // turn stalled the whole app, since it runs under PlanetNode._rebuildLock -- a lock the main
        // thread also needs every frame. Bucketing tiles by a coarse lat/long grid cell means a query
        // only scans the (typically tiny) handful of tiles actually near that point, regardless of how
        // many thousands of tiles the library holds overall. A tile whose bounding box spans multiple
        // cells (the whole-globe-ish baseline ETOPO file, in particular) is registered into every cell
        // it overlaps -- a one-time cost paid when the tile is added, not per query.
        private const float GridCellDegrees = 1f;
        private readonly Dictionary<(int LonCell, int LatCell), List<IHeightTile>> _grid = new();

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
                AddTile(geoTiff);
                Console.WriteLine($"Loaded {file} @ {geoTiff.Area}");
            }
        }

        // Not self-locking -- callers that mutate a library already in use by a PlanetNode must hold
        // its _rebuildLock themselves, the same way every other mutation of this library's contents is
        // already serialized (see PlanetNode.ApplyTileToOverlappingPatches/RebuildAllPatches).
        public void AddTile(IHeightTile tile)
        {
            _tiles.Add(tile);

            // Mirrors LatLongArea.Contains's own choice of corners for min/max, so indexing and the
            // actual containment test always agree on what a tile's bounding box is.
            var minLon = tile.Area.UpperLeft.Longitude;
            var maxLon = tile.Area.LowerRight.Longitude;
            var minLat = tile.Area.LowerLeft.Latitude;
            var maxLat = tile.Area.UpperRight.Latitude;

            var minLonCell = (int)MathF.Floor(minLon / GridCellDegrees);
            var maxLonCell = (int)MathF.Floor(maxLon / GridCellDegrees);
            var minLatCell = (int)MathF.Floor(minLat / GridCellDegrees);
            var maxLatCell = (int)MathF.Floor(maxLat / GridCellDegrees);

            for (var lonCell = minLonCell; lonCell <= maxLonCell; lonCell++)
            {
                for (var latCell = minLatCell; latCell <= maxLatCell; latCell++)
                {
                    var cell = (lonCell, latCell);
                    if (!_grid.TryGetValue(cell, out var bucket))
                    {
                        bucket = new List<IHeightTile>();
                        _grid[cell] = bucket;
                    }
                    bucket.Add(tile);
                }
            }
        }

        public IHeightTile? FindCurrentTile(LatLong point)
        {
            var cell = ((int)MathF.Floor(point.Longitude / GridCellDegrees), (int)MathF.Floor(point.Latitude / GridCellDegrees));
            if (!_grid.TryGetValue(cell, out var bucket))
            {
                return null;
            }

            // Scan newest-first (AddTile appends, so walk the bucket backward) and stop at the first
            // match, instead of scanning the whole bucket and keeping the last match like a naive
            // linear scan would. Both give the identical result -- the most-recently-added tile that
            // contains the point always wins -- but this resolves in a handful of checks rather than
            // the whole bucket for the common case: a query is usually answered by whichever
            // (typically small, precise) tile was fetched most recently for roughly that area, e.g. a
            // patch's own just-finished Terrarium fetch. This matters even after the grid-bucketing
            // above, since repeatedly drilling into the same small area (splitting a patch, then
            // splitting one of its children, and so on) concentrates many tiles into the same one or
            // two cells rather than spreading them out.
            for (var i = bucket.Count - 1; i >= 0; i--)
            {
                if (bucket[i].Area.Contains(point))
                {
                    return bucket[i];
                }
            }

            return null;
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
        //
        // Only real GeoTiff entries go through this lazy-decode step -- other IHeightTile kinds (e.g.
        // Terrarium tiles) already arrive fully decoded at construction time.
        public void LoadAllImageData(Action<GeoTiff>? onTileLoaded = null, Func<bool>? shouldContinue = null)
        {
            Parallel.ForEach(_tiles.OfType<GeoTiff>(), tile =>
            {
                if (shouldContinue != null && !shouldContinue())
                {
                    return;
                }
                if (!tile.IsDetailLoaded)
                {
                    tile.LoadImageData();
                }
                onTileLoaded?.Invoke(tile);
            });
        }
    }
}
