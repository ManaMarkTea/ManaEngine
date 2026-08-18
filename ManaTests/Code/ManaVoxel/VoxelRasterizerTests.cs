using ManaUI;
using ManaVoxel.Code.Tools;

namespace ManaTests;

/// <summary>
/// The Circle and Sphere rasterizers: rim vs interior classification, and hollow (no fill) behaviour.
/// <para>
/// Neither shape had any test coverage before the stroke/fill split -- this is the algorithm itself, no
/// window or view model needed, so it is checked directly against the cell dictionary the tools paint.
/// </para>
/// </summary>
public static class VoxelRasterizerChecks
{
    private static readonly Vector4 Stroke = new(1.0f, 0.0f, 0.0f, 1.0f);
    private static readonly Vector4 Fill = new(0.0f, 0.0f, 1.0f, 1.0f);

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- circle
        var center = new Vector3(0.5f, 0.5f, 0.5f);
        var edge = new Vector3(6.5f, 0.5f, 0.5f);
        var centerCell = VoxelRasterizer.ToCell(center);

        var filled = new Dictionary<(int X, int Y, int Z), Vector4>();
        VoxelRasterizer.AddFilledCircle(filled, center, edge, VoxelWorkPlane.XZ, Stroke, Fill);

        Check("a filled circle paints its centre", filled.ContainsKey(centerCell),
            $"{filled.Count} cells painted");
        Check("the centre is fill colour, not stroke", filled.TryGetValue(centerCell, out var c) && c == Fill,
            $"{c}");

        var farEdgeCell = VoxelRasterizer.ToCell(new Vector3(6.5f, 0.5f, 0.5f));
        Check("the far edge is stroke colour", filled.TryGetValue(farEdgeCell, out var e) && e == Stroke, $"{e}");

        var hollow = new Dictionary<(int X, int Y, int Z), Vector4>();
        VoxelRasterizer.AddFilledCircle(hollow, center, edge, VoxelWorkPlane.XZ, Stroke, null);

        Check("a hollow circle leaves its centre unpainted", !hollow.ContainsKey(centerCell),
            $"{hollow.Count} cells painted");
        Check("but still paints the rim", hollow.TryGetValue(farEdgeCell, out var h) && h == Stroke, $"{h}");
        Check("hollow paints strictly fewer cells than filled", hollow.Count < filled.Count,
            $"hollow {hollow.Count}, filled {filled.Count}");
        Check("and every hollow cell is stroke coloured", hollow.Values.All(v => v == Stroke),
            string.Join(", ", hollow.Values.Distinct()));

        // ---------------------------------------------------------------- sphere
        var sphereCenter = new Vector3(0.5f, 0.5f, 0.5f);
        var sphereEdge = new Vector3(5.5f, 0.5f, 0.5f);
        var sphereCenterCell = VoxelRasterizer.ToCell(sphereCenter);

        var filledSphere = new Dictionary<(int X, int Y, int Z), Vector4>();
        VoxelRasterizer.AddFilledSphere(filledSphere, sphereCenter, sphereEdge, Stroke, Fill);

        Check("a filled sphere paints its centre", filledSphere.ContainsKey(sphereCenterCell),
            $"{filledSphere.Count} cells painted");
        Check("the sphere's centre is fill coloured",
            filledSphere.TryGetValue(sphereCenterCell, out var sc) && sc == Fill, $"{sc}");

        var sphereSurfaceCell = VoxelRasterizer.ToCell(new Vector3(5.5f, 0.5f, 0.5f));
        Check("the sphere's surface is stroke coloured",
            filledSphere.TryGetValue(sphereSurfaceCell, out var se) && se == Stroke, $"{se}");

        var hollowSphere = new Dictionary<(int X, int Y, int Z), Vector4>();
        VoxelRasterizer.AddFilledSphere(hollowSphere, sphereCenter, sphereEdge, Stroke, null);

        Check("a hollow sphere leaves its centre empty -- a shell, not a ball",
            !hollowSphere.ContainsKey(sphereCenterCell), $"{hollowSphere.Count} cells painted");
        Check("but still paints its surface",
            hollowSphere.TryGetValue(sphereSurfaceCell, out var hs) && hs == Stroke, $"{hs}");
        Check("every hollow-sphere cell is stroke coloured", hollowSphere.Values.All(v => v == Stroke),
            string.Join(", ", hollowSphere.Values.Distinct()));

        // ---------------------------------------------------------------- work plane
        // A work plane used to only ever pick which two axes a flat shape (Circle/Text) spanned -- it never
        // actually constrained WHERE anything was drawn. TryIntersectWorkPlane is what makes it a real
        // construction plane: every tool's placement ray hits this instead of scene geometry once a plane
        // (other than Auto) and an offset are chosen.
        var hitAbove = VoxelRasterizer.TryIntersectWorkPlane(
            new Vector3(2.0f, 20.0f, 2.0f), new Vector3(0.0f, -1.0f, 0.0f), VoxelWorkPlane.XZ, 5.0f,
            out var xzPosition, out var xzNormal);
        Check("a straight-down ray finds the XZ plane at its offset", hitAbove);
        Check("landing at the offset height", MathF.Abs(xzPosition.Y - 5.0f) < 0.0001f, $"{xzPosition}");
        Check("with a normal facing back up at the camera", xzNormal == new Vector3(0.0f, 1.0f, 0.0f), $"{xzNormal}");

        var hitSide = VoxelRasterizer.TryIntersectWorkPlane(
            Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), VoxelWorkPlane.YZ, -3.0f,
            out var yzPosition, out _);
        Check("a ray along X finds the YZ plane at its (negative) offset", hitSide);
        Check("at the expected X", MathF.Abs(yzPosition.X - (-3.0f)) < 0.0001f, $"{yzPosition}");

        Check("a ray parallel to the plane never crosses it",
            !VoxelRasterizer.TryIntersectWorkPlane(
                Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), VoxelWorkPlane.XZ, 5.0f, out _, out _));

        Check("a plane behind the ray's origin does not count as a hit",
            !VoxelRasterizer.TryIntersectWorkPlane(
                new Vector3(0.0f, 10.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), VoxelWorkPlane.XZ, 5.0f, out _, out _));

        Check("Auto has no fixed plane to intersect",
            !VoxelRasterizer.TryIntersectWorkPlane(
                Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), VoxelWorkPlane.Auto, 0.0f, out _, out _));
    }
}
