using ManaGeoTools.Code.Geometry;
using ManaGeoTools.Code.Osm;

namespace ManaGeoTools.Code.Planet
{
    public static class PlanetWaterTessellation
    {
        private const double MaxEdgeMetres = 400.0;
        private const double ShoreEdgeMetres = 20.0;
        private const double ShoreSearchMetres = 180.0;

        public static (List<double> Points, List<int> Triangles, float[] Shore) Build(WaterArea area, double metresPerUnit)
        {
            var corners = Earcut.Triangulate(area.Points, area.HoleStarts);
            if (corners.Count < 3)
            {
                return ([], [], []);
            }
            return Subdivide(area.Points, corners, metresPerUnit, ShoreDistance(area, metresPerUnit));
        }

        // Share midpoint indices between neighbours so refinement cannot open cracks along triangle edges.
        private static (List<double> Points, List<int> Triangles, float[] Shore) Subdivide(double[] source, List<int> corners,
            double metresPerUnit, Func<double, double, float> distance)
        {
            var points = new List<double>(source);
            var triangles = corners;
            var shore = new List<float>();
            for (var p = 0; p < source.Length; p += 2)
            {
                shore.Add(distance(source[p], source[p + 1]));
            }
            for (var round = 0; round < 12 && triangles.Count < 600_000; round++)
            {
                var middles = new Dictionary<(int, int), int>();
                for (var t = 0; t < triangles.Count; t += 3)
                {
                    var a = triangles[t];
                    var b = triangles[t + 1];
                    var c = triangles[t + 2];
                    SplitEdge(middles, a, b);
                    SplitEdge(middles, b, c);
                    SplitEdge(middles, c, a);
                }
                if (middles.Count == 0)
                {
                    break;
                }

                var next = new List<int>(triangles.Count * 2);
                for (var t = 0; t < triangles.Count; t += 3)
                {
                    var a = triangles[t];
                    var b = triangles[t + 1];
                    var c = triangles[t + 2];
                    var hasAb = middles.TryGetValue(Key(a, b), out var ab);
                    var hasBc = middles.TryGetValue(Key(b, c), out var bc);
                    var hasCa = middles.TryGetValue(Key(c, a), out var ca);
                    if (hasAb && hasBc && hasCa)
                    {
                        next.AddRange([a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]);
                    }
                    else if (hasAb && hasBc)
                    {
                        next.AddRange([ab, b, bc, a, ab, bc, a, bc, c]);
                    }
                    else if (hasBc && hasCa)
                    {
                        next.AddRange([bc, c, ca, a, b, bc, a, bc, ca]);
                    }
                    else if (hasCa && hasAb)
                    {
                        next.AddRange([a, ab, ca, ab, b, c, ab, c, ca]);
                    }
                    else if (hasAb)
                    {
                        next.AddRange([a, ab, c, ab, b, c]);
                    }
                    else if (hasBc)
                    {
                        next.AddRange([a, b, bc, a, bc, c]);
                    }
                    else if (hasCa)
                    {
                        next.AddRange([a, b, ca, ca, b, c]);
                    }
                    else
                    {
                        next.AddRange([a, b, c]);
                    }
                }
                triangles = next;
            }
            return (points, triangles, shore.ToArray());

            void SplitEdge(Dictionary<(int, int), int> middles, int a, int b)
            {
                var near = Math.Min(shore[a], shore[b]);
                var limit = Math.Clamp((near - 35.0) * 1.75, ShoreEdgeMetres, MaxEdgeMetres) / metresPerUnit;
                if (Squared(points, a, b) <= limit * limit)
                {
                    return;
                }
                var key = Key(a, b);
                if (middles.ContainsKey(key))
                {
                    return;
                }
                var x = (points[a * 2] + points[b * 2]) * 0.5;
                var y = (points[a * 2 + 1] + points[b * 2 + 1]) * 0.5;
                middles.Add(key, shore.Count);
                points.Add(x);
                points.Add(y);
                shore.Add(distance(x, y));
            }
        }

        private static Func<double, double, float> ShoreDistance(WaterArea area, double metresPerUnit)
        {
            var rings = area.Points;
            var ringCount = rings.Length / 2;
            var reach = ShoreSearchMetres / metresPerUnit;
            var cell = reach * 3.0;
            var grid = new Dictionary<(int, int), List<int>>();
            var starts = new List<int> { 0 };
            starts.AddRange(area.HoleStarts);
            starts.Add(ringCount);
            // Edges by their start point; each ring closes back on its first point.
            var edgeEnd = new int[ringCount];
            for (var r = 0; r + 1 < starts.Count; r++)
            {
                for (var i = starts[r]; i < starts[r + 1]; i++)
                {
                    var next = i + 1;
                    if (next == starts[r + 1])
                    {
                        next = starts[r];
                    }
                    edgeEnd[i] = next;
                    // The straight cuts at the tile (or its buffer) are not shoreline.
                    var ax = rings[i * 2];
                    var ay = rings[i * 2 + 1];
                    var bx = rings[next * 2];
                    var by = rings[next * 2 + 1];
                    if ((ax == bx && (ax <= 0 || ax >= area.Extent)) || (ay == by && (ay <= 0 || ay >= area.Extent)))
                    {
                        continue;
                    }
                    var x0 = (int)Math.Floor(Math.Min(rings[i * 2], rings[next * 2]) / cell);
                    var x1 = (int)Math.Floor(Math.Max(rings[i * 2], rings[next * 2]) / cell);
                    var y0 = (int)Math.Floor(Math.Min(rings[i * 2 + 1], rings[next * 2 + 1]) / cell);
                    var y1 = (int)Math.Floor(Math.Max(rings[i * 2 + 1], rings[next * 2 + 1]) / cell);
                    for (var gx = x0; gx <= x1; gx++)
                    {
                        for (var gy = y0; gy <= y1; gy++)
                        {
                            if (!grid.TryGetValue((gx, gy), out var list))
                            {
                                list = new List<int>();
                                grid[(gx, gy)] = list;
                            }
                            list.Add(i);
                        }
                    }
                }
            }

            return (px, py) =>
            {
                var best = reach * reach;
                var x0 = (int)Math.Floor((px - reach) / cell);
                var x1 = (int)Math.Floor((px + reach) / cell);
                var y0 = (int)Math.Floor((py - reach) / cell);
                var y1 = (int)Math.Floor((py + reach) / cell);
                for (var gx = x0; gx <= x1; gx++)
                {
                    for (var gy = y0; gy <= y1; gy++)
                    {
                        if (!grid.TryGetValue((gx, gy), out var list))
                        {
                            continue;
                        }
                        foreach (var i in list)
                        {
                            best = Math.Min(best, SegmentSquared(px, py, rings[i * 2], rings[i * 2 + 1], rings[edgeEnd[i] * 2], rings[edgeEnd[i] * 2 + 1]));
                        }
                    }
                }
                return (float)(Math.Sqrt(best) * metresPerUnit);
            };
        }

        private static double SegmentSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var length = dx * dx + dy * dy;
            var t = 0.0;
            if (length > 0.0)
            {
                t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / length, 0.0, 1.0);
            }
            var ex = ax + dx * t - px;
            var ey = ay + dy * t - py;
            return ex * ex + ey * ey;
        }

        private static (int, int) Key(int a, int b)
        {
            if (a < b)
            {
                return (a, b);
            }
            return (b, a);
        }

        private static double Squared(List<double> points, int a, int b)
        {
            var dx = points[a * 2] - points[b * 2];
            var dy = points[a * 2 + 1] - points[b * 2 + 1];
            return dx * dx + dy * dy;
        }
    }
}
