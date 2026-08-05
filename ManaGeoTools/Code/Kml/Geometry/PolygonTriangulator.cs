namespace ManaGeoTools.Code.Kml.Geometry
{
    /// <summary>
    /// Ear-clipping triangulation of a simple (non-self-intersecting), not-necessarily-convex 2D
    /// polygon. Replaces Godot's Geometry2D.triangulate_polygon/triangulate_delaunay, neither of
    /// which has a .NET equivalent. Consecutive coincident points (including a closing point that
    /// repeats the first, as KML rings conventionally have) are deduped before triangulating. There
    /// is no Delaunay fallback here — if ear-clipping stalls on a degenerate/self-intersecting ring,
    /// whatever ears were already clipped are kept and triangulation stops early rather than
    /// looping forever.
    /// </summary>
    public static class PolygonTriangulator
    {
        private const float Epsilon = 1e-7f;

        /// <summary>Returns a flat list of triangle index triples into <paramref name="points"/>.</summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> points)
        {
            var triangles = new List<int>();

            // KML rings conventionally repeat the first coordinate as the last one to close the
            // ring. Left in, that duplicate sits exactly on top of another vertex, and the
            // point-in-triangle test below can't distinguish "coincident with a triangle corner"
            // from "genuinely inside" — it starves otherwise-valid ears near the seam and can stall
            // triangulation well before the whole polygon is covered. Dedupe first so the algorithm
            // only ever sees distinct points.
            var distinctIndices = DedupeConsecutive(points);
            var count = distinctIndices.Count;
            if (count < 3)
            {
                return triangles;
            }

            var signedArea = SignedArea(points, distinctIndices);
            var isClockwise = signedArea < 0;

            // The convexity test below assumes CCW winding; triangulate on a reversed index order
            // for CW input, then remap back to original indices when emitting triangles.
            var remaining = new List<int>(count);
            for (var i = 0; i < count; i++)
            {
                remaining.Add(isClockwise ? distinctIndices[count - 1 - i] : distinctIndices[i]);
            }

            var guard = 0;
            var maxIterations = count * count;
            while (remaining.Count > 3 && guard < maxIterations)
            {
                guard++;
                var earFound = false;

                for (var i = 0; i < remaining.Count; i++)
                {
                    var prevIndex = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    var currIndex = remaining[i];
                    var nextIndex = remaining[(i + 1) % remaining.Count];

                    if (!IsConvex(points[prevIndex], points[currIndex], points[nextIndex]))
                    {
                        continue;
                    }

                    var isEar = true;
                    foreach (var otherIndex in remaining)
                    {
                        if (otherIndex == prevIndex || otherIndex == currIndex || otherIndex == nextIndex)
                        {
                            continue;
                        }

                        if (PointInTriangle(points[otherIndex], points[prevIndex], points[currIndex], points[nextIndex]))
                        {
                            isEar = false;
                            break;
                        }
                    }

                    if (!isEar)
                    {
                        continue;
                    }

                    triangles.Add(prevIndex);
                    triangles.Add(currIndex);
                    triangles.Add(nextIndex);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    // Degenerate/self-intersecting ring: stop early with whatever ears were clipped.
                    break;
                }
            }

            if (remaining.Count == 3)
            {
                triangles.Add(remaining[0]);
                triangles.Add(remaining[1]);
                triangles.Add(remaining[2]);
            }

            return triangles;
        }

        private static List<int> DedupeConsecutive(IReadOnlyList<Vector2> points)
        {
            var kept = new List<int>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                if (kept.Count == 0 || Vector2.DistanceSquared(points[kept[^1]], points[i]) > Epsilon)
                {
                    kept.Add(i);
                }
            }

            // Closing duplicate: last point coincides with the first.
            if (kept.Count > 1 && Vector2.DistanceSquared(points[kept[0]], points[kept[^1]]) <= Epsilon)
            {
                kept.RemoveAt(kept.Count - 1);
            }

            return kept;
        }

        private static float SignedArea(IReadOnlyList<Vector2> points, IReadOnlyList<int> indices)
        {
            float area = 0;
            for (var i = 0; i < indices.Count; i++)
            {
                var a = points[indices[i]];
                var b = points[indices[(i + 1) % indices.Count]];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area * 0.5f;
        }

        private static bool IsConvex(Vector2 prev, Vector2 curr, Vector2 next)
        {
            var cross = Cross(curr - prev, next - curr);
            return cross > Epsilon;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);

            var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
            var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;

            return !(hasNegative && hasPositive);
        }
    }
}
