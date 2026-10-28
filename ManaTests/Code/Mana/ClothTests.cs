using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

public static class ClothChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var still = Hang(windSpeed: 0.0f);
        var points = still.Cloth.Points;
        var corner = points[^1];
        Check("the pole edge stays where it is held", Vector3.Distance(points[0], still.Origin) < 1e-4f, $"{points[0]}");
        Check("in still air the free corner hangs below the pole top", corner.Y < still.Origin.Y - 1.0f, $"{corner}");
        Check("in still air it hangs against the pole, not out from it", corner.X - still.Origin.X < 1.0f, $"{corner}");
        var across = Vector3.Distance(points[0], points[1]);
        var rest = 2.0f / 19.0f;
        Check("its weave barely stretches", MathF.Abs(across - rest) / rest < 0.15f, $"{across} vs {rest}");

        var blown = Hang(windSpeed: 10.0f);
        var outward = blown.Cloth.Points.Average(point => point.X) - blown.Origin.X;
        var stillOutward = still.Cloth.Points.Average(point => point.X) - still.Origin.X;
        Check("a wind along +X streams the flag out from its pole", outward > 0.6f && outward > stillOutward + 0.3f,
            $"{outward:0.00} vs {stillOutward:0.00} still");

        var mesh = blown.Renderer?.Mesh.CpuData;
        Check("the mesh is in the node's own frame", mesh != null && mesh.Positions[0].Length() < 1e-3f, $"{mesh?.Positions[0]}");
        var instances = new List<ManaRenderMeshInstance>();
        blown.Node.CollectRenderMeshInstances(instances);
        Check("cloth is lit from both sides", instances.Count == 1 && instances[0].TwoSidedLighting);

        // A cape: hung by its top edge from a body's node, a wind blowing it forward into the body.
        var caped = Hang(windSpeed: 8.0f, direction: 180.0f, setUp: (cloth, node) =>
        {
            var body = new ManaSceneNode(node.Context, Matrix4x4.CreateTranslation(40.0f, 0.0f, 40.0f));
            cloth.Size = new Vector2(0.6f, 0.9f);
            cloth.Columns = 10;
            cloth.Rows = 12;
            cloth.Pinned = ManaClothPin.Top;
            cloth.Wrap = 100.0f;
            cloth.HangFrom(body, Matrix4x4.CreateTranslation(-0.3f, 1.5f, -0.3f));
            cloth.CollideWith(body, new Vector3(0.0f, 0.4f, 0.0f), new Vector3(0.0f, 1.5f, 0.0f), 0.25f);
        });
        var axis = new Vector2(40.0f, 40.0f);
        var inside = caped.Cloth.Points.Count(point => point.Y > 0.4f && point.Y < 1.5f
            && Vector2.Distance(new Vector2(point.X, point.Z), axis) < 0.24f);
        Check("a cape blown into its wearer stays outside the body", inside == 0, $"{inside} points inside");
        var top = caped.Cloth.Points[0];
        Check("its top edge follows the node it hangs from, not its own", MathF.Abs(top.Y - 1.5f) < 0.01f, $"{top}");

        // A character setting off at walking pace (7 m/s, as the pawn walks) and turning a corner: the cape swings
        // back but stays below its shoulders, rather than flipping up over the head.
        var (highest, trailing) = SetOff();
        Check("a cape on a character setting off at a walk and turning never lifts above its neck", highest < -0.15f, $"hem peaked {highest:0.00} m from the neck");
        Check("and once walking it trails behind", trailing > 0.15f, $"hem {trailing:0.00} m behind");
    }

    // A cape on a 2 m character who stands for a second, sets off along +X at 7 m/s at once, as the pawn does, then
    // turns a corner. Returns the hem's highest above the neck, and how far it trailed before the turn.
    private static (float Highest, float Trailing) SetOff()
    {
        var world = new ManaWorld();
        world.IrradianceSettings.WindSpeed = 0.0f;
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var body = new ManaSceneNode(context, Matrix4x4.Identity) { Position = new Vector3(40.0f, 0.0f, 40.0f) };
        body.UpdateTransform();
        var node = new ManaSceneNode(context, Matrix4x4.Identity);
        var cape = new ManaClothComponent(node);
        ManaCape.Configure(cape, width: 0.84f, length: 0.74f, bodyDistance: 0.2f);
        // Facing +X: the cape across Z behind the body, curling forward.
        cape.HangFrom(body, Matrix4x4.CreateTranslation(-0.42f, 0.0f, -0.2f) * Matrix4x4.CreateRotationY(MathF.PI / 2.0f)
            * Matrix4x4.CreateTranslation(0.0f, 0.9f, 0.0f));
        cape.CollideWith(body, new Vector3(0.0f, 0.3f, 0.0f), new Vector3(0.0f, 0.9f, 0.0f), 0.18f);
        node.Components.Add(cape);

        var position = new Vector3(40.0f, 0.0f, 40.0f);
        var yaw = 0.0f;
        var speed = 0.0f;
        var highest = float.MinValue;
        var hem = 0.0f;
        for (var frame = 0; frame < 240; frame++)
        {
            var seconds = frame / 60.0f;
            if (seconds > 1.0f)
            {
                speed = 7.0f;
            }
            // A quarter turn at the pawn's 540 degrees a second, a second and a half after setting off.
            if (frame >= 150 && frame < 160)
            {
                yaw += MathF.PI / 20.0f;
            }
            position += Vector3.Transform(Vector3.UnitX, Matrix4x4.CreateRotationY(yaw)) * (speed / 60.0f);
            body.Position = position;
            body.Rotation = new Vector3(0.0f, yaw, 0.0f);
            body.UpdateTransform();
            cape.Advance(1.0f / 60.0f);
            if (seconds > 1.0f)
            {
                highest = MathF.Max(highest, cape.Points.Skip(cape.Points.Count - 12).Max(point => point.Y) - 0.9f);
            }
            if (frame == 149)
            {
                hem = position.X - cape.Points.Skip(cape.Points.Count - 12).Average(point => point.X);
            }
        }
        return (highest, hem);
    }

    private sealed record Hung(ManaClothComponent Cloth, ManaSceneNode Node, MeshRendererComponent? Renderer, Vector3 Origin);

    // A 2 x 1.25 m flag held along its left edge, run for three seconds in a steady wind along +X.
    private static Hung Hang(float windSpeed, float direction = 90.0f, Action<ManaClothComponent, ManaSceneNode>? setUp = null)
    {
        var world = new ManaWorld();
        world.IrradianceSettings.WindSpeed = windSpeed;
        world.IrradianceSettings.WindDirection = direction;
        world.IrradianceSettings.Gustiness = 0.0f;
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var origin = new Vector3(40.0f, 6.0f, 40.0f);
        var node = new ManaSceneNode(context, Matrix4x4.CreateTranslation(origin));
        var cloth = new ManaClothComponent(node) { Size = new Vector2(2.0f, 1.25f), Columns = 20, Rows = 13 };
        setUp?.Invoke(cloth, node);
        node.Components.Add(cloth);
        for (var frame = 0; frame < 90; frame++)
        {
            cloth.Advance(1.0f / 30.0f);
        }
        return new Hung(cloth, node, node.Components.OfType<MeshRendererComponent>().FirstOrDefault(), origin);
    }
}
