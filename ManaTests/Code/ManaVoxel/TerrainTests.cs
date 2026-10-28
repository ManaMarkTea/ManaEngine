using System.Numerics;
using Mana;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code;
using Mana.Code.Components;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// <see cref="ManaTerrainNode"/>: heights between samples, brushes that rebuild only the patches they touch, undo,
/// the level round trip, a pointer ray finding the ground, and a character standing on a slope.
/// </summary>
public static class TerrainChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        RunShapeChecks(check);
        RunBrushChecks(check);
        RunRoundTripChecks(check);
        RunSlopeChecks(check);
    }

    private static ManaContext CreateContext(bool physics)
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        if (physics)
        {
            context.Physics = new ManaPhysics(context);
        }

        return context;
    }

    private static ManaTerrainNode AddTerrain(ManaContext context, int samples, float spacing)
    {
        var terrain = new ManaTerrainNode(context, samples, samples, spacing);
        context.World.DefaultScene.Children.Add(terrain);
        terrain.UpdateTransform();
        return terrain;
    }

    private static void RunShapeChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: false);
        var terrain = AddTerrain(context, 129, 1.0f);
        check("129 samples make 4 x 4 patches of 32 cells", terrain.PatchesX == 4 && terrain.PatchesZ == 4 && terrain.PatchNodes.Count() == 16,
            $"{terrain.PatchesX} x {terrain.PatchesZ}");
        check("centred on the node", MathF.Abs(terrain.Width - 128.0f) < 1e-4f, $"{terrain.Width}");

        // A plane tilted along x: the heights between samples follow it exactly.
        terrain.SetHeights((x, z) => x * 0.25f);
        check("heights between samples lie on the plane", MathF.Abs(terrain.HeightAt(10.3f, -7.7f) - 10.3f * 0.25f) < 1e-3f,
            $"{terrain.HeightAt(10.3f, -7.7f)}");
        var patch = terrain.PatchNodes.First().Mesh.CpuData!;
        var normal = patch.Normals[patch.Normals.Length / 2];
        var expected = Vector3.Normalize(new Vector3(-0.25f, 1.0f, 0.0f));
        check("normals lean away from the slope", Vector3.Distance(normal, expected) < 1e-3f, $"{normal}");
        var a = patch.Positions[patch.Indexes[0]];
        var b = patch.Positions[patch.Indexes[1]];
        var c = patch.Positions[patch.Indexes[2]];
        check("triangles face up (counter-clockwise from above)", Vector3.Cross(b - a, c - a).Y > 0.0f, "");

        var hit = terrain.Raycast(new Vector3(5.0f, 50.0f, 3.0f), new Vector3(0.0f, -1.0f, 0.0f));
        check("a ray straight down finds the ground", hit is { } h && MathF.Abs(h.Y - 1.25f) < 1e-2f, $"{hit}");
        var slanted = terrain.Raycast(new Vector3(-40.0f, 30.0f, 0.0f), Vector3.Normalize(new Vector3(1.0f, -1.0f, 0.2f)));
        check("and a slanted one lands on the surface", slanted is { } s && MathF.Abs(s.Y - terrain.HeightAt(s.X, s.Z)) < 1e-2f, $"{slanted}");
        check("a ray pointing away misses", terrain.Raycast(new Vector3(0.0f, 50.0f, 0.0f), Vector3.UnitY) == null, "");
    }

    private static void RunBrushChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: false);
        var terrain = AddTerrain(context, 129, 1.0f);
        var before = Enumerable.Range(0, 16).Select(i => terrain.PatchBuildCount(i % 4, i / 4)).ToArray();
        var state = terrain.CaptureState();

        // In the middle of the first patch (local -48, -48), well inside it.
        var rebuilt = terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Raise, -48.0f, -48.0f, 4.0f, 1.0f));
        var after = Enumerable.Range(0, 16).Select(i => terrain.PatchBuildCount(i % 4, i / 4)).ToArray();
        check("a small brush rebuilds only its own patch", rebuilt == 1 && after[0] == before[0] + 1 && after.Skip(1).SequenceEqual(before.Skip(1)),
            $"{rebuilt} rebuilt");
        check("and raises the ground under it", terrain.HeightAt(-48.0f, -48.0f) > 0.1f, $"{terrain.HeightAt(-48.0f, -48.0f)}");
        check("but nothing beyond its radius", terrain.HeightAt(-40.0f, -48.0f) == 0.0f, "");

        // On the corner where four patches meet, all four are rebuilt.
        rebuilt = terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Raise, -32.0f, -32.0f, 2.0f, 1.0f));
        check("a brush on a patch corner rebuilds the four that share it", rebuilt == 4, $"{rebuilt}");

        terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Flatten, -48.0f, -48.0f, 6.0f, 1.0f, 0.0f, FlattenHeight: 2.0f));
        check("flatten levels to the height asked for", MathF.Abs(terrain.HeightAt(-48.0f, -48.0f) - 2.0f) < 1e-3f, $"{terrain.HeightAt(-48.0f, -48.0f)}");

        for (var i = 0; i < 20; i++)
        {
            terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Paint, 0.0f, 0.0f, 3.0f, 1.0f, 0.0f, Layer: 2));
        }
        var centre = 64 * 129 + 64;
        var splat = terrain.Splat.Slice(centre * 4, 4).ToArray();
        check("paint moves the weights to its layer, still adding to 255", splat[2] > 250 && splat.Sum(w => w) == 255, string.Join(",", splat));
        var colour = terrain.PatchNodes.ElementAt(10).Mesh.CpuData!.Colors[0];
        check("and the vertex colours follow", Vector4.Distance(colour, ManaTerrainNode.DefaultLayerColors[2]) < 0.05f, $"{colour}");

        // A second of smoothing at the editor's rate (strength 0.5 over 60 frames) visibly flattens a round hill.
        terrain.SetHeights((x, z) => 4.0f * MathF.Exp(-(x * x + z * z) / 18.0f));
        var peak = terrain.HeightAt(0.0f, 0.0f);
        for (var frame = 0; frame < 60; frame++)
        {
            terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Smooth, 0.0f, 0.0f, 6.0f, 0.5f / 60.0f * 6.0f, 0.6f));
        }
        var smoothed = terrain.HeightAt(0.0f, 0.0f);
        check("a second of smoothing brings a round hill well down", smoothed < peak * 0.75f, $"{peak:0.00} -> {smoothed:0.00}");
        check("and leaves the flat ground away from it alone", terrain.HeightAt(40.0f, 40.0f) < 1e-3f, $"{terrain.HeightAt(40.0f, 40.0f)}");
        terrain.SetHeights((x, z) => 0.0f);

        terrain.RestoreState(state);
        check("restoring the state puts the ground back", terrain.HeightAt(-48.0f, -48.0f) == 0.0f && terrain.Splat[centre * 4] == 255, "");

        terrain.SetHeights((x, z) => x * 0.1f);
        terrain.Resize(65, 65, 2.0f);
        check("resizing keeps the size in samples and metres asked for", terrain.SamplesX == 65 && terrain.Width == 128.0f && terrain.PatchesX == 2, "");
        check("and the shape, resampled", MathF.Abs(terrain.HeightAt(30.0f, 0.0f) - 3.0f) < 1e-2f, $"{terrain.HeightAt(30.0f, 0.0f)}");
    }

    private static void RunRoundTripChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: false);
        var terrain = AddTerrain(context, 65, 0.5f);
        terrain.Position = new Vector3(40.0f, 0.0f, 40.0f);
        terrain.SetHeights((x, z) => MathF.Sin(x * 0.3f) * 2.0f + z * 0.05f);
        terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Paint, 3.0f, 3.0f, 4.0f, 1.0f, 0.0f, Layer: 1));
        terrain.LayerColors[3] = new Vector4(0.9f, 0.1f, 0.1f, 1.0f);
        terrain.UpdateTransform();

        var json = ManaLevelCapture.Capture(context.World, "Terrain").ToJson();
        var level = ManaLevel.FromJson(json);
        var entry = level.Nodes.SingleOrDefault(node => node.Kind == ManaLevelSourceKind.Terrain);
        check("a terrain is saved as a Terrain node", entry?.Terrain != null, "");
        check("its grid in a few kilobytes, not a list of numbers", json.Length < 40000, $"{json.Length} chars");

        var reloaded = CreateContext(physics: false);
        ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false);
        var loaded = reloaded.World.DefaultScene.Children.OfType<ManaTerrainNode>().SingleOrDefault();
        check("and loads back as a terrain", loaded != null, "");
        if (loaded == null)
        {
            return;
        }

        check("with its grid and spacing", loaded.SamplesX == 65 && loaded.SamplesZ == 65 && loaded.Spacing == 0.5f, "");
        check("every height exactly", loaded.Heights.SequenceEqual(terrain.Heights), "");
        check("its paint", loaded.Splat.SequenceEqual(terrain.Splat), "");
        check("its layer colours", loaded.LayerColors[3] == terrain.LayerColors[3], $"{loaded.LayerColors[3]}");
        check("and where it stands", loaded.Position == terrain.Position, $"{loaded.Position}");
        check("its patches are not saved as nodes of their own", entry!.Children == null, "");
        check("ground pictures, the default, are not written out", entry.Terrain!.Textured == null, "");
        check("and it loads textured", loaded.Textured, "");

        // Textured, the GPU gets the paint weights in place of the colours; probes still see the colours.
        var patch = loaded.PatchNodes.First();
        var data = patch.Mesh.CpuData!;
        check("textured patches carry their layer weights for the shader",
            patch.MeshRenderer.TerrainLayers && data.ShaderColors != null && data.ShaderColors.All(w => MathF.Abs(w.X + w.Y + w.Z + w.W - 1.0f) < 0.02f),
            $"{data.ShaderColors?[0]}");
        check("and their blended colours for everything else", data.Colors.Length == data.Positions.Length && data.Colors[0].X < 1.0f, $"{data.Colors[0]}");

        loaded.Textured = false;
        var flat = loaded.PatchNodes.First();
        check("flat colours when the pictures are off", !flat.MeshRenderer.TerrainLayers && flat.Mesh.CpuData!.ShaderColors == null, "");
        var flatJson = ManaLevelCapture.Capture(reloaded.World, "Terrain").ToJson();
        var flatEntry = ManaLevel.FromJson(flatJson).Nodes.Single(node => node.Kind == ManaLevelSourceKind.Terrain);
        check("which is saved", flatEntry.Terrain!.Textured == false, "");
    }

    private static void RunSlopeChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: true);
        var terrain = AddTerrain(context, 65, 0.5f);
        // A 15-degree ramp along x.
        var rise = MathF.Tan(15.0f * MathF.PI / 180.0f);
        terrain.SetHeights((x, z) => x * rise);

        var body = ManaCharacterBody.Create(context, new Vector3(4.0f, 4.0f * rise + 0.5f, 0.0f), 0.3f, 1.8f);
        var physics = context.Physics!;
        for (var step = 0; step < 90; step++)
        {
            physics.Simulation.Timestep(1.0f / 60.0f, physics.ThreadDispatcher);
            body.RefreshGroundState();
        }

        var foot = body.FootPosition;
        var ground = terrain.HeightAt(foot.X, foot.Z);
        check("a character dropped on a slope stands on it", body.IsGrounded && MathF.Abs(foot.Y - ground) < 0.15f,
            $"grounded {body.IsGrounded}, foot {foot.Y:0.00} over ground {ground:0.00}");

        // Raised under its feet, the ground's collider is rebuilt and lifts it.
        terrain.Apply(new ManaTerrainDab(ManaTerrainBrush.Flatten, foot.X, foot.Z, 3.0f, 1.0f, 0.0f, FlattenHeight: ground + 1.0f));
        for (var step = 0; step < 60; step++)
        {
            physics.Simulation.Timestep(1.0f / 60.0f, physics.ThreadDispatcher);
            body.RefreshGroundState();
        }
        check("ground sculpted under it carries it up", body.FootPosition.Y > ground + 0.8f, $"foot {body.FootPosition.Y:0.00}");
    }
}
