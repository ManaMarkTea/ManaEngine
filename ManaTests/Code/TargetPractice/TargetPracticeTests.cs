using System.Numerics;
using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Models;
using Mana.Code.Nodes;
using ManaCore.Audio;
using Mana.Code.Shapes;
using ManaEditor.Code.Levels;
using ManaUI;
using TargetPractice;

namespace ManaTests;

/// <summary>
/// The sample game's rules without a window: which nodes are targets, what a ball passing through knocks down, the pop
/// and the reset, winning, peas flying and landing in the physics world, and that the editor knows the mode and the bundled level has what the game needs.
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

        // ---------------------------------------------------------------- a ball passing
        var r = PeaShooter.Radius;
        Check("a ball through a target's face knocks it down", range.Sweep(new Vector3(-3, 0, -9), new Vector3(-3, 0, -11), r) == left && range.Down == 1);
        Check("and it is no longer standing", !range.Standing.Contains("Target 1") && range.NodeOf("Target 1") == null);
        Check("a second ball through it is nothing", range.Sweep(new Vector3(-3, 0, -9), new Vector3(-3, 0, -11), r) == null && range.Down == 1);
        Check("a ball that goes by, wide, is a miss", range.Sweep(new Vector3(-1.5f, 0, -9), new Vector3(-1.5f, 0, -11), r) == null && range.Down == 1);
        Check("so is one that stops short", range.Sweep(new Vector3(0, 0, -5), new Vector3(0, 0, -9), r) == null && range.Down == 1);
        Check("a fast one is caught between its two frames", range.Sweep(new Vector3(0, 0, -2), new Vector3(0, 0, -30), r) == middle && range.Down == 2);
        Check("a ray passing near a target gives its range", range.RangeAlong(Vector3.Zero, Vector3.Normalize(new Vector3(4.5f, 0, -10)), 2.0f) is > 10.0f and < 11.5f);
        Check("one passing far from every target gives none", range.RangeAlong(Vector3.Zero, Vector3.UnitY, 2.0f) == null);

        // ---------------------------------------------------------------- pop and reset
        var homeScale = left.Scale;
        range.Tick(0.1f);
        Check("a hit target shrinks", left.Scale.X < homeScale.X && left.Scale.X > 0.0f, left.Scale.ToString());
        Check("while a standing one does not", right.Scale == homeScale);
        range.Tick(0.2f);
        var ring = left.Children[0].Components.OfType<MeshRendererComponent>().First();
        Check("then it goes out of sight", !ring.IsVisible);

        Check("the clock runs while targets stand", range.Time > 0.29f && range.Time < 0.31f, range.Time.ToString());
        range.Reset();
        Check("start over: every target stands again", range.Down == 0 && range.Standing.Count() == 3);
        Check("at the size the level gave it, and visible", left.Scale == homeScale && ring.IsVisible);
        Check("and the clock is at nothing", range.Time == 0.0f);

        // ---------------------------------------------------------------- winning
        range.Sweep(new Vector3(-3, 0, -9), new Vector3(-3, 0, -11), r);
        range.Sweep(new Vector3(3, 0, -9), new Vector3(3, 0, -11), r);
        Check("two down is not a win", !range.Won);
        range.Tick(0.5f);
        range.Sweep(new Vector3(0, 0, -9), new Vector3(0, 0, -11), r);
        Check("the last one down is", range.Won && range.Down == 3);
        range.Tick(1.0f);
        var stopped = range.Time;
        range.Tick(1.0f);
        Check("the clock stops when you win", stopped > 0.4f && range.Time == stopped, $"{stopped} then {range.Time}");
        Check("and there is nothing left to hit", range.Sweep(new Vector3(0, 0, -9), new Vector3(0, 0, -11), r) == null);
        Check("a level with no targets is never won", !TargetRange.Find(world, "Nothing Called This", 0.2f).Won);

        // ---------------------------------------------------------------- peas, in the physics world
        var physicsContext = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        physicsContext.World.Context = physicsContext;
        physicsContext.World.DefaultScene = new ManaScene(physicsContext) { Name = "Default Scene" };
        physicsContext.World.Scenes.Add(physicsContext.World.DefaultScene);
        physicsContext.Physics = new ManaPhysics(physicsContext);
        var floor = new ManaSceneNode(physicsContext, Matrix4x4.CreateTranslation(0, -0.1f, 0)) { Name = "Floor" };
        physicsContext.World.DefaultScene.Children.Add(floor);
        floor.UpdateTransform();
        physicsContext.Physics.AddBoxBody(floor, new Vector3(1000, 0.2f, 1000), Vector3.Zero);

        var field = TargetRange.Find(physicsContext.World, "Nothing", 0.2f);
        var shooter = new PeaShooter(physicsContext);
        shooter.Fire(new Vector3(0, 1.5f, 0), new Vector3(0, 0, -1));
        Check("a pea is in flight", shooter.InFlight == 1);
        var dt = 1.0f / 60.0f;
        for (var frame = 0; frame < 15; frame++)
        {
            physicsContext.Physics.Simulation.Timestep(dt, physicsContext.Physics.ThreadDispatcher);
            shooter.Tick(dt, field);
        }
        var flying = shooter.Positions.First();
        Check("it flies the way it was aimed, fast", flying.Z < -8.0f && Math.Abs(flying.X) < 0.01f, flying.ToString());
        Check("and falls under gravity as it goes", flying.Y < 1.5f && flying.Y > 0.5f, flying.Y.ToString());
        for (var frame = 0; frame < 120; frame++)
        {
            physicsContext.Physics.Simulation.Timestep(dt, physicsContext.Physics.ThreadDispatcher);
            shooter.Tick(dt, field);
        }
        var landed = shooter.Positions.FirstOrDefault();
        Check("the floor holds it up, not letting it through", shooter.InFlight == 1 && landed.Y > 0.05f && landed.Y < 0.3f, landed.ToString());
        shooter.Fire(new Vector3(0, 1.5f, 0), new Vector3(0, 1, -1));
        shooter.Fire(new Vector3(0, 1.5f, 0), new Vector3(0, 1, -1));
        shooter.Clear();
        Check("clearing takes them all out of the world", shooter.InFlight == 0 && physicsContext.Physics.PhysicsMap.Count == 1);

        // Aimed at a target, a pea knocks it down and is used up.
        var mark = new ManaSceneNode(physicsContext, Matrix4x4.CreateTranslation(0, 1.5f, -12)) { Name = "Target A" };
        mark.Components.Add(new MeshRendererComponent(mark, new ManaCube(physicsContext, Vector4.One, Vector3.One)));
        physicsContext.World.DefaultScene.Children.Add(mark);
        mark.UpdateTransform();
        var course = TargetRange.Find(physicsContext.World, "Target", 0.2f);
        var lift = 0.5f * PeaShooter.EffectiveGravity * MathF.Pow(12.0f / PeaShooter.Speed, 2.0f);
        shooter.Fire(new Vector3(0, 1.5f, 0), new Vector3(0, lift, -12));
        for (var frame = 0; frame < 60 && course.Down == 0; frame++)
        {
            physicsContext.Physics.Simulation.Timestep(dt, physicsContext.Physics.ThreadDispatcher);
            shooter.Tick(dt, course);
        }
        Check("a pea aimed at a target knocks it down", course.Won);
        Check("and is used up", shooter.InFlight == 0);

        // The mode lightens the world; a pea still falls by its own gravity, and a dropped ball by the new one.
        physicsContext.Physics.Gravity = new Vector3(0, -24, 0);
        shooter.Fire(new Vector3(0, 20, 0), new Vector3(1, 0, 0));
        var ball = new ManaSceneNode(physicsContext, Matrix4x4.CreateTranslation(50, 20, 0)) { Name = "Ball" };
        physicsContext.World.DefaultScene.Children.Add(ball);
        ball.UpdateTransform();
        var ballBody = physicsContext.Physics.AddDynamicSphereBody(ball, 0.1f, 1.0f, Vector3.Zero);
        for (var frame = 0; frame < 30; frame++)
        {
            physicsContext.Physics.Simulation.Timestep(dt, physicsContext.Physics.ThreadDispatcher);
            shooter.Tick(dt, field);
        }
        var peaDrop = 20.0f - shooter.Positions.First().Y;
        var ballDrop = 20.0f - physicsContext.Physics.Simulation.Bodies[ballBody].Pose.Position.Y;
        Check("a pea falls by its own gravity in a lighter world", Math.Abs(peaDrop - 0.5f * PeaShooter.EffectiveGravity * 0.25f) < 0.15f, peaDrop.ToString());
        Check("and setting the world's gravity reaches the simulation", Math.Abs(ballDrop - 0.5f * 24.0f * 0.25f) < 0.3f, ballDrop.ToString());
        physicsContext.Physics.Gravity = ManaPhysics.DefaultGravity;
        shooter.Clear();
        shooter.Dispose();
        Check("disposing takes the pea scene out of the world", !physicsContext.World.Scenes.Any(sc => sc.Name == "Peas"));

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
            Check("and targets, each placed from a Target prefab", targets.Count >= 10
                && targets.All(t => t.GetProperty("kind").GetString() == "Prefab"
                    && t.GetProperty("prefab").GetProperty("assetPath").GetString()!.StartsWith("Prefabs/Target Practice/")),
                targets.Count.ToString());
            Check("and runs the game", document.RootElement.GetProperty("gameModes").EnumerateArray()
                .Any(m => m.GetProperty("type").GetString() == TargetPracticeMode.ModeType));
            Check("and shows the HUD layout, bound to the mode", nodes.SelectMany(n => n.TryGetProperty("scripts", out var s) ? s.EnumerateArray() : Enumerable.Empty<JsonElement>())
                .Any(s => s.GetProperty("type").GetString() == "UiCanvas"
                    && s.GetProperty("properties").GetProperty("bindingSource").GetString() == TargetPracticeMode.ModeType));
        }

        // ---------------------------------------------------------------- the HUD: a layout, bound to the view model
        var hudFile = FindContent(Path.Combine("UI", "Target Practice HUD.manaui"));
        Check("the HUD layout is there", hudFile != null);
        if (hudFile != null)
        {
            var hud = new TargetPracticeHud();
            var layout = UiMarkup.Load(File.ReadAllText(hudFile), new UiTheme(), hud, typeof(TargetPracticeHud));
            Check("it binds only to what the view model has", layout.Problems.Count == 0, string.Join("; ", layout.Problems));
            hud.Refresh(3, 11, 12.34f, 5, false);
            Check("and follows it: the count", layout.Find<TextBlock>("Count")?.Text == "3 / 11", layout.Find<TextBlock>("Count")?.Text ?? "missing");
            Check("the time, formatted by the layout", layout.Find<TextBlock>("Time")?.Text == "12.3 s", layout.Find<TextBlock>("Time")?.Text ?? "missing");
            Check("the bar", layout.Find<ProgressBar>("Progress") is { } bar && Math.Abs(bar.Fraction - 3.0f / 11.0f) < 1e-4f);
            Check("and no banner until the last one falls", layout.Find<Border>("Win Banner")?.Visibility == Visibility.Collapsed);
            hud.Refresh(11, 11, 20.0f, 14, true);
            Check("then the banner, with the score", layout.Find<Border>("Win Banner")?.Visibility == Visibility.Visible
                && layout.Find<TextBlock>("Win Message")?.Text == "All down in 20.0 s with 14 shots -- R to go again",
                layout.Find<TextBlock>("Win Message")?.Text ?? "missing");
        }

        // ---------------------------------------------------------------- prefabs placed from code
        var spawnContext = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        spawnContext.World.Context = spawnContext;
        var spawnScene = new ManaScene(spawnContext) { Name = "Spawned" };
        spawnContext.World.Scenes.Add(spawnScene);
        var target = ManaPrefabs.Spawn(spawnContext, spawnScene, "Prefabs/Target Practice/Target.manaprefab", Matrix4x4.CreateTranslation(1, 2, 3), "Target 99");
        Check("a prefab placed from code has its parts", target != null && ManaPrefabs.Find(target, "Bullseye") != null);
        Check("named and placed as asked", target is { Name: "Target 99" } && target.WorldTransform.Translation == new Vector3(1, 2, 3));
        Check("its sound on the cue a hit raises", target != null
            && TargetRange.Subtree(target).SelectMany(n => n.Components).OfType<ManaAudioSourceComponent>().Any(a => a.Cue == ManaSoundCue.EnemyDefeat));
        Check("and never saved into a level", target != null && TargetRange.Subtree(target).All(n => n.IsEditorTransient));
        if (target != null)
        {
            ManaPrefabs.Despawn(target);
        }
        Check("taking it out leaves nothing behind", spawnScene.Children.Count == 0);
        var pea = new ManaModelLibrary(spawnContext).Model("Prefabs/Target Practice/Pea.manaprefab");
        Check("the model library reads a prefab for its look", pea != null && Math.Abs(pea.BoundsMax.Y - pea.BoundsMin.Y - 0.24f) < 0.01f,
            pea == null ? "null" : (pea.BoundsMax - pea.BoundsMin).ToString());
    }

    private static string? FindContent(string relative)
    {
        var level = FindLevel();
        if (level == null)
        {
            return null;
        }
        var file = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(level))!, relative);
        if (File.Exists(file))
        {
            return file;
        }
        return null;
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
