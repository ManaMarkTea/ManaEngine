using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Animation;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The play loop's rules: coins, checkpoints, hazards, falling, and the goal.
/// <para>
/// Everything here runs against a pawn with no physics body -- the pieces only read where the player
/// stands and tell <see cref="ManaGameState"/> what happened, so a position and a tick are all a test has
/// to supply. That is the reason the pieces are overlap tests rather than physics sensors.
/// </para>
/// </summary>
public static class CoursePieceChecks
{
    private const float FrameSeconds = 1.0f / 60.0f;

    private sealed class Course
    {
        public required ManaContext Context { get; init; }

        public required ManaPawnNode Player { get; init; }

        public ManaWorld World => Context.World;

        public ManaGameState State => World.GameState;

        public T Place<T>(Vector3 position, Func<ManaSceneNode, T> create, bool withMesh = false) where T : ManaComponent
        {
            var node = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(position));
            var component = create(node);
            node.Components.Add(component);
            if (withMesh)
            {
                node.Components.Add(new MeshRendererComponent(node, new ManaCube(Context, Vector4.One, Vector3.One)));
            }

            World.DefaultScene.Children.Add(node);
            node.UpdateTransform();
            return component;
        }

        /// <summary>Puts the player somewhere and runs one frame, which is when overlaps are noticed.</summary>
        public void Step(Vector3? playerAt = null, int frames = 1)
        {
            if (playerAt.HasValue)
            {
                Player.Position = playerAt.Value;
            }

            for (var frame = 0; frame < frames; frame++)
            {
                ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(FrameSeconds);
                State.Tick(FrameSeconds);
                foreach (var scene in World.Scenes)
                {
                    foreach (var node in scene.Children)
                    {
                        node.UpdateTransform();
                    }
                }
            }
        }
    }

    /// <summary>
    /// A piece's trigger volume as the level stores it: on the node's Trigger script, or -- in a level saved before
    /// volumes had a component of their own -- inside the piece's own script.
    /// </summary>
    private static (Vector3 Size, Vector3 Offset) VolumeOf(ManaLevelNode node)
    {
        var holder = node.Scripts!.FirstOrDefault(script => script.Type == TriggerVolumeComponent.ScriptType)
            ?? node.Scripts!.First(script => script.Properties.ContainsKey("size"));
        var size = ReadVector(holder.Properties["size"]);
        var offset = holder.Properties.TryGetValue("offset", out var offsetValue) ? ReadVector(offsetValue) : Vector3.Zero;
        return (size, offset);
    }

    private static Vector3 ReadVector(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Course CreateCourse()
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);

        var player = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false };
        world.DefaultScene.Children.Add(player);
        world.GameState.Player = player;

        return new Course { Context = context, Player = player };
    }

    private static ManaGameState FullMarks(int deaths = 0)
    {
        var state = new ManaGameState();
        state.RegisterCollectible(2);
        state.AddCoins(2);
        for (var death = 0; death < deaths; death++)
        {
            state.RegisterFall();
        }

        return state;
    }

    private static readonly Vector3 Far = new(500.0f, 0.0f, 500.0f);

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- the overlap test
        var at = Matrix4x4.CreateTranslation(10.0f, 0.0f, 0.0f);
        var box = new Vector3(2.0f, 2.0f, 2.0f);

        bool Overlaps(Matrix4x4 world, Vector3 offset, Vector3 feet) =>
            TriggerVolumeComponent.Contains(world, offset, box, feet, 0.4f, 2.0f);

        Check("standing in a trigger overlaps it", Overlaps(at, Vector3.Zero, new Vector3(10.0f, 0.0f, 0.0f)));
        Check("standing well clear does not", !Overlaps(at, Vector3.Zero, new Vector3(20.0f, 0.0f, 0.0f)));

        Check("the player's own width counts, so grazing the edge overlaps",
            Overlaps(at, Vector3.Zero, new Vector3(11.3f, 0.0f, 0.0f)));
        Check("but not past it", !Overlaps(at, Vector3.Zero, new Vector3(11.5f, 0.0f, 0.0f)));

        Check("a trigger's height is measured from the feet, so a player far above does not touch it",
            !Overlaps(at, Vector3.Zero, new Vector3(10.0f, 5.0f, 0.0f)));

        Check("the offset moves the volume off the node",
            Overlaps(at, new Vector3(0.0f, 0.0f, 6.0f), new Vector3(10.0f, 0.0f, 6.0f))
            && !Overlaps(at, new Vector3(0.0f, 0.0f, 6.0f), new Vector3(10.0f, 0.0f, 0.0f)));

        // A long thin box turned a quarter turn: its long axis now runs along X instead of Z.
        var thin = new Vector3(1.0f, 2.0f, 20.0f);
        var turned = Matrix4x4.CreateRotationY(MathF.PI * 0.5f) * Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f);
        Check("a turned trigger follows its rotation",
            TriggerVolumeComponent.Contains(turned, Vector3.Zero, thin, new Vector3(8.0f, 0.0f, 0.0f), 0.4f, 2.0f)
            && !TriggerVolumeComponent.Contains(turned, Vector3.Zero, thin, new Vector3(0.0f, 0.0f, 8.0f), 0.4f, 2.0f));

        // ---------------------------------------------------------------- inert outside play
        var editing = CreateCourse();
        var idleCoin = editing.Place(Vector3.Zero, node => new CollectibleComponent(node));
        editing.Step(Vector3.Zero, 3);
        Check("nothing triggers while the world is not playing",
            !idleCoin.IsCollected && editing.State.Coins == 0);

        // ---------------------------------------------------------------- coins
        var coins = CreateCourse();
        var near = coins.Place(new Vector3(0.0f, 0.0f, 0.0f), node => new CollectibleComponent(node) { Value = 2 }, withMesh: true);
        var distant = coins.Place(new Vector3(30.0f, 0.0f, 0.0f), node => new CollectibleComponent(node), withMesh: true);
        coins.World.BeginPlay();

        Check("beginning play counts every coin on offer",
            coins.State.TotalCoins == 3, $"{coins.State.TotalCoins} on offer");

        coins.Step(Far);
        Check("a coin the player is not near is left alone", !near.IsCollected && coins.State.Coins == 0);

        coins.Step(Vector3.Zero);
        var nearMeshes = near.Owner.Components.OfType<MeshRendererComponent>().ToList();
        Check("walking into a coin collects it for its value", near.IsCollected && coins.State.Coins == 2,
            $"{coins.State.Coins} coins");
        Check("and hides it", nearMeshes.Count == 1 && nearMeshes.All(mesh => !mesh.IsVisible));

        coins.Step(Far);
        coins.Step(Vector3.Zero, 5);
        Check("coming back to an empty spot does not pay twice", coins.State.Coins == 2);

        coins.Step(new Vector3(30.0f, 0.0f, 0.0f));
        Check("the other coin adds to the total", distant.IsCollected && coins.State.Coins == 3);

        coins.World.EndPlay();
        Check("stopping play puts the coins back",
            !near.IsCollected && nearMeshes.All(mesh => mesh.IsVisible),
            "or the next run would start with the course already eaten");

        coins.World.BeginPlay();
        Check("and the next run starts from nothing", coins.State.Coins == 0 && coins.State.TotalCoins == 3);
        coins.World.EndPlay();

        // ---------------------------------------------------------------- hazards
        var hazards = CreateCourse();
        var spikes = hazards.Place(Vector3.Zero, node => new HazardComponent(node) { Damage = 1 });
        hazards.World.BeginPlay();

        Check("a level with a hazard shows health", hazards.State.UsesHealth);
        Check("health starts full", hazards.State.Health == hazards.State.MaxHealth);

        hazards.Step(Vector3.Zero);
        Check("a hazard hurts on touch", hazards.State.Health == hazards.State.MaxHealth - 1,
            $"{hazards.State.Health} health");

        hazards.Step(Vector3.Zero, 10);
        Check("and does not hurt again straight away while the player is still inside it",
            hazards.State.Health == hazards.State.MaxHealth - 1, "the brief invulnerability spaces the hits");

        hazards.Step(Vector3.Zero, (int)(ManaGameState.HitInvulnerableSeconds / FrameSeconds) + 5);
        Check("but keeps hurting if the player stays", hazards.State.Health == hazards.State.MaxHealth - 2,
            $"{hazards.State.Health} health");

        hazards.Step(Vector3.Zero, (int)(ManaGameState.HitInvulnerableSeconds / FrameSeconds) + 5);
        Check("running out of health is a death: full health again", hazards.State.Health == hazards.State.MaxHealth
            && hazards.State.Deaths == 1, $"{hazards.State.Health} health, {hazards.State.Deaths} deaths");

        Check("and the player is put back at the respawn point",
            hazards.Player.Position == hazards.Player.RespawnPoint);
        hazards.World.EndPlay();

        var pit = CreateCourse();
        pit.Place(Vector3.Zero, node => new HazardComponent(node) { Kills = true });
        pit.Player.RespawnPoint = new Vector3(40.0f, 0.0f, 0.0f);
        pit.World.BeginPlay();
        pit.Step(Vector3.Zero);
        Check("a deadly hazard is a death at once, whatever the health",
            pit.State.Deaths == 1 && pit.Player.Position == new Vector3(40.0f, 0.0f, 0.0f));
        pit.World.EndPlay();

        // ---------------------------------------------------------------- checkpoints
        var checkpoints = CreateCourse();
        var flag = checkpoints.Place(new Vector3(20.0f, 0.0f, 0.0f), node => new CheckpointComponent(node));
        checkpoints.Place(new Vector3(60.0f, 0.0f, 0.0f), node => new HazardComponent(node) { Kills = true });
        checkpoints.Player.RespawnPoint = new Vector3(1.0f, 0.5f, 1.0f);
        checkpoints.World.BeginPlay();

        checkpoints.Step(new Vector3(20.0f, 0.0f, 0.0f));
        Check("touching a checkpoint moves the respawn point to it",
            flag.IsActivated && checkpoints.Player.RespawnPoint.X == 20.0f,
            $"{checkpoints.Player.RespawnPoint}");

        checkpoints.Step(new Vector3(60.0f, 0.0f, 0.0f));
        Check("and dying afterwards brings the player back to it, not to the start",
            checkpoints.State.Deaths == 1 && checkpoints.Player.Position.X == 20.0f,
            $"{checkpoints.Player.Position}");

        checkpoints.World.EndPlay();
        Check("stopping play rearms the checkpoint", !flag.IsActivated);

        // ---------------------------------------------------------------- falling
        var falling = CreateCourse();
        falling.World.BeginPlay();
        var fellBefore = falling.State.Deaths;
        falling.State.Damage(1);

        // The pawn raises this itself when it drops below FallResetHeight; the state only has to count it.
        falling.State.RegisterFall();
        Check("falling out of the world costs a death and restores health",
            falling.State.Deaths == fellBefore + 1 && falling.State.Health == falling.State.MaxHealth,
            $"{falling.State.Deaths} deaths, {falling.State.Health} health");
        falling.World.EndPlay();

        // ---------------------------------------------------------------- the goal
        var goal = CreateCourse();
        goal.Place(new Vector3(0.0f, 0.0f, 0.0f), node => new CollectibleComponent(node));
        goal.Place(new Vector3(20.0f, 0.0f, 0.0f), node => new GoalComponent(node) { RequireAllCoins = true });
        goal.World.BeginPlay();

        goal.Step(new Vector3(20.0f, 0.0f, 0.0f));
        Check("a goal that wants every coin stays shut without them",
            goal.State.Status == ManaGameStatus.Playing && goal.State.Message.Contains("coin", StringComparison.OrdinalIgnoreCase),
            goal.State.Message);

        goal.Step(Vector3.Zero);
        goal.Step(Far);
        goal.Step(new Vector3(20.0f, 0.0f, 0.0f));
        Check("and opens once they are all collected", goal.State.Status == ManaGameStatus.Won);

        var finishedAt = goal.State.ElapsedSeconds;
        goal.Step(Far, 30);
        Check("the clock stops when the level is won", goal.State.ElapsedSeconds == finishedAt);

        goal.State.Damage(1);
        Check("and nothing hurts a winner", goal.State.Health == goal.State.MaxHealth);
        goal.World.EndPlay();

        var open = CreateCourse();
        open.Place(Vector3.Zero, node => new GoalComponent(node));
        open.World.BeginPlay();
        open.Step(Vector3.Zero);
        Check("an ordinary goal ends the level on touch", open.State.Status == ManaGameStatus.Won);
        open.World.BeginPlay();
        open.World.EndPlay();
        open.World.BeginPlay();
        Check("the next run starts playing again", open.State.Status == ManaGameStatus.Playing);
        open.World.EndPlay();

        // ---------------------------------------------------------------- the HUD line
        var hud = new ManaGameState();
        Check("with nothing to collect and nothing to fear the HUD is only the clock",
            hud.FormatHud() == "Time 0:00.0", hud.FormatHud());

        hud.RegisterCollectible(3);
        hud.RegisterHazard();
        hud.AddCoins(1);
        hud.Tick(75.5f);
        Check("coins, health and time show once the level has them",
            hud.FormatHud() == "Coins 1/3   💖💖💖   Time 1:15.5", hud.FormatHud());

        // ---------------------------------------------------------------- saved in a level
        var authoring = CreateCourse();
        var authoredCoin = authoring.Place(Vector3.Zero, node => new CollectibleComponent(node)
        {
            Value = 5,
            Size = new Vector3(3.0f, 4.0f, 5.0f),
            Offset = new Vector3(0.0f, 1.5f, 0.0f)
        });
        authoredCoin.Owner.LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One);
        authoring.Place(new Vector3(9.0f, 0.0f, 0.0f), node => new GoalComponent(node) { RequireAllCoins = true })
            .Owner.LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One);

        var captured = ManaLevelCapture.CaptureScene(authoring.World, authoring.World.DefaultScene, "Course");
        var scripts = captured.Nodes.SelectMany(node => node.Scripts ?? new List<ManaLevelScript>()).Select(script => script.Type).ToList();
        Check("course pieces are written into the level",
            scripts.Contains(CollectibleComponent.ScriptType) && scripts.Contains(GoalComponent.ScriptType),
            string.Join(", ", scripts));

        var reloadedWorld = new ManaWorld();
        var reloadedContext = new ManaContext(null!, null!, reloadedWorld) { DeferGpuUploads = true };
        var reloadedScene = new ManaScene(reloadedContext) { Name = "Reloaded" };
        reloadedWorld.Scenes.Add(reloadedScene);
        var reloaded = ManaLevelLoader.Load(
            ManaLevel.FromJson(captured.ToJson()),
            reloadedContext,
            reloadedScene,
            replaceAuthoredNodes: false);

        var coinBack = reloaded.Nodes.SelectMany(node => node.GetComponents<CollectibleComponent>()).FirstOrDefault();
        var goalBack = reloaded.Nodes.SelectMany(node => node.GetComponents<GoalComponent>()).FirstOrDefault();
        Check("and read back with their settings",
            coinBack is { Value: 5 } && goalBack is { RequireAllCoins: true },
            $"{coinBack?.Value}, {goalBack?.RequireAllCoins}");
        Check("including the shape of the trigger volume",
            coinBack != null && coinBack.Size == new Vector3(3.0f, 4.0f, 5.0f) && coinBack.Offset == new Vector3(0.0f, 1.5f, 0.0f),
            $"{coinBack?.Size} {coinBack?.Offset}");

        // ---------------------------------------------------------------- seeing the volume in the editor
        var seen = CreateCourse();
        var marked = seen.Place(Vector3.Zero, node => new HazardComponent(node) { Size = new Vector3(4.0f, 2.0f, 4.0f) });
        seen.Step(Far);
        var cage = marked.VolumeMarker;
        var cageRenderer = cage?.Components.OfType<MeshRendererComponent>().FirstOrDefault();
        Check("an editor tick draws the trigger volume as a wireframe", cage != null && cageRenderer is { IsVisible: true });
        Check("and keeps it out of the outliner as editor furniture", cage is { IsEditorTransient: true });

        var meshBefore = cageRenderer?.Mesh;
        marked.Size = new Vector3(8.0f, 2.0f, 4.0f);
        seen.Step(Far);
        Check("and redraws it when the size changes", cageRenderer != null && !ReferenceEquals(cageRenderer.Mesh, meshBefore));

        marked.Owner.Scale = new Vector3(2.0f, 2.0f, 2.0f);
        marked.Offset = new Vector3(0.0f, 1.0f, 0.0f);
        seen.Step(Far);
        Check("the wireframe undoes the node's scale, since the volume ignores it",
            cage != null && cage.Scale == new Vector3(0.5f, 0.5f, 0.5f) && cage.Position == new Vector3(0.0f, 0.5f, 0.0f),
            $"{cage?.Scale} {cage?.Position}");

        seen.World.BeginPlay();
        Check("playing hides it", cageRenderer is { IsVisible: false });
        seen.World.EndPlay();
        seen.Step(Far);
        Check("and stopping brings it back", cageRenderer is { IsVisible: true });

        var scaled = ManaLevelCapture.CaptureScene(seen.World, seen.World.DefaultScene, "Marked");
        Check("the wireframe is editor furniture and never saved", scaled.Nodes.Count == 0, $"{scaled.Nodes.Count} nodes");

        // ---------------------------------------------------------------- the Add menu's ready-made pieces
        var built = CreateCourse();
        var shapes = new ManaShapeFactory(built.Context, built.World.DefaultScene);
        foreach (var (label, build) in CoursePieceFactory.Pieces)
        {
            build(shapes, new Vector3(5.0f, 0.0f, 5.0f));
        }

        var builtLevel = ManaLevelCapture.CaptureScene(built.World, built.World.DefaultScene, "Pieces");
        var allBuiltTypes = builtLevel.Nodes.SelectMany(node => node.Scripts ?? new List<ManaLevelScript>()).Select(script => script.Type).ToList();

        // Every piece carries a trigger volume of its own, saved as a script beside its behaviour.
        Check("and each saves its trigger volume beside it", allBuiltTypes.Count(type => type == TriggerVolumeComponent.ScriptType) >= 6,
            string.Join(", ", allBuiltTypes));
        var builtTypes = allBuiltTypes.Where(type => type != TriggerVolumeComponent.ScriptType).ToList();
        Check("each ready-made piece saves with its behaviour",
            builtTypes.Count == CoursePieceFactory.Pieces.Length
            && builtTypes.Contains(CollectibleComponent.ScriptType) && builtTypes.Contains(HazardComponent.ScriptType)
            && builtTypes.Contains(CheckpointComponent.ScriptType) && builtTypes.Contains(GoalComponent.ScriptType),
            string.Join(", ", builtTypes));

        var spin = CreateCourse();
        var spinner = spin.Place(Vector3.Zero, node => new CollectibleComponent(node) { SpinDegreesPerSecond = 90.0f });
        spin.Step(Far, 30);
        Check("a coin does not spin in the editor", spinner.Owner.Rotation.Y == 0.0f);
        spin.World.BeginPlay();
        spin.Step(Far, 60);
        Check("but spins while playing", MathF.Abs(spinner.Owner.Rotation.Y) > 0.5f, $"{spinner.Owner.Rotation.Y}");
        spin.World.EndPlay();
        Check("and stands as it was placed afterwards", spinner.Owner.Rotation.Y == 0.0f);

        // ---------------------------------------------------------------- moving platforms
        Check("a platform starts at its placed end", MovingPlatformComponent.FractionAt(10.0f, 5.0f, 1.0f, 0.0f) == 0.0f);
        Check("is at the far end after one leg", MathF.Abs(MovingPlatformComponent.FractionAt(10.0f, 5.0f, 1.0f, 2.0f) - 1.0f) < 0.001f);
        Check("waits there", MovingPlatformComponent.FractionAt(10.0f, 5.0f, 1.0f, 2.8f) == 1.0f);
        Check("is back at the start after out, wait and back",
            MathF.Abs(MovingPlatformComponent.FractionAt(10.0f, 5.0f, 1.0f, 5.0f)) < 0.001f);
        Check("and repeats", MathF.Abs(MovingPlatformComponent.FractionAt(10.0f, 5.0f, 1.0f, 2.0f + 6.0f) - 1.0f) < 0.001f);
        Check("each leg is eased, so halfway is halfway but the ends are gentle",
            MathF.Abs(MovingPlatformComponent.FractionAt(10.0f, 5.0f, 0.0f, 1.0f) - 0.5f) < 0.001f
            && MovingPlatformComponent.FractionAt(10.0f, 5.0f, 0.0f, 0.1f) < 0.02f);

        var slab = Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f);
        var slabSize = new Vector3(3.0f, 0.5f, 3.0f);
        Check("feet on the top face are standing on it",
            MovingPlatformComponent.IsStandingOn(slab, Vector3.Zero, slabSize, new Vector3(0.0f, 0.25f, 0.0f)));
        Check("feet a long way above it are not", !MovingPlatformComponent.IsStandingOn(slab, Vector3.Zero, slabSize, new Vector3(0.0f, 3.0f, 0.0f)));
        Check("nor feet beside it", !MovingPlatformComponent.IsStandingOn(slab, Vector3.Zero, slabSize, new Vector3(4.0f, 0.25f, 0.0f)));
        Check("nor feet underneath", !MovingPlatformComponent.IsStandingOn(slab, Vector3.Zero, slabSize, new Vector3(0.0f, -2.0f, 0.0f)));

        var moving = CreateCourse();
        var lift = moving.Place(Vector3.Zero, node => new MovingPlatformComponent(node)
        {
            Travel = new Vector3(10.0f, 0.0f, 0.0f),
            Speed = 5.0f,
            PauseSeconds = 0.0f
        });
        moving.Player.Position = new Vector3(0.0f, 0.25f, 0.0f);
        moving.Step(null);
        Check("the editor shows a ghost of the platform at its far end", lift.DestinationMarker != null && lift.DestinationMarker.Position.X > 9.0f,
            $"{lift.DestinationMarker?.Position}");

        moving.World.BeginPlay();
        moving.Step(null, 60);
        Check("a platform moves while playing", lift.Owner.Position.X > 1.0f, $"{lift.Owner.Position.X}");
        Check("and carries a player standing on it", MathF.Abs(moving.Player.Position.X - lift.Owner.Position.X) < 0.01f
            && lift.IsCarryingPlayer, $"platform {lift.Owner.Position.X}, player {moving.Player.Position.X}");

        moving.Player.Position = new Vector3(0.0f, 5.0f, 8.0f);
        var leftBehind = moving.Player.Position;
        moving.Step(null, 30);
        // Only X and Z: a pawn with no physics body is pinned to the ground height on its own.
        Check("but not one who is not on it",
            moving.Player.Position.X == leftBehind.X && moving.Player.Position.Z == leftBehind.Z && !lift.IsCarryingPlayer,
            $"{moving.Player.Position}");

        moving.World.EndPlay();
        Check("stopping play puts the platform back where it was placed", lift.Owner.Position == Vector3.Zero);

        // ---------------------------------------------------------------- enemies
        Check("landing on a head is a stomp", EnemyComponent.IsStomp(feetY: 1.2f, topY: 1.3f, fallingSpeed: 4.0f));
        Check("walking into the side is not", !EnemyComponent.IsStomp(feetY: 0.0f, topY: 1.3f, fallingSpeed: 0.0f));
        Check("nor is rising into it from below, however high the feet are",
            !EnemyComponent.IsStomp(feetY: 1.2f, topY: 1.3f, fallingSpeed: -6.0f));

        var foes = CreateCourse();
        var walker = foes.Place(new Vector3(0.0f, 0.6f, 0.0f), node => new EnemyComponent(node)
        {
            Travel = new Vector3(6.0f, 0.0f, 0.0f),
            Speed = 3.0f,
            PauseSeconds = 0.0f
        }, withMesh: true);
        var enemyMeshes = walker.Owner.Components.OfType<MeshRendererComponent>().ToList();
        foes.World.BeginPlay();

        Check("an enemy counts as one to defeat, and as a reason to show health",
            foes.State.TotalEnemies == 1 && foes.State.UsesHealth);

        foes.Step(Far, 60);
        Check("it walks its beat", walker.Owner.Position.X > 0.5f, $"{walker.Owner.Position.X}");
        Check("facing where it is going", MathF.Abs(walker.Owner.Rotation.Y - (MathF.PI * 0.5f)) < 0.05f, $"{walker.Owner.Rotation.Y}");

        foes.Step(Far, 300);
        Check("and turns round at the end of it", walker.Owner.Position.X < 6.01f && walker.Owner.Position.X >= 0.0f,
            $"{walker.Owner.Position.X}");

        var fast = CreateCourse();
        var sprinter = fast.Place(new Vector3(0.0f, 0.6f, 0.0f), node => new EnemyComponent(node)
        {
            Travel = new Vector3(20.0f, 0.0f, 0.0f),
            Speed = 12.0f,
            PauseSeconds = 0.0f
        });
        fast.World.BeginPlay();
        fast.Step(Far, 60 * 2);
        Check("a fast enemy does not stall on its own beat", sprinter.Owner.Position.X > 15.0f, $"{sprinter.Owner.Position.X}");
        fast.World.EndPlay();

        // Standing at the enemy's own spot at ground level: side-on, so it hurts.
        foes.Player.GroundHeight = 0.0f;
        var healthBefore = foes.State.Health;
        foes.Step(new Vector3(walker.Owner.Position.X, 0.0f, 0.0f));
        Check("touching an enemy from the side hurts", foes.State.Health < healthBefore && !walker.IsDefeated,
            $"{foes.State.Health} health");

        // Above it and falling onto its head.
        foes.Player.GroundHeight = 1.2f;
        foes.Step(new Vector3(walker.Owner.Position.X, 1.2f, 0.0f));
        Check("landing on its head defeats it", walker.IsDefeated && foes.State.EnemiesDefeated == 1);
        Check("and hides it", enemyMeshes.All(mesh => !mesh.IsVisible));

        var restedAt = walker.Owner.Position;
        foes.Step(Far, 30);
        Check("a defeated enemy stays where it fell", walker.Owner.Position == restedAt);

        foes.World.EndPlay();
        Check("stopping play sets it back on its beat, alive and visible",
            !walker.IsDefeated && walker.Owner.Position == new Vector3(0.0f, 0.6f, 0.0f) && enemyMeshes.All(mesh => mesh.IsVisible));

        var hunters = CreateCourse();
        var hunter = hunters.Place(new Vector3(0.0f, 0.6f, 0.0f), node => new EnemyComponent(node)
        {
            Travel = new Vector3(0.0f, 0.0f, 0.0f),
            ChaseRadius = 6.0f,
            ChaseSpeed = 4.0f,
            LeashRadius = 20.0f
        });
        hunters.World.BeginPlay();
        hunters.Step(new Vector3(20.0f, 0.0f, 0.0f), 30);
        Check("an enemy ignores a player outside its chase radius", !hunter.IsChasing && hunter.Owner.Position.X == 0.0f);

        hunters.Step(new Vector3(5.0f, 0.0f, 0.0f), 30);
        Check("and goes after one inside it", hunter.IsChasing && hunter.Owner.Position.X > 1.0f, $"{hunter.Owner.Position.X}");

        hunters.Step(new Vector3(60.0f, 0.0f, 0.0f), 200);
        Check("then gives up and walks home when they leave", !hunter.IsChasing && hunter.Owner.Position.X < 0.1f, $"{hunter.Owner.Position.X}");

        var tethered = CreateCourse();
        var dog = tethered.Place(new Vector3(0.0f, 0.6f, 0.0f), node => new EnemyComponent(node)
        {
            Travel = Vector3.Zero,
            ChaseRadius = 100.0f,
            ChaseSpeed = 4.0f,
            LeashRadius = 3.0f
        });
        tethered.World.BeginPlay();
        tethered.Step(new Vector3(40.0f, 0.0f, 0.0f), 300);
        Check("a chasing enemy stops at its leash", dog.Owner.Position.X <= 3.2f, $"{dog.Owner.Position.X}");

        var hud2 = new ManaGameState();
        hud2.RegisterEnemy();
        hud2.RegisterEnemy();
        hud2.DefeatEnemy();
        Check("the HUD counts enemies once there are some", hud2.FormatHud().Contains("Enemies 1/2"), hud2.FormatHud());

        // ---------------------------------------------------------------- the results screen and restarting
        var run = CreateCourse();
        var runCoin = run.Place(Vector3.Zero, node => new CollectibleComponent(node), withMesh: true);
        run.Place(new Vector3(20.0f, 0.0f, 0.0f), node => new GoalComponent(node));
        run.World.BeginPlay();
        run.Step(Vector3.Zero);
        run.Step(new Vector3(20.0f, 0.0f, 0.0f));
        Check("the run is won with its coin", run.State.Status == ManaGameStatus.Won && run.State.Coins == 1);

        var spawn = new Vector3(7.0f, 0.5f, 3.0f);
        run.State.Restart(run.World, spawn, 1.0f);
        Check("restarting begins a fresh run", run.World.IsPlaying && run.State.Status == ManaGameStatus.Playing
            && run.State.Coins == 0 && run.State.Deaths == 0 && run.State.ElapsedSeconds == 0.0f);
        Check("with the coin back on offer", !runCoin.IsCollected && run.State.TotalCoins == 1, $"{run.State.TotalCoins}");
        Check("and the player back at the spawn, facing it", run.Player.Position == spawn && MathF.Abs(run.Player.Rotation.Y - 1.0f) < 0.001f
            && run.Player.RespawnPoint == spawn, $"{run.Player.Position}");

        run.Step(spawn);
        run.Step(Vector3.Zero);
        Check("and the coin can be collected again", run.State.Coins == 1);
        run.World.EndPlay();

        Check("a clean sweep gets a compliment", ManaUiPlayResults.Verdict(FullMarks()) == "Flawless!");
        Check("a death takes it away", ManaUiPlayResults.Verdict(FullMarks(deaths: 1)) == "Every coin collected.");
        Check("a level with nothing to collect has nothing to praise", ManaUiPlayResults.Verdict(new ManaGameState()) == string.Empty);
        Check("times read as minutes and seconds", ManaUiPlayResults.FormatTime(75.5f) == "1:15.5", ManaUiPlayResults.FormatTime(75.5f));

        var uiState = new ManaGameState();
        uiState.RegisterCollectible(2);
        uiState.AddCoins(2);
        var uiRoot = new UiRoot(fonts);
        var restarted = 0;
        var exited = 0;
        var results = ManaUiPlayResults.Build(uiRoot.Theme, uiState, () => restarted++, () => exited++, "Back to Editor");
        uiRoot.Content = ManaUiPlayHud.Build(uiRoot.Theme, () => "controls", () => uiState.FormatHud(), results);
        uiRoot.UpdateLayout(new Vector2(1280, 720));
        Check("the results screen is out of the way while playing", results.Visibility == Visibility.Collapsed);

        uiState.Win();
        uiRoot.UpdateLayout(new Vector2(1280, 720));
        Check("and comes up when the level is won", results.Visibility == Visibility.Visible && results.Bounds.Width > 100.0f,
            $"{results.Bounds.Width:0}px wide");
        Check("centred on screen", MathF.Abs((results.Bounds.X + (results.Bounds.Width * 0.5f)) - 640.0f) < 2.0f,
            $"centre {results.Bounds.X + (results.Bounds.Width * 0.5f):0}");

        var hit = uiRoot.HitTest(results.Bounds.X + (results.Bounds.Width * 0.5f), results.Bounds.Y + (results.Bounds.Height * 0.5f));
        Check("the panel takes clicks rather than letting them fall through to the game", hit != null);
        Check("while empty screen around it does not", uiRoot.HitTest(4.0f, 4.0f) == null);

        // ---------------------------------------------------------------- an animated enemy
        ManaAnimationClip Clip(string name, double seconds) => new()
        {
            Name = name, DurationInTicks = seconds, TicksPerSecond = 1.0, Channels = Array.Empty<ManaAnimationChannel>()
        };

        var animated = CreateCourse();
        var actor = new ManaSkeletalActorNode(animated.Context, Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f)) { Name = "Orc" };
        actor.Animation.AddClips(new[] { Clip("idle", 1.0), Clip("walk", 1.0), Clip("sprint", 1.0), Clip("die", 0.5) });
        actor.Components.Add(new MeshRendererComponent(actor, new ManaCube(animated.Context, Vector4.One, Vector3.One)));
        var orcy = new EnemyComponent(actor)
        {
            Size = new Vector3(1.5f, 1.8f, 1.5f),
            Offset = new Vector3(0.0f, 0.9f, 0.0f),
            Travel = new Vector3(8.0f, 0.0f, 0.0f),
            Speed = 2.0f,
            PauseSeconds = 0.0f,
            ChaseRadius = 5.0f
        };
        actor.Components.Add(orcy);
        animated.World.DefaultScene.Children.Add(actor);
        actor.UpdateTransform();
        animated.World.BeginPlay();

        animated.Step(Far, 20);
        Check("an animated enemy walks with its walk clip", actor.Animation.ActiveClip?.Name == "walk", actor.Animation.ActiveClip?.Name ?? "");

        animated.Step(new Vector3(actor.Position.X + 3.0f, 0.0f, 0.0f), 20);
        Check("sprints when it chases", orcy.IsChasing && actor.Animation.ActiveClip?.Name == "sprint", actor.Animation.ActiveClip?.Name ?? "");

        animated.Player.GroundHeight = 1.4f;
        animated.Step(new Vector3(actor.Position.X, 1.4f, 0.0f));
        Check("stomping it plays its die clip", orcy.IsDefeated && actor.Animation.ActiveClip?.Name == "die" && !actor.Animation.Looping,
            actor.Animation.ActiveClip?.Name ?? "");
        Check("and leaves it in view while it falls", actor.Components.OfType<MeshRendererComponent>().All(mesh => mesh.IsVisible));

        animated.Step(Far, 60);
        Check("then takes it away", actor.Components.OfType<MeshRendererComponent>().All(mesh => !mesh.IsVisible));

        animated.World.EndPlay();
        Check("stopping play puts it back, alive and looping",
            !orcy.IsDefeated && actor.Animation.ActiveClip == null && actor.Animation.Looping
            && actor.Components.OfType<MeshRendererComponent>().All(mesh => mesh.IsVisible));

        // ---------------------------------------------------------------- the player's character in a level
        var startCourse = CreateCourse();
        var start = new ManaPlayerStartNode(startCourse.Context, new Vector3(40.0f, 0.0f, 40.0f))
        {
            CharacterPack = "kenney_mini-dungeon",
            CharacterName = "character-human",
            CharacterHeight = 2.0f
        };
        startCourse.World.DefaultScene.Children.Add(start);

        var startLevel = ManaLevelCapture.CaptureScene(startCourse.World, startCourse.World.DefaultScene, "Start");
        var savedStart = startLevel.Nodes.FirstOrDefault(node => node.Kind == ManaLevelSourceKind.PlayerStart);
        Check("a Player Start writes which character the player is",
            savedStart?.Character is { Pack: "kenney_mini-dungeon", Name: "character-human", Height: 2.0f });

        var plain = new ManaPlayerStartNode(startCourse.Context, Vector3.Zero);
        var plainWorld = CreateCourse();
        plainWorld.World.DefaultScene.Children.Add(plain);
        var plainLevel = ManaLevelCapture.CaptureScene(plainWorld.World, plainWorld.World.DefaultScene, "Plain");
        Check("and one that does not care writes nothing", plainLevel.Nodes.Single(node => node.Kind == ManaLevelSourceKind.PlayerStart).Character == null);

        var loadedInto = CreateCourse();
        var loadedStart = ManaLevelLoader.Load(ManaLevel.FromJson(startLevel.ToJson()), loadedInto.Context, loadedInto.World.DefaultScene, replaceAuthoredNodes: false).PlayerStart;
        Check("read back, it still asks for the same character at the same height",
            loadedStart is { CharacterPack: "kenney_mini-dungeon", CharacterName: "character-human", CharacterHeight: 2.0f },
            $"{loadedStart?.CharacterPack}/{loadedStart?.CharacterName}/{loadedStart?.CharacterHeight}");

        // ---------------------------------------------------------------- telling the player what hurt them
        var told = CreateCourse();
        told.Place(Vector3.Zero, node => { node.Name = "Spilled Oil"; return new HazardComponent(node); });
        told.World.BeginPlay();
        told.Step(Vector3.Zero);
        Check("being hurt names what did it", told.State.Message.Contains("Hurt by Spilled Oil"), told.State.Message);
        Check("and it is what the HUD line leads with", told.State.FormatHud().StartsWith("Hurt by Spilled Oil"), told.State.FormatHud());
        Check("the cause is kept", told.State.LastCause == "Spilled Oil");

        var drowned = CreateCourse();
        drowned.Place(Vector3.Zero, node => { node.Name = "Harbor Water"; return new HazardComponent(node) { Kills = true }; });
        drowned.World.BeginPlay();
        drowned.Step(Vector3.Zero);
        Check("a killing hazard records what killed you, for the death screen",
            drowned.State.LastDeathCause == "Harbor Water" && drowned.State.IsShowingDeathScreen && drowned.State.Message.Length == 0,
            $"{drowned.State.LastDeathCause} / '{drowned.State.Message}'");

        var beaten = CreateCourse();
        beaten.World.BeginPlay();
        for (var strike = 0; strike < 3; strike++)
        {
            beaten.State.Damage(1, "Yard Orc");
            beaten.Step(Far, (int)((ManaGameState.HitInvulnerableSeconds + 0.1f) / FrameSeconds));
        }

        Check("the hit that kills names its cause too", beaten.State.Deaths == 1 && beaten.State.LastCause == "Yard Orc", $"{beaten.State.Deaths} deaths, {beaten.State.LastCause}");

        var fell = CreateCourse();
        fell.World.BeginPlay();
        fell.State.RegisterFall();
        Check("falling out of the world is a death of its own kind",
            fell.State.LastDeathFlavor == ManaDeathFlavor.Fall && fell.State.IsShowingDeathScreen, fell.State.LastDeathCause);

        fell.Step(Far, (int)((ManaGameState.DeathScreenSeconds + 0.5f) / FrameSeconds));
        Check("and the death screen goes again after its time", !fell.State.IsShowingDeathScreen);

        var stomped = CreateCourse();
        var thug = stomped.Place(new Vector3(0.0f, 0.6f, 0.0f), node => { node.Name = "Yard Hog"; return new EnemyComponent(node) { Travel = Vector3.Zero }; });
        stomped.World.BeginPlay();
        stomped.Player.GroundHeight = 0.0f;
        stomped.Step(new Vector3(0.0f, 0.0f, 0.0f));
        Check("an enemy is named when it hurts", stomped.State.Message.Contains("Hurt by Yard Hog"), stomped.State.Message);

        // ---------------------------------------------------------------- dying, and dying again straight away
        var wade = CreateCourse();
        wade.Place(Vector3.Zero, node => { node.Name = "Harbor Water"; return new HazardComponent(node) { Kills = true, Flavor = ManaDeathFlavor.Drowned }; });
        wade.World.BeginPlay();
        wade.Step(Vector3.Zero);
        Check("the first touch of deadly water kills at once", wade.State.Deaths == 1 && wade.State.LastDeathFlavor == ManaDeathFlavor.Drowned);

        // The respawn point is inside the water here, which is the worst case: the player is put straight back into it.
        wade.Step(Vector3.Zero, (int)(0.2f / FrameSeconds));
        Check("nothing kills them in the first moments after a respawn", wade.State.Deaths == 1, $"{wade.State.Deaths} deaths");

        wade.Step(Vector3.Zero, (int)(0.3f / FrameSeconds));
        Check("but deadly water does not wait out the long damage invulnerability -- it kills again as soon as the short grace is over",
            wade.State.Deaths >= 2 && ManaGameState.RespawnGraceSeconds < ManaGameState.RespawnInvulnerableSeconds, $"{wade.State.Deaths} deaths");

        // ---------------------------------------------------------------- the death screen
        Check("a drowning gets a wave", ManaUiDeathScreen.Icon(ManaDeathFlavor.Drowned) == "🌊");
        Check("and an enemy an ogre", ManaUiDeathScreen.Icon(ManaDeathFlavor.Enemy) == "👹");
        Check("the cause line names what did it", ManaUiDeathScreen.CauseLine(ManaDeathFlavor.Drowned, "Harbor Water").Contains("Drowned in Harbor Water"));
        Check("an unnamed cause is still a sentence", ManaUiDeathScreen.CauseLine(ManaDeathFlavor.Generic, "").Contains("Killed by something"));
        Check("the quip depends on how you died",
            ManaUiDeathScreen.Quip(ManaDeathFlavor.Drowned, 0) != ManaUiDeathScreen.Quip(ManaDeathFlavor.Fall, 0));
        Check("and changes with each death, so it does not repeat a joke",
            ManaUiDeathScreen.Quip(ManaDeathFlavor.Drowned, 1) != ManaUiDeathScreen.Quip(ManaDeathFlavor.Drowned, 2));
        Check("nobody has a skull tally before they die", ManaUiDeathScreen.Tally(0).Length == 0);
        Check("a few deaths are counted in skulls", ManaUiDeathScreen.Tally(3).StartsWith("💀💀💀"), ManaUiDeathScreen.Tally(3));
        Check("and many are counted in a number", ManaUiDeathScreen.Tally(12).Contains("12") && !ManaUiDeathScreen.Tally(12).StartsWith("💀💀"));

        Check("the red wash is not there before you have died or been hurt", ManaUiDeathScreen.WashOpacity(float.MaxValue, float.MaxValue) == 0.0f);
        Check("it is strong just after a death", ManaUiDeathScreen.WashOpacity(0.1f, float.MaxValue) > 0.4f, $"{ManaUiDeathScreen.WashOpacity(0.1f, float.MaxValue)}");
        Check("and fades", ManaUiDeathScreen.WashOpacity(2.0f, float.MaxValue) < ManaUiDeathScreen.WashOpacity(0.5f, float.MaxValue));
        Check("being merely hurt flashes red too, more faintly",
            ManaUiDeathScreen.WashOpacity(float.MaxValue, 0.0f) is > 0.1f and < 0.4f, $"{ManaUiDeathScreen.WashOpacity(float.MaxValue, 0.0f)}");
        Check("the card fades in", ManaUiDeathScreen.CardOpacity(0.0f) == 0.0f && ManaUiDeathScreen.CardOpacity(0.5f) == 1.0f);
        Check("and out before the screen ends", ManaUiDeathScreen.CardOpacity(ManaGameState.DeathScreenSeconds - 0.1f) < 0.5f
            && ManaUiDeathScreen.CardOpacity(ManaGameState.DeathScreenSeconds + 1.0f) == 0.0f);
        Check("it drops in from above and settles", ManaUiDeathScreen.SlideY(0.0f) < -20.0f && ManaUiDeathScreen.SlideY(1.0f) == 0.0f);
        Check("the skulls arrive one after another",
            ManaUiDeathScreen.SkullOpacity(0, 0.3f) > ManaUiDeathScreen.SkullOpacity(4, 0.3f) && ManaUiDeathScreen.SkullOpacity(4, 1.0f) == 1.0f);
        Check("and the countdown runs from full to empty",
            ManaUiDeathScreen.Countdown(0.0f) == 1.0f && ManaUiDeathScreen.Countdown(ManaGameState.DeathScreenSeconds) == 0.0f);

        var deathState = new ManaGameState();
        var deathRoot = new UiRoot(fonts);
        var deathScreen = ManaUiDeathScreen.Build(deathRoot.Theme, deathState);
        deathRoot.Content = deathScreen;
        deathRoot.UpdateLayout(new Vector2(1280, 720));
        var quiet = deathRoot.Render().Instances.Length;
        deathState.Die("Harbor Water", ManaDeathFlavor.Drowned);
        deathState.Tick(0.6f);
        deathRoot.UpdateLayout(new Vector2(1280, 720));
        var shown = deathRoot.Render().Instances.Length;
        Check("the death screen is nothing at all until you die", quiet == 0, $"{quiet} things drawn");
        Check("then it draws", shown > quiet + 3, $"{shown} things drawn");
        Check("and never takes a click", deathRoot.HitTest(640.0f, 200.0f) == null);
        deathState.Tick(ManaGameState.DeathScreenSeconds);
        deathRoot.UpdateLayout(new Vector2(1280, 720));
        Check("and is gone again afterwards", deathRoot.Render().Instances.Length == 0, $"{deathRoot.Render().Instances.Length} things drawn");

        var flavored = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Deep Water", Kind = ManaLevelSourceKind.Group,
                    Scripts = new List<ManaLevelScript>
                    {
                        new()
                        {
                            Type = HazardComponent.ScriptType,
                            Properties =
                            {
                                ["kills"] = JsonSerializer.SerializeToElement(true),
                                ["flavor"] = JsonSerializer.SerializeToElement("Drowned")
                            }
                        }
                    }
                }
            }
        };
        var flavoredWorld = CreateCourse();
        var flavoredLoad = ManaLevelLoader.Load(flavored, flavoredWorld.Context, flavoredWorld.World.DefaultScene, replaceAuthoredNodes: false);
        Check("a hazard's death flavour is read from the level by name",
            flavoredLoad.Nodes[0].GetComponents<HazardComponent>().FirstOrDefault() is { Flavor: ManaDeathFlavor.Drowned, Kills: true });

        // ---------------------------------------------------------------- spring pads
        var reference = CreateCourse();
        Check("the speed that reaches a height is sqrt(2 g h)",
            MathF.Abs(reference.Player.VelocityForHeight(9.0f) - MathF.Sqrt(2.0f * 64.0f * 9.0f)) < 0.01f);
        // Close to, not equal: a held jump hangs at its top and is topped up for the frame stepping (PawnJumpChecks plays it out).
        Check("a jump leaves at about the speed of a launch to its height",
            MathF.Abs(reference.Player.GetJumpVelocity() / reference.Player.VelocityForHeight(reference.Player.JumpHeight) - 1.0f) < 0.1f);
        Check("four times the height takes twice the speed", MathF.Abs(reference.Player.VelocityForHeight(12.0f) - 2.0f * reference.Player.VelocityForHeight(3.0f)) < 0.01f);

        var spring = CreateCourse();
        var pad = spring.Place(Vector3.Zero, node => new SpringPadComponent(node) { LaunchHeight = 9.0f });
        spring.World.BeginPlay();
        spring.Step(Far);
        Check("a pad does nothing until someone steps on it", pad.LaunchCount == 0);
        spring.Step(Vector3.Zero);
        Check("stepping on it launches", pad.LaunchCount == 1, $"{pad.LaunchCount} launches");
        Check("at the speed that reaches its height, well above an ordinary jump", pad.LastLaunchSpeed > spring.Player.GetJumpVelocity() * 1.5f,
            $"{pad.LastLaunchSpeed:0.0} against a jump's {spring.Player.GetJumpVelocity():0.0}");
        spring.Step(Vector3.Zero, 3);
        Check("and squashes flat as it fires", pad.Owner.Scale.Y < 0.9f, $"{pad.Owner.Scale}");
        Check("without firing again while it recovers", pad.LaunchCount == 1);
        spring.Step(Far, 60);
        Check("and springs back up", MathF.Abs(pad.Owner.Scale.Y - 1.0f) < 0.05f && MathF.Abs(pad.Owner.Scale.X - 1.0f) < 0.05f, $"{pad.Owner.Scale}");
        spring.World.EndPlay();
        Check("stopping play restores its shape", pad.Owner.Scale == Vector3.One);

        // ---------------------------------------------------------------- boost pads
        var boosting = CreateCourse();
        var boostPad = boosting.Place(Vector3.Zero, node => new BoostPadComponent(node) { Multiplier = 1.6f, Seconds = 1.0f });
        boosting.World.BeginPlay();
        Check("nobody is boosted to begin with", !boosting.Player.IsBoosted && boosting.Player.SpeedScale == 1.0f);
        boosting.Step(Vector3.Zero);
        Check("running over a boost pad speeds the player up", boosting.Player.IsBoosted && MathF.Abs(boosting.Player.SpeedScale - 1.6f) < 0.001f,
            $"x{boosting.Player.SpeedScale}");
        Check("and says so", boosting.State.Message.Contains("boost", StringComparison.OrdinalIgnoreCase), boosting.State.Message);
        boosting.Step(Far, (int)(1.3f / FrameSeconds));
        Check("until it wears off", !boosting.Player.IsBoosted && boosting.Player.SpeedScale == 1.0f);

        var stacking = CreateCourse();
        stacking.Player.Boost(1.5f, 2.0f);
        stacking.Player.Boost(1.3f, 5.0f);
        Check("a weaker boost over a stronger one keeps the strength but not the shorter time",
            MathF.Abs(stacking.Player.SpeedScale - 1.5f) < 0.001f);
        stacking.Player.Boost(1.0f, 10.0f);
        stacking.Player.Boost(2.0f, 0.0f);
        Check("and a pad that would not speed you up, or not for any time, is ignored", MathF.Abs(stacking.Player.SpeedScale - 1.5f) < 0.001f);

        // ---------------------------------------------------------------- asking a node for a component by type
        var lookup = CreateCourse();
        var lookupNode = new ManaSceneNode(lookup.Context, Matrix4x4.Identity);
        Check("a node with no such component gives null", lookupNode.GetComponent<TriggerVolumeComponent>() == null
            && !lookupNode.TryGetComponent<TriggerVolumeComponent>(out _));
        var firstTrigger = new TriggerVolumeComponent(lookupNode);
        lookupNode.Components.Add(firstTrigger);
        lookupNode.Components.Add(new TriggerVolumeComponent(lookupNode));
        Check("it gives the first of that type", ReferenceEquals(lookupNode.GetComponent<TriggerVolumeComponent>(), firstTrigger)
            && lookupNode.TryGetComponent<TriggerVolumeComponent>(out var foundTrigger) && ReferenceEquals(foundTrigger, firstTrigger));
        Check("and finds one by a base type too", lookupNode.GetComponent<ManaComponent>() != null);

        // ---------------------------------------------------------------- pointing at another component on the same node
        var pointing = CreateCourse();
        var pointNode = new ManaSceneNode(pointing.Context, Matrix4x4.Identity) { Name = "Two Volumes" };
        var volumeA = new TriggerVolumeComponent(pointNode) { Size = new Vector3(2.0f, 2.0f, 2.0f) };
        var volumeB = new TriggerVolumeComponent(pointNode) { Size = new Vector3(2.0f, 2.0f, 2.0f), Offset = new Vector3(20.0f, 0.0f, 0.0f) };
        pointNode.Components.Add(volumeA);
        pointNode.Components.Add(volumeB);
        pointing.World.DefaultScene.Children.Add(pointNode);

        Check("a reference to the first of its kind is just the type name", ManaComponentReference.To(volumeA).Key == "TriggerVolumeComponent");
        Check("and to a later one carries its position among that kind", ManaComponentReference.To(volumeB).Key == "TriggerVolumeComponent#1");
        Check("it resolves back to the very component",
            ReferenceEquals(ManaComponentReference.To(volumeB).Resolve<TriggerVolumeComponent>(pointNode), volumeB)
            && ReferenceEquals(pointNode.GetComponent<TriggerVolumeComponent>(ManaComponentReference.To(volumeA)), volumeA));
        Check("a reference to nothing resolves to nothing", ManaComponentReference.None.Resolve<TriggerVolumeComponent>(pointNode) == null && !ManaComponentReference.None.IsSet);
        Check("as does one whose target is gone",
            new ManaComponentReference("TriggerVolumeComponent#5").Resolve<TriggerVolumeComponent>(pointNode) == null
            && new ManaComponentReference("NoSuchComponent").Resolve<TriggerVolumeComponent>(pointNode) == null);
        Check("or is not the kind asked for", ManaComponentReference.To(volumeA).Resolve<HazardComponent>(pointNode) == null);

        // A hazard on that node listens to the first volume unless told otherwise.
        var choosy = new HazardComponent(pointNode) { Kills = true };
        pointNode.Components.Add(choosy);
        Check("a piece listens to the first volume by default", ReferenceEquals(choosy.Trigger, volumeA));
        choosy.TriggerSource = ManaComponentReference.To(volumeB);
        Check("and to another when pointed at it", ReferenceEquals(choosy.Trigger, volumeB));

        pointing.World.BeginPlay();
        pointing.Step(Vector3.Zero);
        Check("so standing in the first volume does nothing to it", pointing.State.Deaths == 0, $"{pointing.State.Deaths} deaths");
        pointing.Step(new Vector3(20.0f, 0.0f, 0.0f));
        Check("while standing in the one it is pointed at does", pointing.State.Deaths == 1, $"{pointing.State.Deaths} deaths");
        pointing.World.EndPlay();

        // saved and loaded, and left out when it points at nothing
        var pointSaved = ManaLevelCapture.CaptureScene(pointing.World, pointing.World.DefaultScene, "Pointing");
        pointNode.LevelSource = ManaLevelSource.Group;
        pointSaved = ManaLevelCapture.CaptureScene(pointing.World, pointing.World.DefaultScene, "Pointing");
        var pointScripts = pointSaved.Nodes.Single(node => node.Name == "Two Volumes").Scripts!;
        var hazardScript = pointScripts.Single(script => script.Type == HazardComponent.ScriptType);
        Check("the choice is saved with the piece, by name", hazardScript.Properties.TryGetValue("triggerSource", out var savedSource)
            && savedSource.GetString() == "TriggerVolumeComponent#1", string.Join(",", hazardScript.Properties.Keys));

        var reloadWorld = CreateCourse();
        var reloadNode = ManaLevelLoader.Load(ManaLevel.FromJson(pointSaved.ToJson()), reloadWorld.Context, reloadWorld.World.DefaultScene, replaceAuthoredNodes: false).Nodes[0];
        var reloadedHazard = reloadNode.GetComponent<HazardComponent>()!;
        var reloadedVolumes = reloadNode.GetComponents<TriggerVolumeComponent>();
        Check("and read back pointing at the same volume", reloadedVolumes.Count == 2 && ReferenceEquals(reloadedHazard.Trigger, reloadedVolumes[1]),
            $"{reloadedVolumes.Count} volumes");

        var plainCourse = CreateCourse();
        plainCourse.Place(Vector3.Zero, node => { node.Name = "Plain Coin"; node.LevelSource = ManaLevelSource.Group; return new CollectibleComponent(node); });
        var plainScripts = ManaLevelCapture.CaptureScene(plainCourse.World, plainCourse.World.DefaultScene, "Plain").Nodes.Single(node => node.Name == "Plain Coin").Scripts!;
        Check("a piece that points at nothing does not save the property at all",
            !plainScripts.Single(script => script.Type == CollectibleComponent.ScriptType).Properties.ContainsKey("triggerSource"));

        // ---------------------------------------------------------------- naming components
        var naming = CreateCourse();
        var nameNode = new ManaSceneNode(naming.Context, Matrix4x4.Identity) { Name = "Doors" };
        var front = new TriggerVolumeComponent(nameNode) { Label = "Front Door", Size = new Vector3(2.0f, 2.0f, 2.0f) };
        var back = new TriggerVolumeComponent(nameNode) { Label = "Back Door", Size = new Vector3(2.0f, 2.0f, 2.0f), Offset = new Vector3(20.0f, 0.0f, 0.0f) };
        nameNode.Components.Add(front);
        nameNode.Components.Add(back);
        naming.World.DefaultScene.Children.Add(nameNode);

        Check("a component has no name until it is given one", new TriggerVolumeComponent(nameNode).Label == null);
        Check("a reference to a named component uses its name", ManaComponentReference.To(back).Key == "@Back Door");
        Check("and finds it by that name", ReferenceEquals(nameNode.GetComponent<TriggerVolumeComponent>(ManaComponentReference.To(back)), back));
        var backReference = ManaComponentReference.To(back);
        nameNode.Components.Reverse();
        Check("even after the components have been reordered", ReferenceEquals(backReference.Resolve<TriggerVolumeComponent>(nameNode), back));
        Check("where a reference by position would now point at the other one",
            !ReferenceEquals(new ManaComponentReference("TriggerVolumeComponent#1").Resolve<TriggerVolumeComponent>(nameNode), back));
        nameNode.Components.Reverse();
        Check("a name nobody has is not found", new ManaComponentReference("@Side Door").Resolve<TriggerVolumeComponent>(nameNode) == null);

        var picky = new HazardComponent(nameNode) { Kills = true, TriggerSource = ManaComponentReference.To(back) };
        nameNode.Components.Add(picky);
        Check("and an unnamed component is still pointed at by its kind", ManaComponentReference.To(picky).Key == "HazardComponent");
        nameNode.LevelSource = ManaLevelSource.Group;
        var namedSave = ManaLevelCapture.CaptureScene(naming.World, naming.World.DefaultScene, "Doors").Nodes.Single(node => node.Name == "Doors");
        var savedTriggers = namedSave.Scripts!.Where(script => script.Type == TriggerVolumeComponent.ScriptType).ToList();
        Check("names are saved with their scripts", savedTriggers.Select(script => script.Name).SequenceEqual(new[] { "Front Door", "Back Door" }),
            string.Join(",", savedTriggers.Select(script => script.Name)));
        Check("and a piece's choice is saved by name", namedSave.Scripts!.Single(script => script.Type == HazardComponent.ScriptType).Properties["triggerSource"].GetString() == "@Back Door");

        var namedWorld = CreateCourse();
        var namedNode = ManaLevelLoader.Load(ManaLevel.FromJson(new ManaLevel { Nodes = { namedSave } }.ToJson()), namedWorld.Context, namedWorld.World.DefaultScene, replaceAuthoredNodes: false).Nodes[0];
        var namedTriggers = namedNode.GetComponents<TriggerVolumeComponent>();
        Check("both come back with their names", namedTriggers.Select(trigger => trigger.Label).SequenceEqual(new[] { "Front Door", "Back Door" }),
            string.Join(",", namedTriggers.Select(trigger => trigger.Label)));
        Check("and the piece still listens to the one it was pointed at", ReferenceEquals(namedNode.GetComponent<HazardComponent>()!.Trigger, namedTriggers[1]));

        var unnamedSave = ManaLevelCapture.CaptureScene(plainCourse.World, plainCourse.World.DefaultScene, "Unnamed").Nodes.Single(node => node.Name == "Plain Coin");
        Check("a component with no name saves none", unnamedSave.Scripts!.All(script => script.Name == null));

        // ---------------------------------------------------------------- renaming a component keeps what points at it pointing at it
        var renaming = CreateCourse();
        var renameNode = new ManaSceneNode(renaming.Context, Matrix4x4.Identity) { Name = "Rename" };
        var firstVolume = new TriggerVolumeComponent(renameNode);
        var secondVolume = new TriggerVolumeComponent(renameNode);
        renameNode.Components.Add(firstVolume);
        renameNode.Components.Add(secondVolume);
        var follower = new HazardComponent(renameNode) { TriggerSource = ManaComponentReference.To(secondVolume) };
        renameNode.Components.Add(follower);
        Check("a piece pointed at an unnamed volume records it by position", follower.TriggerSource.Key == "TriggerVolumeComponent#1");

        ManaComponentReference.Rename(secondVolume, "Back");
        Check("naming that volume moves the piece's pointer to the name", follower.TriggerSource.Key == "@Back" && ReferenceEquals(follower.Trigger, secondVolume),
            follower.TriggerSource.Key ?? "");
        ManaComponentReference.Rename(secondVolume, "Rear");
        Check("and so does renaming it again", follower.TriggerSource.Key == "@Rear" && ReferenceEquals(follower.Trigger, secondVolume));
        ManaComponentReference.Rename(secondVolume, "  ");
        Check("clearing the name goes back to pointing by position", follower.TriggerSource.Key == "TriggerVolumeComponent#1" && secondVolume.Label == null
            && ReferenceEquals(follower.Trigger, secondVolume), follower.TriggerSource.Key ?? "");
        ManaComponentReference.Rename(firstVolume, "Front");
        Check("renaming a volume nothing points at leaves the others alone", follower.TriggerSource.Key == "TriggerVolumeComponent#1" && firstVolume.Label == "Front");

        // ---------------------------------------------------------------- the trigger volume is its own component
        var split = CreateCourse();
        var splitHazard = split.Place(Vector3.Zero, node => new HazardComponent(node));
        var splitTrigger = splitHazard.Owner.Components.OfType<TriggerVolumeComponent>().ToList();
        Check("a piece gets a trigger volume component of its own on the node", splitTrigger.Count == 1);
        Check("and is not itself a trigger: it listens to one", !typeof(TriggerVolumeComponent).IsAssignableFrom(typeof(HazardComponent))
            && ReferenceEquals(splitHazard.Trigger, splitTrigger[0]));
        var second = new CollectibleComponent(splitHazard.Owner);
        splitHazard.Owner.Components.Add(second);
        Check("two pieces on one node share the one volume", splitHazard.Owner.Components.OfType<TriggerVolumeComponent>().Count() == 1
            && ReferenceEquals(second.Trigger, splitHazard.Trigger));
        Check("the volume carries the piece's defaults, and the piece reads and writes it through",
            splitHazard.Trigger.Size == new Vector3(2.0f, 2.0f, 2.0f) && splitHazard.Size == splitHazard.Trigger.Size);
        splitHazard.Trigger.Size = new Vector3(5.0f, 1.0f, 5.0f);
        Check("changing the trigger changes what the piece sees", splitHazard.Size == new Vector3(5.0f, 1.0f, 5.0f));

        // anything can listen -- it does not have to be a course piece
        var heard = new List<string>();
        var eavesdrop = CreateCourse();
        var bareNode = new ManaSceneNode(eavesdrop.Context, Matrix4x4.Identity) { Name = "Door" };
        var voice = new TriggerVolumeComponent(bareNode) { Size = new Vector3(2.0f, 2.0f, 2.0f) };
        bareNode.Components.Add(voice);
        voice.PlayerEntered += (_, _) => heard.Add("enter");
        voice.PlayerStayed += (_, _) => heard.Add("stay");
        voice.PlayerExited += (_, _) => heard.Add("exit");
        eavesdrop.World.DefaultScene.Children.Add(bareNode);
        eavesdrop.World.BeginPlay();
        eavesdrop.Step(Vector3.Zero);
        eavesdrop.Step(Vector3.Zero);
        eavesdrop.Step(Far);
        Check("a trigger tells anyone listening when the player enters, stays and leaves",
            string.Join(",", heard) == "enter,stay,stay,exit", string.Join(",", heard));

        // and a listener lets go when play ends
        var stopping = CreateCourse();
        var stoppingCoin = stopping.Place(Vector3.Zero, node => new CollectibleComponent(node));
        stopping.World.BeginPlay();
        stopping.World.EndPlay();
        stopping.World.BeginPlay();
        stopping.World.EndPlay();
        stopping.State.Reset();
        stopping.World.BeginPlay();
        stopping.Step(Vector3.Zero);
        Check("playing again does not subscribe a piece twice", stopping.State.Coins == 1, $"{stopping.State.Coins} coins");
        stopping.World.EndPlay();
        var coinsBefore = stopping.State.Coins;
        stopping.Step(Far);
        stopping.Step(Vector3.Zero);
        Check("and once play has ended a trigger does nothing to it", stopping.State.Coins == coinsBefore);

        // a level saved before volumes had a component of their own still loads
        var legacyLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Old Coin", Kind = ManaLevelSourceKind.Group,
                    Scripts = new List<ManaLevelScript>
                    {
                        new()
                        {
                            Type = CollectibleComponent.ScriptType,
                            Properties =
                            {
                                ["value"] = JsonSerializer.SerializeToElement(3),
                                ["size"] = JsonSerializer.SerializeToElement(new[] { 4.0f, 5.0f, 6.0f }),
                                ["offset"] = JsonSerializer.SerializeToElement(new[] { 0.0f, 1.5f, 0.0f })
                            }
                        }
                    }
                }
            }
        };
        var legacyWorld = CreateCourse();
        var legacyNode = ManaLevelLoader.Load(legacyLevel, legacyWorld.Context, legacyWorld.World.DefaultScene, replaceAuthoredNodes: false).Nodes[0];
        var legacyCoin = legacyNode.GetComponents<CollectibleComponent>().Single();
        Check("an older level's size and offset land on the trigger", legacyCoin.Trigger.Size == new Vector3(4.0f, 5.0f, 6.0f) && legacyCoin.Trigger.Offset == new Vector3(0.0f, 1.5f, 0.0f) && legacyCoin.Value == 3,
            $"{legacyCoin.Trigger.Size} {legacyCoin.Trigger.Offset}");

        // and a new save keeps the volume on its own script, out of the piece's
        var newSave = ManaLevelCapture.CaptureScene(legacyWorld.World, legacyWorld.World.DefaultScene, "Legacy").Nodes.Single(node => node.Name == "Old Coin");
        var savedPiece = newSave.Scripts!.Single(script => script.Type == CollectibleComponent.ScriptType);
        var savedVolume = newSave.Scripts!.Single(script => script.Type == TriggerVolumeComponent.ScriptType);
        Check("a saved piece has no volume of its own, and its trigger script does",
            !savedPiece.Properties.ContainsKey("size") && !savedPiece.Properties.ContainsKey("offset") && savedVolume.Properties.ContainsKey("size") && savedVolume.Properties.ContainsKey("offset"),
            string.Join(",", savedPiece.Properties.Keys));

        // ---------------------------------------------------------------- what a shape's size means
        // The generator that built the sample course assumed half-extents and every cube came out half the size it
        // was meant to be, with hazard volumes (sized as intended) hanging out past them. Full extent is the
        // convention -- it is what Bepu's Box, the level's collision size and every other bundled level use.
        Vector3 ExtentOf(ManaMesh mesh)
        {
            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);
            foreach (var position in mesh.GetOrCreateCpuData().Positions)
            {
                low = Vector3.Min(low, position);
                high = Vector3.Max(high, position);
            }

            return high - low;
        }

        var extentCourse = CreateCourse();
        var cubeExtent = ExtentOf(new ManaCube(extentCourse.Context, Vector4.One, new Vector3(13.5f, 0.05f, 11.0f)));
        Check("a cube is as big as the size it is given, across", MathF.Abs(cubeExtent.X - 13.5f) < 0.01f && MathF.Abs(cubeExtent.Z - 11.0f) < 0.01f, $"{cubeExtent}");
        var diskExtent = ExtentOf(new ManaDisk(extentCourse.Context, Vector4.One, new Vector3(2.4f, 0.3f, 2.4f)));
        Check("and a disk is as wide as its size", MathF.Abs(diskExtent.X - 2.4f) < 0.1f, $"{diskExtent}");

        // ---------------------------------------------------------------- water nodes made in the editor
        var pool = CreateCourse();
        var water = new ManaWaterNode(pool.Context, new Vector2(16.0f, 16.0f), 64)
        {
            LevelSource = ManaLevelSource.ForWater(new Vector2(16.0f, 16.0f), 64)
        };
        pool.World.DefaultScene.Children.Add(water);
        var mesh = water.Mesh;
        water.Resize(new Vector2(40.0f, 24.0f), 96);
        Check("a water node can be resized", water.Mesh.Size == new Vector2(40.0f, 24.0f) && water.Mesh.Resolution == 96 && !ReferenceEquals(water.Mesh, mesh),
            $"{water.Mesh.Size}, {water.Mesh.Resolution}");
        Check("the renderer draws the new surface", ReferenceEquals(water.MeshRenderer.Mesh, water.Mesh));
        water.Resize(new Vector2(-3.0f, 0.0f), 100000);
        Check("and is kept to sensible limits", water.Mesh.Size.X >= 0.5f && water.Mesh.Size.Y >= 0.5f && water.Mesh.Resolution == ManaWaterNode.MaximumResolution,
            $"{water.Mesh.Size}, {water.Mesh.Resolution}");
        water.Resize(new Vector2(40.0f, 24.0f), 96);
        var savedWater = ManaLevelCapture.CaptureScene(pool.World, pool.World.DefaultScene, "Pool").Nodes.Single();
        Check("what is saved is the size it was resized to",
            savedWater.Kind == ManaLevelSourceKind.Water && savedWater.Water is { Resolution: 96 } saved && saved.Size[0] == 40.0f && saved.Size[1] == 24.0f,
            $"{savedWater.Kind} {savedWater.Water?.Size[0]}x{savedWater.Water?.Size[1]}");

        // ---------------------------------------------------------------- the sample course is safe to respawn into
        var courseFile = Path.Combine(AppContext.BaseDirectory, "Content", "Levels", "Sample Course.manalevel");
        if (!File.Exists(courseFile))
        {
            courseFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Content", "Levels", "Sample Course.manalevel"));
        }

        if (File.Exists(courseFile))
        {
            var course = ManaLevel.Load(courseFile);
            var courseNodes = LevelWalk.Flatten(course.Nodes);
            var courseHazards = courseNodes
                .Where(node => node.Scripts?.Any(script => script.Type == HazardComponent.ScriptType) == true)
                .Select(node =>
                {
                    var script = node.Scripts!.First(candidate => candidate.Type == HazardComponent.ScriptType);
                    var (size, offset) = VolumeOf(node);
                    var world = Matrix4x4.CreateRotationY(node.Rotation[1]) * Matrix4x4.CreateTranslation(node.Position[0], node.Position[1], node.Position[2]);
                    return (node.Name, world, size, offset);
                })
                .ToList();

            var respawnPoints = courseNodes
                .Where(node => node.Kind == ManaLevelSourceKind.PlayerStart
                    || node.Scripts?.Any(script => script.Type is CheckpointComponent.ScriptType or SpringPadComponent.ScriptType or BoostPadComponent.ScriptType) == true)
                .Select(node => (node.Name, feet: new Vector3(node.Position[0], node.Position[1] + 0.5f, node.Position[2])))
                .ToList();

            // A hazard's volume should be the size of the thing that looks dangerous: a volume hanging out past the oil
            // (or short of it) is damage from nothing, or a puddle you can walk through unhurt.
            var mismatched = new List<string>();
            foreach (var node in courseNodes.Where(node => node.Kind == ManaLevelSourceKind.Shape && node.Size is { Length: 3 }
                && node.Scripts?.Any(script => script.Type == HazardComponent.ScriptType) == true))
            {
                var volume = VolumeOf(node).Size;
                if (MathF.Abs(volume.X - node.Size![0]) > 0.5f || MathF.Abs(volume.Z - node.Size![2]) > 0.5f)
                {
                    mismatched.Add($"{node.Name}: looks {node.Size[0]}x{node.Size[2]}, hurts over {volume.X}x{volume.Z}");
                }
            }

            Check("every hazard's volume matches the shape it sits on", mismatched.Count == 0, string.Join("; ", mismatched));

            // The course is organised Section > Category > item, and grouping it must not have moved anything.
            var sectionGroups = course.Nodes.Where(node => node.Kind == ManaLevelSourceKind.Group).ToList();
            Check("the course is grouped into sections at the scene offset the other levels use",
                sectionGroups.Count >= 5 && course.Nodes.All(node => node.Kind == ManaLevelSourceKind.Group
                    && node.Position[0] >= 32.0f && node.Position[2] >= 32.0f && node.Children is { Count: > 0 }),
                $"{course.Nodes.Count} top-level nodes");
            Check("with categories inside each section", sectionGroups.All(section => section.Children!.All(child => child.Kind == ManaLevelSourceKind.Group)),
                string.Join(", ", sectionGroups.Take(3).Select(section => section.Name)));
            var startNode = courseNodes.FirstOrDefault(node => node.Kind == ManaLevelSourceKind.PlayerStart);
            Check("and the player still starts where the course begins", startNode != null && MathF.Abs(startNode.Position[0] - 38.0f) < 0.01f && MathF.Abs(startNode.Position[2] - 40.0f) < 0.01f,
                $"{startNode?.Position[0]}, {startNode?.Position[2]}");
            Check("with nothing lost in the move", courseNodes.Count(node => node.Scripts?.Any(script => script.Type == CollectibleComponent.ScriptType) == true) == 27, "27 coins");

            var unsafeSpots = new List<string>();
            foreach (var (spot, feet) in respawnPoints)
            {
                foreach (var (hazard, world, size, offset) in courseHazards)
                {
                    if (TriggerVolumeComponent.Contains(world, offset, size, feet, TriggerVolumeComponent.PlayerRadius, ManaPawnNode.ReferenceHeight))
                    {
                        unsafeSpots.Add($"{spot} is inside {hazard}");
                    }
                }
            }

            Check("nobody respawns inside a hazard, and no pad sits in one: the start, the checkpoints and the pads are clear of them",
                respawnPoints.Count >= 5 && courseHazards.Count >= 3 && unsafeSpots.Count == 0,
                $"{respawnPoints.Count} respawn points, {courseHazards.Count} hazards; {string.Join("; ", unsafeSpots)}");
        }

        // ---------------------------------------------------------------- the shipped sample level
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Content", "Levels", "Sample Course.manalevel");
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Content", "Levels", "Sample Course.manalevel"));
        var levelFile = File.Exists(samplePath) ? samplePath : sourcePath;
        if (File.Exists(levelFile))
        {
            var sample = ManaLevel.Load(levelFile);
            var types = LevelWalk.Flatten(sample.Nodes).SelectMany(node => node.Scripts ?? new List<ManaLevelScript>()).Select(script => script.Type).ToList();
            Check("the sample course has coins, hazards, checkpoints, a goal, enemies and moving platforms",
                types.Count(type => type == CollectibleComponent.ScriptType) >= 20
                && types.Count(type => type == CheckpointComponent.ScriptType) >= 2
                && types.Count(type => type == MovingPlatformComponent.ScriptType) >= 2
                && types.Count(type => type == EnemyComponent.ScriptType) >= 3
                && types.Contains(HazardComponent.ScriptType)
                && types.Contains(SpringPadComponent.ScriptType)
                && types.Contains(BoostPadComponent.ScriptType)
                && types.Contains(CheckpointComponent.ScriptType)
                && types.Contains(GoalComponent.ScriptType)
                && types.Contains(EnemyComponent.ScriptType)
                && types.Contains(MovingPlatformComponent.ScriptType),
                string.Join(", ", types));
        }
    }
}
