using System.IO.Compression;
using ManaGeoTools.Code.Shapefile;

namespace ManaGeoTools.Code.Geo
{
    /// <summary>
    /// Polygon rings from a Natural Earth shapefile, kept with per-ring bounding boxes so a caller can
    /// ask "which rings touch this lat/long box" cheaply.
    ///
    /// Prefiltering is deliberately per RING rather than per record: ne_10m_ocean is a single record
    /// whose bounding box is the entire world, but its 6822 rings each bound one coastline tightly. A
    /// record-level filter would reject nothing.
    /// </summary>
    public sealed class GeoPolygonSet
    {
        private readonly struct Ring
        {
            public required (float Longitude, float Latitude)[] Points { get; init; }
            public required float MinLongitude { get; init; }
            public required float MaxLongitude { get; init; }
            public required float MinLatitude { get; init; }
            public required float MaxLatitude { get; init; }
        }

        private readonly Ring[] _rings;

        public int RingCount => _rings.Length;
        public string Name { get; }

        private GeoPolygonSet(string name, Ring[] rings)
        {
            Name = name;
            _rings = rings;
        }

        /// <summary>Loads every ring from the .shp inside a Natural Earth zip. Null if the file isn't there.</summary>
        public static GeoPolygonSet? TryLoadFromZip(string zipPath, string shpEntryName)
        {
            if (!File.Exists(zipPath))
            {
                return null;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(shpEntryName);
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            var rings = new List<Ring>();
            foreach (var ring in ShapefilePolygonReader.ReadRings(stream))
            {
                if (ring.Count < 3)
                {
                    continue;
                }

                var points = new (float, float)[ring.Count];
                var minLon = float.MaxValue;
                var maxLon = float.MinValue;
                var minLat = float.MaxValue;
                var maxLat = float.MinValue;
                for (var i = 0; i < ring.Count; i++)
                {
                    var longitude = (float)ring[i].Longitude;
                    var latitude = (float)ring[i].Latitude;
                    points[i] = (longitude, latitude);
                    minLon = MathF.Min(minLon, longitude);
                    maxLon = MathF.Max(maxLon, longitude);
                    minLat = MathF.Min(minLat, latitude);
                    maxLat = MathF.Max(maxLat, latitude);
                }

                rings.Add(new Ring
                {
                    Points = points,
                    MinLongitude = minLon,
                    MaxLongitude = maxLon,
                    MinLatitude = minLat,
                    MaxLatitude = maxLat
                });
            }

            return new GeoPolygonSet(Path.GetFileNameWithoutExtension(shpEntryName), rings.ToArray());
        }

        /// <summary>
        /// Even-odd scanline fill of every ring overlapping the box, into a width x height grid of cell
        /// centres. ORs into `coverage` so several sets can accumulate into one mask.
        ///
        /// Scanline rather than per-point ray casting because a mesh asks about tens of thousands of
        /// points inside one box -- filling once and looking up is far cheaper than testing each point
        /// against the geometry. Even-odd also excludes islands for free: shapefile holes arrive as
        /// separate rings, and a point inside a hole crosses two boundaries.
        /// </summary>
        public void RasterizeInto(
            bool[] coverage,
            float lonMin,
            float lonMax,
            float latMin,
            float latMax,
            int width,
            int height)
        {
            if (width <= 0 || height <= 0 || coverage.Length < width * height)
            {
                return;
            }

            var candidates = new List<Ring>();
            foreach (var ring in _rings)
            {
                if (ring.MaxLongitude < lonMin || ring.MinLongitude > lonMax ||
                    ring.MaxLatitude < latMin || ring.MinLatitude > latMax)
                {
                    continue;
                }

                candidates.Add(ring);
            }

            if (candidates.Count == 0)
            {
                return;
            }

            var crossings = new List<float>();
            for (var row = 0; row < height; row++)
            {
                var latitude = latMin + ((row + 0.5f) / height * (latMax - latMin));
                crossings.Clear();

                foreach (var ring in candidates)
                {
                    if (latitude < ring.MinLatitude || latitude > ring.MaxLatitude)
                    {
                        continue;
                    }

                    var points = ring.Points;
                    for (var i = 0; i < points.Length; i++)
                    {
                        var a = points[i];
                        var b = points[(i + 1) % points.Length];
                        if ((a.Latitude <= latitude) == (b.Latitude <= latitude))
                        {
                            continue;
                        }

                        var t = (latitude - a.Latitude) / (b.Latitude - a.Latitude);
                        crossings.Add(a.Longitude + (t * (b.Longitude - a.Longitude)));
                    }
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort();
                var rowOffset = row * width;
                for (var i = 0; i + 1 < crossings.Count; i += 2)
                {
                    var spanStart = crossings[i];
                    var spanEnd = crossings[i + 1];
                    if (spanEnd < lonMin || spanStart > lonMax)
                    {
                        continue;
                    }

                    var firstColumn = (int)MathF.Ceiling(((spanStart - lonMin) / (lonMax - lonMin) * width) - 0.5f);
                    var lastColumn = (int)MathF.Floor(((spanEnd - lonMin) / (lonMax - lonMin) * width) - 0.5f);
                    firstColumn = Math.Max(firstColumn, 0);
                    lastColumn = Math.Min(lastColumn, width - 1);
                    for (var column = firstColumn; column <= lastColumn; column++)
                    {
                        coverage[rowOffset + column] = true;
                    }
                }
            }
        }
    }
}
