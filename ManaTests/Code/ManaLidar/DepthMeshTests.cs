using System.Numerics;
using ManaLidar;
using ManaUI;

namespace ManaTests;

/// <summary>ARCore depth to a mesh: a flat wall stays flat and faces the camera, and a depth jump is cut, not bridged.</summary>
public static class DepthMeshChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        ScanKeyframe Frame(float[] depth, Matrix4x4 pose) =>
            new(8, 6, new byte[8 * 6 * 3], pose, fx: 8, fy: 8, cx: 4, cy: 3, timestamp: 0, depthWidth: 8, depthHeight: 6, depth: depth);

        var wall = Enumerable.Repeat(2f, 48).ToArray();
        var piece = ScanDepthMesher.Build(Frame(wall, Matrix4x4.Identity));
        check("a flat wall meshes every cell", piece is { TriangleCount: 7 * 5 * 2 }, $"{piece?.TriangleCount}");
        check("it sits 2 m down -Z", piece != null && piece.Positions.All(p => MathF.Abs(p.Z + 2) < 1e-4f));
        check("and faces the camera", piece != null && piece.Normals.All(n => n.Z > 0.99f), piece?.Normals[0].ToString() ?? "");

        // The right half is 4 m back: no triangle may join the two halves.
        var step = wall.ToArray();
        for (var y = 0; y < 6; y++)
        {
            for (var x = 4; x < 8; x++)
            {
                step[y * 8 + x] = 4f;
            }
        }
        var cut = ScanDepthMesher.Build(Frame(step, Matrix4x4.Identity));
        check("a depth jump is left open", cut is { TriangleCount: 6 * 5 * 2 }, $"{cut?.TriangleCount}");

        var moved = ScanDepthMesher.Build(Frame(wall, Matrix4x4.CreateTranslation(0, 1, 0)));
        check("the camera pose carries it into the world", moved != null && MathF.Abs(moved.Positions.Average(p => p.Y) - 1) < 0.01f);
        check("no depth gives no piece", ScanDepthMesher.Build(Frame(new float[48], Matrix4x4.Identity)) == null);
    }
}
