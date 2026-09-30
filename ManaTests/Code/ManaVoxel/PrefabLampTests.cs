using System.Text.Json.Nodes;
using Mana;
using Mana.Code;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

namespace ManaTests;

/// <summary>
/// Prefabs put to the classic test: one white Lamp, placed down a row with some recoloured on the instance,
/// and a Blue Lamp variant that holds nothing but its colour. Then the base is edited, and the variant has
/// to follow it while keeping its own colour.
/// </summary>
public static class PrefabLampChecks
{
    private static readonly Vector4 White = new(1, 1, 1, 1);
    private static readonly Vector4 Red = new(1, 0.15f, 0.1f, 1);
    private static readonly Vector4 Green = new(0.2f, 1, 0.3f, 1);
    private static readonly Vector4 Blue = new(0.2f, 0.45f, 1, 1);

    private delegate void Checker(string what, bool ok, string detail = "");

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        RunLevelChecks(Check);
        RunOverrideMarkChecks(Check);
        RunLightRoundTrip(Check);
        RunVariantChecks(Check);
    }

    private static ManaContext CreateContext()
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        context.Physics = new ManaPhysics(context);
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        return context;
    }

    private static ManaLightSourceComponent? LightOf(ManaSceneNode node) =>
        ManaPrefabRuntime.Walk(node).SelectMany(n => n.Components).OfType<ManaLightSourceComponent>().FirstOrDefault();

    private static bool Near(Vector4 a, Vector4 b) => Vector4.Distance(a, b) < 1e-3f;

    /// <summary>The level as shipped: every lamp lit the colour it should be.</summary>
    private static void RunLevelChecks(Checker check)
    {
        var path = ManaLevelLibrary.ResolvePath("Prefab Lamps");
        if (!File.Exists(path))
        {
            check("the Prefab Lamps level ships", false, path);
            return;
        }

        var context = CreateContext();
        var result = ManaLevelLoader.Load(ManaLevel.Load(path), context, context.World.DefaultScene, replaceAuthoredNodes: false);
        check("the Prefab Lamps level loads without a warning", result.Warnings.Count == 0, string.Join("; ", result.Warnings));

        ManaLightSourceComponent? Lamp(string name) =>
            result.Nodes.FirstOrDefault(node => node.Name == name && node.PrefabLink != null) is { } instance ? LightOf(instance) : null;

        var white = Lamp("White Lamp");
        var red = Lamp("Red Lamp");
        var green = Lamp("Green Lamp");
        var blue = Lamp("Blue Lamp");
        var bright = Lamp("Bright Blue Lamp");
        check("a lamp placed as it is gives white light", white != null && Near(white.Color, White), $"{white?.Color}");
        check("and ones recoloured on the instance give red and green", red != null && Near(red.Color, Red) && green != null && Near(green.Color, Green),
            $"{red?.Color} {green?.Color}");
        check("the Blue Lamp variant gives blue", blue != null && Near(blue.Color, Blue) && Math.Abs(blue.Intensity - 1) < 1e-3f, $"{blue?.Color} x{blue?.Intensity}");
        check("and an instance of it turned up stays blue, only brighter",
            bright != null && Near(bright.Color, Blue) && Math.Abs(bright.Intensity - 3) < 1e-3f, $"{bright?.Color} x{bright?.Intensity}");
        check("every lamp is its own light, not one shared", new[] { white, red, green, blue, bright }.Distinct().Count() == 5);

        var variantFile = JsonNode.Parse(File.ReadAllText(ManaContentLibrary.ResolvePrefab(ManaPrefab.ReadId(Path.Combine(ManaContentLibrary.Root, "Prefabs", "Blue Lamp.manaprefab")), "Prefabs/Blue Lamp.manaprefab")))!;
        check("the Blue Lamp file holds its base and one change, and no tree of its own",
            variantFile["root"] == null && variantFile["base"]?["overrides"]?.AsArray().Count == 1, variantFile.ToJsonString());
    }

    /// <summary>
    /// The Inspector's override marks: a recoloured lamp's colour is marked and nothing else, its revert arrow
    /// puts white back, and a variant open for editing marks what it changes from its base.
    /// </summary>
    private static void RunOverrideMarkChecks(Checker check)
    {
        var context = CreateContext();
        var result = ManaLevelLoader.Load(ManaLevel.Load(ManaLevelLibrary.ResolvePath("Prefab Lamps")), context, context.World.DefaultScene, replaceAuthoredNodes: false);
        ManaSceneNode? Bulb(string lamp) => result.Nodes.FirstOrDefault(n => n.Name == lamp && n.PrefabLink != null) is { } instance
            ? ManaPrefabRuntime.Walk(instance).FirstOrDefault(n => n.Name == "Bulb")
            : null;

        var red = Bulb("Red Lamp");
        var white = Bulb("White Lamp");
        var redLight = red == null ? null : LightOf(red);
        var whiteLight = white == null ? null : LightOf(white);
        if (red == null || white == null || redLight == null || whiteLight == null)
        {
            check("the lamps have bulbs to mark", false);
            return;
        }

        check("a recoloured lamp's colour is marked as overridden", PrefabOverrideMarks.Find(red, redLight, "color") != null);
        check("and its brightness, left alone, is not", PrefabOverrideMarks.Find(red, redLight, "intensity") == null
            && PrefabOverrideMarks.Find(red, null, "position") == null);
        check("a lamp placed as it is has nothing marked", PrefabOverrideMarks.Find(white, whiteLight, "color") == null);

        PrefabOverrideMarks.Revert(red, PrefabOverrideMarks.Find(red, redLight, "color")!);
        var redInstance = context.World.DefaultScene.Children.SelectMany(ManaPrefabRuntime.Walk).FirstOrDefault(n => n.Name == "Red Lamp" && n.PrefabLink != null);
        // The revert replaces the instance, so it is found again rather than read from the load result.
        var reverted = redInstance == null ? null : LightOf(redInstance);
        check("its revert arrow puts the prefab's white back", reverted != null && Near(reverted.Color, White), $"{reverted?.Color}");
        check("and leaves it with no overrides", redInstance != null && ManaPrefabRuntime.CaptureReference(redInstance).Overrides.Count == 0);

        // The editor room, as far as the prefab stage cares: a floor it keeps and a wall it leaves behind.
        var floor = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Grid Plane XZ" };
        var wall = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Grid Plane YX" };
        context.World.DefaultScene.Children.Add(floor);
        context.World.DefaultScene.Children.Add(wall);

        // The variant, open for editing: its colour is its one change, and reverting it leaves the base's white.
        var model = ManaVoxel.Code.UI.ProjectAssetViewModel.For(context.World);
        model.OpenPrefab(Path.Combine(ManaContentLibrary.Root, "Prefabs", "Blue Lamp.manaprefab"));
        var shown = context.World.Scenes.SelectMany(s => s.Children).ToList();
        check("a prefab is edited on the floor alone, the room's walls left out", shown.Contains(floor) && !shown.Contains(wall));
        var editing = model.EditingScenes?.SelectMany(s => s.Children).SelectMany(ManaPrefabRuntime.Walk).FirstOrDefault(n => n.Name == "Bulb");
        var editingLight = editing == null ? null : LightOf(editing);
        check("an open variant marks what it changes from its base", editing != null && editingLight != null
            && PrefabOverrideMarks.Find(editing, editingLight, "color") != null && PrefabOverrideMarks.Find(editing, editingLight, "intensity") == null,
            model.Status);
        if (editing != null && editingLight != null && PrefabOverrideMarks.Find(editing, editingLight, "color") is { } change)
        {
            PrefabOverrideMarks.Revert(editing, change);
            var rebuilt = model.EditingScenes?.SelectMany(s => s.Children).SelectMany(ManaPrefabRuntime.Walk).FirstOrDefault(n => n.Name == "Bulb");
            var rebuiltLight = rebuilt == null ? null : LightOf(rebuilt);
            check("and reverting it gives the base's colour, with nothing left changed",
                rebuiltLight != null && Near(rebuiltLight.Color, White) && model.VariantOverrides().Count == 0, $"{rebuiltLight?.Color} {model.Status}");
        }

        model.DiscardEditing();
        check("discarding the edit returns to the level", !model.IsEditing && context.World.Scenes.Contains(context.World.DefaultScene));
        check("with the room as it was", context.World.Scenes.SelectMany(s => s.Children).Contains(wall));
    }

    /// <summary>A light saved in a level comes back the colour it was, not black.</summary>
    private static void RunLightRoundTrip(Checker check)
    {
        var context = CreateContext();
        var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Torch", LevelSource = ManaLevelSource.Group };
        node.Components.Add(new ManaLightSourceComponent(node) { Color = Red, Intensity = 2.5f });
        context.World.DefaultScene.Children.Add(node);

        var json = ManaLevelCapture.Capture(context.World, "Torch").ToJson();
        var reloaded = CreateContext();
        var result = ManaLevelLoader.Load(ManaLevel.FromJson(json), reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false);
        var light = result.Nodes.Select(LightOf).FirstOrDefault(found => found != null);
        check("a light is saved with its node, colour and all", light != null && Near(light.Color, Red) && Math.Abs(light.Intensity - 2.5f) < 1e-3f,
            $"{light?.Color} x{light?.Intensity}");
        check("with its colour written as [r, g, b, a]", json.Contains("\"color\": [1, 0.15, 0.1, 1]", StringComparison.Ordinal), "");
    }

    /// <summary>Variants against a copy of the lamps: made, saved as changes only, and following their base.</summary>
    private static void RunVariantChecks(Checker check)
    {
        var source = Path.Combine(ManaContentLibrary.Root, "Prefabs");
        var previous = Environment.CurrentDirectory;
        var scratch = Path.Combine(Path.GetTempPath(), "mana-lamp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scratch, "Content", "Prefabs"));
        foreach (var name in new[] { "Lamp.manaprefab", "Blue Lamp.manaprefab" })
        {
            File.Copy(Path.Combine(source, name), Path.Combine(scratch, "Content", "Prefabs", name));
        }

        try
        {
            Environment.CurrentDirectory = scratch;
            var lampPath = Path.Combine(scratch, "Content", "Prefabs", "Lamp.manaprefab");
            var bluePath = Path.Combine(scratch, "Content", "Prefabs", "Blue Lamp.manaprefab");
            check("the copies are the ones read", Path.GetFullPath(ManaContentLibrary.Root) == Path.GetFullPath(Path.Combine(scratch, "Content")), ManaContentLibrary.Root);

            // The base changes: the variant follows it, and keeps the one thing it changed.
            var lamp = ManaPrefab.Load(lampPath);
            LightScript(lamp.Root)!["intensity"] = System.Text.Json.JsonSerializer.SerializeToElement(2.0f);
            lamp.Save(lampPath);
            var blue = ManaPrefab.Load(bluePath);
            var blueLight = LightScript(blue.Root)!;
            check("a change to the base reaches its variant", blueLight["intensity"].GetSingle() == 2.0f, blueLight["intensity"].ToString());
            check("and the variant keeps its own colour", Same(blueLight["color"], Blue), blueLight["color"].GetRawText());
            check("with the base's exposed values, as it has none of its own", blue.IsVariant && blue.Properties.Count == lamp.Properties.Count);

            // A new variant, as Create Variant makes one: changed once, saved as that one change.
            var green = ManaPrefab.CreateVariant(lamp, lampPath, "Green Lamp");
            LightScript(green.Root)!["color"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { 0.2f, 1f, 0.3f, 1f });
            var greenPath = Path.Combine(scratch, "Content", "Prefabs", "Green Lamp.manaprefab");
            green.Save(greenPath);
            var greenFile = JsonNode.Parse(File.ReadAllText(greenPath))!;
            check("a new variant is saved as its base and its one change", greenFile["root"] == null
                && greenFile["base"]?["overrides"]?.AsArray().Count == 1 && greenFile["base"]?["assetId"]?.GetValue<string>() == lamp.Id,
                greenFile["base"]?.ToJsonString() ?? "");
            var reread = ManaPrefab.Load(greenPath);
            check("and reads back green, with the base's brightness", Same(LightScript(reread.Root)!["color"], Green)
                && LightScript(reread.Root)!["intensity"].GetSingle() == 2.0f);

            // A variant of a variant: the Blue Lamp made brighter still.
            var brighter = ManaPrefab.CreateVariant(blue, bluePath, "Bright Blue Lamp");
            LightScript(brighter.Root)!["intensity"] = System.Text.Json.JsonSerializer.SerializeToElement(5.0f);
            var brighterPath = Path.Combine(scratch, "Content", "Prefabs", "Bright Blue Lamp.manaprefab");
            brighter.Save(brighterPath);
            var brighterLight = LightScript(ManaPrefab.Load(brighterPath).Root)!;
            check("a variant of a variant keeps both changes", Same(brighterLight["color"], Blue) && brighterLight["intensity"].GetSingle() == 5.0f);

            // A variant made from itself is a loop, and is refused rather than hanging the loader.
            var loopFile = JsonNode.Parse(File.ReadAllText(bluePath))!;
            loopFile["base"]!["assetId"] = loopFile["id"]!.GetValue<string>();
            loopFile["base"]!["assetPath"] = "Prefabs/Loop.manaprefab";
            var loopPath = Path.Combine(scratch, "Content", "Prefabs", "Loop.manaprefab");
            loopFile["id"] = Guid.NewGuid().ToString("N");
            loopFile["base"]!["assetId"] = loopFile["id"]!.GetValue<string>();
            File.WriteAllText(loopPath, loopFile.ToJsonString());
            var refused = false;
            try
            {
                ManaPrefab.Load(loopPath);
            }
            catch (InvalidDataException)
            {
                refused = true;
            }

            check("a variant made from itself is refused", refused);
            File.Delete(loopPath);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool Same(System.Text.Json.JsonElement colour, Vector4 expected)
    {
        var values = colour.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        return values.Length == 4 && Near(new Vector4(values[0], values[1], values[2], values[3]), expected);
    }

    /// <summary>The Lamp's light script's properties, wherever it sits in a tree.</summary>
    private static Dictionary<string, System.Text.Json.JsonElement>? LightScript(ManaLevelNode root) =>
        ManaPrefab.Walk(root).SelectMany(node => node.Scripts ?? []).FirstOrDefault(script => script.Type == "Light")?.Properties;
}
