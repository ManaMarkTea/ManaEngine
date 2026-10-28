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
    }

    private sealed record Hung(ManaClothComponent Cloth, ManaSceneNode Node, MeshRendererComponent? Renderer, Vector3 Origin);

    // A 2 x 1.25 m flag held along its left edge, run for three seconds in a steady wind along +X.
    private static Hung Hang(float windSpeed)
    {
        var world = new ManaWorld();
        world.IrradianceSettings.WindSpeed = windSpeed;
        world.IrradianceSettings.WindDirection = 90.0f;
        world.IrradianceSettings.Gustiness = 0.0f;
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var origin = new Vector3(40.0f, 6.0f, 40.0f);
        var node = new ManaSceneNode(context, Matrix4x4.CreateTranslation(origin));
        var cloth = new ManaClothComponent(node) { Size = new Vector2(2.0f, 1.25f), Columns = 20, Rows = 13 };
        node.Components.Add(cloth);
        for (var frame = 0; frame < 90; frame++)
        {
            cloth.Advance(1.0f / 30.0f);
        }
        return new Hung(cloth, node, node.Components.OfType<MeshRendererComponent>().FirstOrDefault(), origin);
    }
}
