using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// The Glowing Coin: a Coin variant that adds nothing but a Glow. Placed, its meshes draw with the glow's
/// emission (and a plain coin's do not), it brings a light of its own, and it saves as one added script.
/// </summary>
public static class GlowChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        var glowingPath = Path.Combine(ManaContentLibrary.Root, "Prefabs", "Glowing Coin.manaprefab");
        var coinPath = Path.Combine(ManaContentLibrary.Root, "Prefabs", "Coin.manaprefab");
        if (!File.Exists(glowingPath))
        {
            check("the Glowing Coin prefab ships", false, glowingPath);
            return;
        }

        var glowing = ManaPrefab.Load(glowingPath);
        var coin = ManaPrefab.Load(coinPath);
        check("the Glowing Coin is a variant of the Coin", glowing.IsVariant && glowing.Base?.AssetId == coin.Id);
        check("that changes one thing: it adds a Glow", glowing.Base?.Overrides.Count == 1
            && ManaPrefab.Walk(glowing.Root).SelectMany(n => n.Scripts ?? []).Any(s => s.Type == "Glow"));

        ManaLevelNode Place(ManaPrefab asset, string name, float x) => new()
        {
            Id = Guid.NewGuid().ToString("N"), Name = name, Kind = ManaLevelSourceKind.Prefab,
            Position = [x, 0, 0], Rotation = [0, 0, 0], Scale = [1, 1, 1],
            Prefab = new ManaPrefabReference { AssetId = asset.Id, AssetPath = ManaContentLibrary.Reference(asset == glowing ? glowingPath : coinPath) }
        };
        var result = ManaLevelLoader.Load(new ManaLevel { Nodes = [Place(glowing, "Glowing", 0), Place(coin, "Plain", 3)] },
            context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("both place without a warning", result.Warnings.Count == 0, string.Join("; ", result.Warnings));

        var glowingNode = result.Nodes.First(n => n.Name == "Glowing");
        var glow = ManaPrefabRuntime.Walk(glowingNode).SelectMany(n => n.Components).OfType<ManaGlowComponent>().FirstOrDefault();
        check("the placed Glowing Coin carries its Glow", glow != null && glow.Strength > 1.0f, $"{glow?.Strength}");

        // What the renderer is handed: the glowing coin's meshes emit in the glow's colour, the plain one's do not.
        var lights = context.World.CollectPlacedLights();
        var meshes = new List<ManaRenderMeshInstance>();
        glowingNode.CollectRenderMeshInstances(meshes);
        var plain = new List<ManaRenderMeshInstance>();
        result.Nodes.First(n => n.Name == "Plain").CollectRenderMeshInstances(plain);
        check("its meshes draw with the glow's emission", meshes.Count > 0 && meshes.All(m => m.EmissionStrength > 0.5f && m.Emission.X > 0.9f),
            $"{meshes.Count} meshes, {meshes.FirstOrDefault()?.EmissionStrength}");
        check("a plain coin beside it does not glow", plain.Count > 0 && plain.All(m => m.EmissionStrength < 0.01f), $"{plain.FirstOrDefault()?.EmissionStrength}");
        check("and it brings a light of its own, in its colour", lights.Count == 1 && lights[0].Color.X > lights[0].Color.Z, $"{lights.Count} lights");

        var saved = ManaLevelCapture.Capture(context.World, "Glow");
        var savedGlowing = saved.Nodes.First(n => n.Name == "Glowing");
        check("saved, the instance is still just the variant, with no changes of its own", savedGlowing.Prefab?.Overrides.Count == 0,
            $"{savedGlowing.Prefab?.Overrides.Count}");
    }
}
