namespace ManaGeoTools.Code.Geo
{
    /// <summary>
    /// Answers "is this lat/long water" from Natural Earth polygons, so colouring doesn't have to infer
    /// it from elevation. Lake Michigan sits at ~176m, which any elevation ramp reads as green.
    ///
    /// Terrarium already encodes lake surfaces at water level, so the geometry is flat where it should
    /// be -- this only corrects the colour.
    /// </summary>
    public sealed class GeoWaterMask
    {
        /// <summary>
        /// Set once at startup by the app (it owns the Data folder path); null until then, and null on a
        /// machine without the non-repo Natural Earth files, which degrades to elevation-only colouring.
        /// </summary>
        public static GeoWaterMask? Shared { get; set; }

        // Lakes are tracked separately from ocean, not merged: colouring wants "any water", but the
        // ocean meshes need to EXCLUDE lakes -- an inland lake is water, and is emphatically not sea.
        private readonly List<GeoPolygonSet> _lakeSets = new();
        private readonly List<GeoPolygonSet> _waterSets = new();

        // Whole-globe lake coverage, built once. Cell size is ~10km at the equator, which comfortably
        // resolves anything the ocean shell could render into (Lake Michigan spans ~19x12 cells) while
        // staying at 8MB. Point queries against 1597 lake rings would be far too slow per vertex.
        private const int GlobalLakeRasterWidth = 4096;
        private const int GlobalLakeRasterHeight = 2048;
        private GeoWaterLookup? _globalLakes;

        public bool HasAnyData => _waterSets.Count > 0;
        public IReadOnlyList<GeoPolygonSet> Sets => _waterSets;

        /// <summary>Adds a set counted as water for colouring, and as a lake to be kept clear of ocean geometry.</summary>
        public void AddLakes(GeoPolygonSet? set)
        {
            if (set == null)
            {
                return;
            }

            _lakeSets.Add(set);
            _waterSets.Add(set);

            // Built here, on the loading thread, rather than lazily from the first IsLake call -- that
            // would land inside mesh generation, on whichever thread got there first.
            var coverage = new bool[GlobalLakeRasterWidth * GlobalLakeRasterHeight];
            foreach (var lakeSet in _lakeSets)
            {
                lakeSet.RasterizeInto(coverage, -180f, 180f, -90f, 90f, GlobalLakeRasterWidth, GlobalLakeRasterHeight);
            }

            _globalLakes = new GeoWaterLookup(
                coverage, -180f, 180f, -90f, 90f,
                GlobalLakeRasterWidth, GlobalLakeRasterHeight, buildShoreDistance: false);
        }

        /// <summary>Adds a set counted as water for colouring only.</summary>
        public void Add(GeoPolygonSet? set)
        {
            if (set != null)
            {
                _waterSets.Add(set);
            }
        }

        /// <summary>True inside a lake polygon. O(1) -- backed by the global raster above.</summary>
        public bool IsLake(float longitude, float latitude)
        {
            if (_lakeSets.Count == 0)
            {
                return false;
            }

            return _globalLakes?.IsWater(longitude, latitude) ?? false;
        }

        /// <summary>
        /// A water lookup covering one mesh's lat/long box, rasterized at the mesh's own resolution so
        /// coastlines land within a vertex of where they belong.
        /// </summary>
        public GeoWaterLookup Rasterize(float lonMin, float lonMax, float latMin, float latMax, int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var coverage = new bool[width * height];
            foreach (var set in _waterSets)
            {
                set.RasterizeInto(coverage, lonMin, lonMax, latMin, latMax, width, height);
            }

            return new GeoWaterLookup(coverage, lonMin, lonMax, latMin, latMax, width, height);
        }
    }

    public sealed class GeoWaterLookup
    {
        // Shore distance at which water reads as fully deep. Natural Earth gives outlines but no
        // bathymetry, so depth is inferred from distance to the nearest shore -- which matches real
        // lakes well enough to look right, and keeps ponds shallow while Lake Michigan's middle goes dark.
        private const float FullDepthShoreDistanceMeters = 15000f;

        private const float MetersPerDegreeLatitude = 111195f;

        private readonly bool[] _coverage;
        private readonly float[] _shoreDistanceCells;
        private readonly float _metersPerCell;
        private readonly float _lonMin;
        private readonly float _lonMax;
        private readonly float _latMin;
        private readonly float _latMax;
        private readonly int _width;
        private readonly int _height;

        /// <param name="buildShoreDistance">
        /// Skip for coverage-only lookups. The transform is two sweeps over every cell plus a float per
        /// cell, which is 33MB of pointless work on a whole-globe raster that only answers IsWater.
        /// </param>
        internal GeoWaterLookup(
            bool[] coverage,
            float lonMin,
            float lonMax,
            float latMin,
            float latMax,
            int width,
            int height,
            bool buildShoreDistance = true)
        {
            _coverage = coverage;
            _lonMin = lonMin;
            _lonMax = lonMax;
            _latMin = latMin;
            _latMax = latMax;
            _width = width;
            _height = height;

            var midLatitude = (latMin + latMax) * 0.5f;
            var cellHeightMeters = (latMax - latMin) / height * MetersPerDegreeLatitude;
            var cellWidthMeters = (lonMax - lonMin) / width * MetersPerDegreeLatitude
                * MathF.Max(MathF.Cos(midLatitude * MathF.PI / 180f), 1e-3f);
            _metersPerCell = MathF.Max((cellWidthMeters + cellHeightMeters) * 0.5f, 1e-3f);

            _shoreDistanceCells = buildShoreDistance
                ? BuildShoreDistance(coverage, width, height)
                : Array.Empty<float>();
        }

        public bool IsWater(float longitude, float latitude)
        {
            return TryGetCell(longitude, latitude, out var index) && _coverage[index];
        }

        /// <summary>0 at the shoreline, 1 once FullDepthShoreDistanceMeters from any land. 0 on land.</summary>
        public float GetWaterDepth01(float longitude, float latitude)
        {
            if (_shoreDistanceCells.Length == 0 ||
                !TryGetCell(longitude, latitude, out var index) ||
                !_coverage[index])
            {
                return 0f;
            }

            var meters = _shoreDistanceCells[index] * _metersPerCell;
            return Math.Clamp(meters / FullDepthShoreDistanceMeters, 0f, 1f);
        }

        private bool TryGetCell(float longitude, float latitude, out int index)
        {
            index = 0;
            var spanLon = _lonMax - _lonMin;
            var spanLat = _latMax - _latMin;
            if (spanLon <= 0f || spanLat <= 0f)
            {
                return false;
            }

            var column = (int)((longitude - _lonMin) / spanLon * _width);
            var row = (int)((latitude - _latMin) / spanLat * _height);
            if (column < 0 || column >= _width || row < 0 || row >= _height)
            {
                return false;
            }

            index = (row * _width) + column;
            return true;
        }

        /// <summary>
        /// Two-pass chamfer distance transform: cells-to-nearest-land for every water cell. Approximates
        /// Euclidean distance to within a few percent using 1 / sqrt(2) neighbour weights, in two linear
        /// sweeps rather than the per-cell search an exact transform needs.
        ///
        /// Cells outside the raster are left unconstrained rather than seeded as land, so a box entirely
        /// inside a big lake saturates to full depth instead of falsely shallowing at its edges.
        /// </summary>
        private static float[] BuildShoreDistance(bool[] coverage, int width, int height)
        {
            const float diagonal = 1.41421356f;
            var distance = new float[coverage.Length];
            for (var i = 0; i < coverage.Length; i++)
            {
                distance[i] = coverage[i] ? float.MaxValue : 0f;
            }

            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var index = (row * width) + column;
                    if (distance[index] == 0f)
                    {
                        continue;
                    }

                    var best = distance[index];
                    if (row > 0)
                    {
                        best = MathF.Min(best, distance[index - width] + 1f);
                        if (column > 0)
                        {
                            best = MathF.Min(best, distance[index - width - 1] + diagonal);
                        }

                        if (column + 1 < width)
                        {
                            best = MathF.Min(best, distance[index - width + 1] + diagonal);
                        }
                    }

                    if (column > 0)
                    {
                        best = MathF.Min(best, distance[index - 1] + 1f);
                    }

                    distance[index] = best;
                }
            }

            for (var row = height - 1; row >= 0; row--)
            {
                for (var column = width - 1; column >= 0; column--)
                {
                    var index = (row * width) + column;
                    if (distance[index] == 0f)
                    {
                        continue;
                    }

                    var best = distance[index];
                    if (row + 1 < height)
                    {
                        best = MathF.Min(best, distance[index + width] + 1f);
                        if (column > 0)
                        {
                            best = MathF.Min(best, distance[index + width - 1] + diagonal);
                        }

                        if (column + 1 < width)
                        {
                            best = MathF.Min(best, distance[index + width + 1] + diagonal);
                        }
                    }

                    if (column + 1 < width)
                    {
                        best = MathF.Min(best, distance[index + 1] + 1f);
                    }

                    distance[index] = best;
                }
            }

            return distance;
        }
    }
}
