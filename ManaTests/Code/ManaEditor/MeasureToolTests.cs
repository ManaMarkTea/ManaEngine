using System.Numerics;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaEditor.Code.Measure;

namespace ManaTests;

/// <summary>
/// The editor's measuring tool without a window: picking the triangle under a ray, snapping to its corners, edge
/// middles, edges and face by screen distance, dropping points onto the first surface, the click flow of each
/// mode, and the figures it shows.
/// </summary>
public static class MeasureToolChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- snapping
        // A floor triangle seen straight from above: a metre is 100 pixels, x right and z down the screen.
        Vector2? TopDown(Vector3 world) => new Vector2(world.X * 100, world.Z * 100);
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, 0, 0);
        var c = new Vector3(0, 0, 1);
        MeasureSnap At(Vector3 hit, MeasureSnapTargets targets = MeasureSnapTargets.All) =>
            MeasureSnapping.FromTriangle(hit, Vector3.UnitY, a, b, c, TopDown(hit)!.Value, TopDown, 14, targets);

        var corner = At(new Vector3(0.05f, 0, 0.05f));
        Check("near a corner snaps to it", corner.Kind == MeasureSnapKind.Vertex && corner.Position == a, $"{corner.Kind} {corner.Position}");
        var middle = At(new Vector3(0.52f, 0, 0.04f));
        Check("near an edge's middle snaps to the middle", middle.Kind == MeasureSnapKind.Midpoint && middle.Position == new Vector3(0.5f, 0, 0),
            $"{middle.Kind} {middle.Position}");
        var edge = At(new Vector3(0.3f, 0, 0.05f));
        Check("near an edge elsewhere snaps onto the edge", edge.Kind == MeasureSnapKind.Edge
            && Vector3.Distance(edge.Position, new Vector3(0.3f, 0, 0)) < 1e-5f, $"{edge.Kind} {edge.Position}");
        var face = At(new Vector3(0.3f, 0, 0.3f));
        Check("in the middle of a face it stays where it is", face.Kind == MeasureSnapKind.Face && face.Position == new Vector3(0.3f, 0, 0.3f),
            $"{face.Kind} {face.Position}");
        var noCorners = At(new Vector3(0.05f, 0, 0.05f), MeasureSnapTargets.All & ~MeasureSnapTargets.Vertex);
        Check("with corners off, a corner is not snapped to", noCorners.Kind != MeasureSnapKind.Vertex, noCorners.Kind.ToString());

        // b-c is the long side; marked as a diagonal across a flat face, it is not snapped to.
        var nearDiagonal = new Vector3(0.5f, 0, 0.52f);
        var withDiagonal = MeasureSnapping.FromTriangle(nearDiagonal, Vector3.UnitY, a, b, c, TopDown(nearDiagonal)!.Value, TopDown, 14, MeasureSnapTargets.All);
        var withoutDiagonal = MeasureSnapping.FromTriangle(nearDiagonal, Vector3.UnitY, a, b, c, TopDown(nearDiagonal)!.Value, TopDown, 14,
            MeasureSnapTargets.All, edges: 1 | 4);
        Check("a real edge's middle is snapped to", withDiagonal.Kind == MeasureSnapKind.Midpoint, withDiagonal.Kind.ToString());
        Check("a flat face's diagonal is not", withoutDiagonal.Kind == MeasureSnapKind.Face, withoutDiagonal.Kind.ToString());

        var ground = MeasureSnapping.OnGround(new Vector3(0.23f, 5, 0.38f), -Vector3.UnitY, 0.25f);
        Check("over nothing, the ground on the snap grid", ground is { Kind: MeasureSnapKind.Grid } g && g.Position == new Vector3(0.25f, 0, 0.5f),
            ground?.Position.ToString() ?? "none");
        Check("the ground is never behind the camera", MeasureSnapping.OnGround(new Vector3(0, 5, 0), Vector3.UnitY, 0) == null);

        // ---------------------------------------------------------------- along the floor
        var dropped = MeasureSnapping.OntoPlane(new Vector3(2, 1.2f, 3), new Vector3(0, 0, 0), Vector3.UnitY);
        Check("a point on a wall drops to the floor below it", dropped == new Vector3(2, 0, 3), dropped.ToString());

        var tool = new MeasureTool { Active = true, StayOnFirstSurface = true };
        Check("no plane until there is a first point", tool.LockPlane(false) == null);
        tool.Click(new MeasureSnap(new Vector3(0, 0.02f, 0), Vector3.UnitY, MeasureSnapKind.Face));
        Check("then the first point's surface", tool.LockPlane(false) is { } plane && plane.Normal == Vector3.UnitY);
        tool.StayOnFirstSurface = false;
        Check("only while asked for", tool.LockPlane(false) == null && tool.LockPlane(shiftHeld: true) != null);
        tool.Cancel();

        // ---------------------------------------------------------------- modes
        tool.Mode = MeasureMode.Distance;
        tool.Click(new MeasureSnap(new Vector3(0, 0, 0), Vector3.UnitY, MeasureSnapKind.Vertex));
        tool.Click(new MeasureSnap(new Vector3(3, 4, 0), Vector3.UnitY, MeasureSnapKind.Vertex));
        var distance = tool.Measurements.LastOrDefault();
        Check("a distance is done after two clicks", distance != null && tool.Current == null && MathF.Abs(distance.Length - 5) < 1e-5f,
            distance?.Length.ToString() ?? "none");
        Check("and splits into across and up", distance != null && MathF.Abs(distance.Horizontal - 3) < 1e-5f && MathF.Abs(distance.Vertical - 4) < 1e-5f);

        tool.Mode = MeasureMode.Radius;
        tool.Click(new MeasureSnap(new Vector3(1, 0, 1), Vector3.UnitY, MeasureSnapKind.Face));
        Check("a radius keeps its rim in the centre's surface", tool.LockPlane(false) != null);
        tool.Click(new MeasureSnap(new Vector3(1, 0, 3), Vector3.UnitY, MeasureSnapKind.Face));
        var radius = tool.Measurements.LastOrDefault();
        Check("and measures the radius", radius is { Mode: MeasureMode.Radius } && MathF.Abs(radius.Length - 2) < 1e-5f,
            radius?.Length.ToString() ?? "none");
        Check("with its diameter and area", radius != null && tool.Details(radius).Contains("4.000 m") && tool.Details(radius).Contains("12.566"),
            radius == null ? "none" : tool.Details(radius));

        tool.Mode = MeasureMode.Path;
        foreach (var p in new[] { new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 1) })
        {
            tool.Click(new MeasureSnap(p, Vector3.UnitY, MeasureSnapKind.Face));
        }
        Check("a path goes on taking points", tool.Current?.Points.Count == 3 && tool.Measurements.Count == 2);
        tool.Click(new MeasureSnap(new Vector3(2, 0, 1), Vector3.UnitY, MeasureSnapKind.Face));
        var path = tool.Measurements.LastOrDefault();
        Check("until a double-click finishes it, all legs added", tool.Current == null && path is { Mode: MeasureMode.Path }
            && MathF.Abs(path.Length - 3) < 1e-5f, path?.Length.ToString() ?? "none");

        tool.Mode = MeasureMode.Distance;
        tool.Click(new MeasureSnap(Vector3.Zero, Vector3.UnitY, MeasureSnapKind.Face));
        tool.Mode = MeasureMode.Radius;
        Check("changing mode drops a half-taken measurement", tool.Current == null);
        tool.Remove(tool.Measurements[0]);
        Check("one can be removed", tool.Measurements.Count == 2);
        tool.Active = false;
        Check("putting the tool away keeps what was measured", tool.Measurements.Count == 2 && tool.Hover == null);
        tool.Clear();
        Check("Clear all clears them", tool.Measurements.Count == 0);

        // ---------------------------------------------------------------- rays
        // A room 2.5 m high with a wall at x = 3: the host's ray test, as the editor's picker would answer it.
        Vector3? Room(Vector3 origin, Vector3 direction)
        {
            var hits = new List<Vector3>();
            if (direction.Y > 1e-6f) hits.Add(origin + direction * ((2.5f - origin.Y) / direction.Y));
            if (direction.Y < -1e-6f) hits.Add(origin + direction * (-origin.Y / direction.Y));
            if (direction.X > 1e-6f) hits.Add(origin + direction * ((3f - origin.X) / direction.X));
            return hits.Count == 0 ? null : hits.MinBy(h => Vector3.Distance(h, origin));
        }
        var rays = new MeasureTool { Active = true, Mode = MeasureMode.Ray, CastRay = Room };
        var onFloor = new MeasureSnap(new Vector3(1, 0, 1), Vector3.UnitY, MeasureSnapKind.Face);
        rays.Point(onFloor, false, click: false);
        Check("hovering shows where the ray would land", rays.RayPreview is { To: { } landing } && MathF.Abs(landing.Y - 2.5f) < 1e-3f,
            rays.RayPreview?.To?.ToString() ?? "none");
        Check("without keeping it", rays.Measurements.Count == 0);
        rays.Point(onFloor, false, click: true);
        Check("out of a floor is up: the ceiling height", rays.Measurements.LastOrDefault() is { Mode: MeasureMode.Ray } up
            && MathF.Abs(up.Length - 2.5f) < 1e-3f && rays.Details(up) == "straight up",
            rays.Measurements.LastOrDefault() is { } u ? $"{u.Length} {rays.Details(u)}" : "none");
        rays.Point(new MeasureSnap(new Vector3(0, 1.2f, 1), Vector3.UnitX, MeasureSnapKind.Face), false, click: true);
        Check("out of a wall goes across to the one opposite", MathF.Abs(rays.Measurements[^1].Length - 3f) < 1e-3f,
            rays.Measurements[^1].Length.ToString());
        rays.RayDirection = MeasureRayDirection.Down;
        rays.Point(new MeasureSnap(new Vector3(1, 0.9f, 1), Vector3.UnitX, MeasureSnapKind.Face), false, click: true);
        Check("Down goes down wherever the click", MathF.Abs(rays.Measurements[^1].Length - 0.9f) < 1e-3f,
            rays.Measurements[^1].Length.ToString());
        rays.RayDirection = MeasureRayDirection.Out;
        var kept = rays.Measurements.Count;
        rays.Point(new MeasureSnap(new Vector3(1, 1, 1), -Vector3.UnitX, MeasureSnapKind.Face), false, click: true);
        Check("a ray that meets nothing keeps nothing, and says so", rays.Measurements.Count == kept && rays.RayPreview is { To: null });

        // ---------------------------------------------------------------- units
        Check("metres from a metre up", MeasureTool.Format(1.23456f, MeasureUnits.Metric) == "1.235 m");
        Check("centimetres below", MeasureTool.Format(0.4567f, MeasureUnits.Metric) == "45.7 cm");
        Check("no minus zero", MeasureTool.Format(-0.00001f, MeasureUnits.Metric) == "0.0 cm");
        Check("feet and inches", MeasureTool.Format(1.6f, MeasureUnits.Imperial) == "5' 3.0\"", MeasureTool.Format(1.6f, MeasureUnits.Imperial));

        // ---------------------------------------------------------------- picking
        // A 2 m cube at x = 10: a ray down onto its top meets the top face, and the triangle comes back in world space.
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var cube = new ManaSceneNode(context, Matrix4x4.CreateTranslation(10, 0, 0)) { Name = "Measured Cube" };
        cube.Components.Add(new MeshRendererComponent(cube, new ManaCube(context, Vector4.One, new Vector3(2, 2, 2))));
        var scene = new ManaScene(context) { Name = "Measured" };
        scene.Children.Add(cube);
        var scenes = new[] { scene };
        var hit = ManaScenePicker.PickTriangle(scenes, new Vector3(10.3f, 10, 0.2f), -Vector3.UnitY);
        Check("the picker finds the triangle under a ray", hit is { } h && h.Node == cube && MathF.Abs(h.Position.Y - 1) < 1e-4f
            && h.Normal.Y > 0.99f, hit?.Position.ToString() ?? "missed");
        Check("with its corners on the cube's top", hit is { } t && new[] { t.A, t.B, t.C }.All(v => MathF.Abs(v.Y - 1) < 1e-4f
            && MathF.Abs(MathF.Abs(v.X - 10) - 1) < 1e-4f || MathF.Abs(v.Y - 1) < 1e-4f && MathF.Abs(MathF.Abs(v.Z) - 1) < 1e-4f),
            hit is { } d ? $"{d.A} {d.B} {d.C}" : "missed");
        var creases = hit?.CreaseEdges ?? 0;
        Check("the diagonal across the flat top is not an edge, its two sides are",
            System.Numerics.BitOperations.PopCount((uint)creases) == 2, $"edge bits {creases}");
        Check("and nothing where there is nothing",
            ManaScenePicker.PickTriangle(scenes, new Vector3(20, 10, 0), -Vector3.UnitY) == null);
    }
}
