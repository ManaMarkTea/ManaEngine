using System.Numerics;
using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaEditor.Code.Levels;
using ManaUI;
using TargetPractice;

namespace ManaTests;

/// <summary>
/// The sample game's rules without a window: which nodes are targets, what a shot knocks down, what stands in its way,
/// the pop and the reset, winning, and that the editor knows the mode and the bundled level has what the game needs.
/// </summary>
public static class TargetPracticeChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var scene = new ManaScene(context) { Name = "Range" };
        world.Scenes.Add(scene);

        // A 1 m cube standing in for each target, a group like the level's, 10 m down -Z from where the shots start.
        ManaSceneNode Box(string name, Vector3 at, Vector3 size, ManaSceneNode? into = null)
        {
            var node = new ManaSceneNode(context, Matrix4x4.CreateTranslation(at)) { Name = name };
            node.Components.Add(new MeshRendererComponent(node, new ManaCube(context, Vector4.One, size)));
            if (into == null)
            {
                scene.Children.Add(node);
            }
            else
            {
                into.AddChild(node);
            }
            node.UpdateTransform();
            return node;
        }
        ManaSceneNode Target(string name, Vector3 at)
        {
            var group = new ManaSceneNode(context, Matrix4x4.CreateTranslation(at)) { Name = name };
            scene.Children.Add(group);
            group.UpdateTransform();
            Box("Ring", Vector3.Zero, Vector3.One, group);
            return group;
        }

        var left = Target("Target 1", new Vector3(-3, 0, -10));
        var middle = Target("Target 2", new Vector3(0, 0, -10));
        var right = Target("Target 3", new Vector3(3, 0, -10));
        Box("Wall", new Vector3(0, 0, -12), new Vector3(20, 6, 1));
        Box("Target Marker Inside", new Vector3(0, 3, 0), Vector3.One, middle);

        var range = TargetRange.Find(world, "Target", 0.2f);
        Check("a target is a node whose name starts with the prefix", range.Total == 3, range.Total.ToString());
        range.Tick(2.0f);
        Check("the clock waits for the first shot", range.Time == 0.0f, range.Time.ToString());
        Check("a node under a target is part of it, not another", !range.Standing.Contains("Target Marker Inside"));
        Check("another prefix finds other nodes", TargetRange.Find(world, "Wall", 0.2f).Total == 1);

        // ---------------------------------------------------------------- shooting
        var from = Vector3.Zero;
        Check("a shot at a target knocks it down", range.Shoot(world, from, new Vector3(-3, 0, -10) / 10.440307f) && range.Down == 1);
        Check("and it is no longer standing", !range.Standing.Contains("Target 1") && range.NodeOf("Target 1") == null);
        Check("a second shot at it is nothing", !range.Shoot(world, from, new Vector3(-3, 0, -10) / 10.440307f) && range.Down == 1);
        Check("a shot at the wall is a miss", !range.Shoot(world, from, new Vector3(8, 0, -10) / 12.806249f) && range.Down == 1);
        Check("a shot at nothing is a miss", !range.Shoot(world, from, Vector3.UnitY) && range.Down == 1);

        var body = Box("Player", new Vector3(0, 0, -4), new Vector3(1, 2, 1));
        Check("something in the way takes the shot", !range.Shoot(world, from, -Vector3.UnitZ) && range.Down == 1);
        Check("unless it is the player's own body, shot through", range.Shoot(world, from, -Vector3.UnitZ, body) && range.Down == 2);

        // ---------------------------------------------------------------- pop and reset
        var homeScale = left.Scale;
        range.Tick(0.1f);
        Check("a hit target shrinks", left.Scale.X < homeScale.X && left.Scale.X > 0.0f, left.Scale.ToString());
        Check("while a standing one does not", right.Scale == homeScale);
        range.Tick(0.2f);
        var ring = left.Children[0].Components.OfType<MeshRendererComponent>().First();
        Check("then it goes out of sight", !ring.IsVisible);
        Check("and a shot where it was meets the wall, not it", !range.Shoot(world, from, new Vector3(-3, 0, -10) / 10.440307f) && range.Down == 2);

        Check("the clock runs while targets stand", range.Time > 0.29f && range.Time < 0.31f, range.Time.ToString());
        range.Reset();
        Check("start over: every target stands again", range.Down == 0 && range.Standing.Count() == 3);
        Check("at the size the level gave it, and visible", left.Scale == homeScale && ring.IsVisible);
        Check("and the clock is at nothing", range.Time == 0.0f);

        // ---------------------------------------------------------------- winning
        range.Shoot(world, from, new Vector3(-3, 0, -10) / 10.440307f);
        range.Shoot(world, from, new Vector3(3, 0, -10) / 10.440307f);
        Check("two down is not a win", !range.Won);
        range.Tick(0.5f);
        range.Shoot(world, from, Vector3.Normalize(new Vector3(0, 0, -10)), body);
        Check("the last one down is", range.Won && range.Down == 3);
        range.Tick(1.0f);
        var stopped = range.Time;
        range.Tick(1.0f);
        Check("the clock stops when you win", stopped > 0.4f && range.Time == stopped, $"{stopped} then {range.Time}");
        Check("and there is nothing left to hit", !range.Shoot(world, from, -Vector3.UnitZ, body));
        Check("a level with no targets is never won", !TargetRange.Find(world, "Nothing Called This", 0.2f).Won);

        // ---------------------------------------------------------------- the mode, and its level
        var definition = ManaLevelGameModes.Find(TargetPracticeMode.ModeType);
        Check("the editor lists the mode", definition != null && definition.Create() is TargetPracticeMode);
        var mode = (TargetPracticeMode)ManaLevelGameModes.Create(TargetPracticeMode.ModeType)!;
        mode.TargetPrefix = "Goal";
        mode.PopSeconds = 0.5f;
        var saved = ManaLevelGameModes.Capture(mode);
        var loaded = ManaLevelGameModes.Load(saved, new List<string>()) as TargetPracticeMode;
        Check("its settings save and load", loaded is { TargetPrefix: "Goal", PopSeconds: 0.5f });
        Check("it asks for the host's character", mode.WantsPlayerCharacter && !mode.ShowsPlayHud);

        var levelFile = FindLevel();
        Check("the sample level is there", levelFile != null);
        if (levelFile != null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(levelFile));
            var nodes = document.RootElement.GetProperty("nodes").EnumerateArray().ToList();
            Check("it has a Player Start", nodes.Any(n => n.GetProperty("kind").GetString() == "PlayerStart"));
            var targets = nodes.Where(n => n.GetProperty("name").GetString()!.StartsWith("Target ")).ToList();
            Check("and ten targets, each a group with its rings", targets.Count == 10
                && targets.All(t => t.GetProperty("kind").GetString() == "Group" && t.GetProperty("children").GetArrayLength() >= 3),
                targets.Count.ToString());
            Check("and runs the game", document.RootElement.GetProperty("gameModes").EnumerateArray()
                .Any(m => m.GetProperty("type").GetString() == TargetPracticeMode.ModeType));
        }
    }

    private static string? FindLevel()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var file = Path.Combine(folder.FullName, "Content", "Levels", "Target Practice.manalevel");
            if (File.Exists(file))
            {
                return file;
            }
        }
        return null;
    }
}
