using System.Numerics;
using ManaLidar;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The scan simplifier: a flat patch collapses to almost nothing without losing its outline, a curved one keeps
/// its shape to the given error, and pieces cut from one surface weld back together first.
/// </summary>
public static class ScanSimplifyChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- flat
        var flat = Grid(20, (x, y) => 0f);
        var flatSimple = ScanMeshSimplifier.Simplify(flat, 0.002f);
        Check("a flat 1 m patch collapses to a few triangles", flatSimple.TriangleCount <= 12,
            $"{flat.TriangleCount} -> {flatSimple.TriangleCount}");
        Check("covering the same area", Math.Abs(Area(flatSimple) - 1f) < 1e-3f, $"{Area(flatSimple):F4} m2");
        Check("with its corners where they were", Corners(flatSimple),
            string.Join(" ", flatSimple.Positions.Select(p => $"({p.X:F2},{p.Y:F2})")));
        Check("and every triangle still facing up", FacesAlong(flatSimple, Vector3.UnitZ));

        // ---------------------------------------------------------------- curved
        // A dome 10 cm high: simplified to 3 mm it must stay within a few mm of the true surface.
        float Dome(float x, float y) => 0.1f * MathF.Max(0, 1 - ((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 4);
        var dome = Grid(40, Dome);
        var domeSimple = ScanMeshSimplifier.Simplify(dome, 0.003f);
        var worst = 0f;
        for (var t = 0; t < domeSimple.TriangleCount; t++)
        {
            // Off-vertex error matters as much as at the vertices: check triangle centres too.
            var c = (domeSimple.Positions[domeSimple.Indices[t * 3]] + domeSimple.Positions[domeSimple.Indices[t * 3 + 1]]
                + domeSimple.Positions[domeSimple.Indices[t * 3 + 2]]) / 3;
            worst = MathF.Max(worst, MathF.Abs(c.Z - Dome(c.X, c.Y)));
        }
        Check("a dome loses most of its triangles", domeSimple.TriangleCount < dome.TriangleCount / 4,
            $"{dome.TriangleCount} -> {domeSimple.TriangleCount}");
        Check("but keeps its shape to within a few millimetres", worst < 0.012f, $"worst {worst * 1000:F1} mm");
        Check("and does not turn any triangle over", FacesAlong(domeSimple, Vector3.UnitZ));

        // ---------------------------------------------------------------- welding
        var left = Grid(4, (x, y) => 0f, offsetX: 0f, width: 0.5f);
        var right = Grid(4, (x, y) => 0f, offsetX: 0.5f, width: 0.5f);
        var welded = ScanMeshSimplifier.Weld(new[] { left, right }, Guid.NewGuid());
        Check("pieces that share an edge weld along it", welded.Positions.Length == left.Positions.Length * 2 - 5,
            $"{welded.Positions.Length} vertices");
        var weldedSimple = ScanMeshSimplifier.Simplify(welded, 0.002f);
        Check("and then simplify as one", weldedSimple.TriangleCount <= 12, $"{weldedSimple.TriangleCount} triangles");
    }

    // An n x n grid over [offsetX, offsetX + width] x [0, width], height from f, facing +Z.
    private static ScanMeshPiece Grid(int n, Func<float, float, float> f, float offsetX = 0f, float width = 1f)
    {
        var positions = new List<Vector3>();
        for (var y = 0; y <= n; y++)
        {
            for (var x = 0; x <= n; x++)
            {
                var px = offsetX + width * x / n;
                var py = width * y / n;
                positions.Add(new Vector3(px, py, f(px, py)));
            }
        }
        var indices = new List<int>();
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var a = y * (n + 1) + x;
                indices.AddRange(new[] { a, a + 1, a + n + 2, a, a + n + 2, a + n + 1 });
            }
        }
        return new ScanMeshPiece(Guid.NewGuid(), positions.ToArray(), positions.Select(_ => Vector3.UnitZ).ToArray(), indices.ToArray());
    }

    private static float Area(ScanMeshPiece piece)
    {
        var area = 0f;
        for (var t = 0; t < piece.TriangleCount; t++)
        {
            var a = piece.Positions[piece.Indices[t * 3]];
            area += Vector3.Cross(piece.Positions[piece.Indices[t * 3 + 1]] - a, piece.Positions[piece.Indices[t * 3 + 2]] - a).Length() * 0.5f;
        }
        return area;
    }

    private static bool Corners(ScanMeshPiece piece) => new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One }
        .All(c => piece.Positions.Any(p => Vector2.Distance(new Vector2(p.X, p.Y), c) < 1e-4f));

    private static bool FacesAlong(ScanMeshPiece piece, Vector3 direction)
    {
        for (var t = 0; t < piece.TriangleCount; t++)
        {
            var a = piece.Positions[piece.Indices[t * 3]];
            var cross = Vector3.Cross(piece.Positions[piece.Indices[t * 3 + 1]] - a, piece.Positions[piece.Indices[t * 3 + 2]] - a);
            if (Vector3.Dot(cross, direction) <= 0)
            {
                return false;
            }
        }
        return true;
    }
}
