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
            ManaTriggerComponent.Contains(world, offset, box, feet, 0.4f, 2.0f);

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
            ManaTriggerComponent.Contains(turned, Vector3.Zero, thin, new Vector3(8.0f, 0.0f, 0.0f), 0.4f, 2.0f)
            && !ManaTriggerComponent.Contains(turned, Vector3.Zero, thin, new Vector3(0.0f, 0.0f, 8.0f), 0.4f, 2.0f));

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
            hud.FormatHud() == "Coins 1/3   Health 3/3   Time 1:15.5", hud.FormatHud());

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
        var builtTypes = builtLevel.Nodes.SelectMany(node => node.Scripts ?? new List<ManaLevelScript>()).Select(script => script.Type).ToList();
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
        Check("an animated enemy walks with its walk clip", actor.Animation.ActiveClip?.Name == "walk", actor.Animation.ActiveClip?.Name);

        animated.Step(new Vector3(actor.Position.X + 3.0f, 0.0f, 0.0f), 20);
        Check("sprints when it chases", orcy.IsChasing && actor.Animation.ActiveClip?.Name == "sprint", actor.Animation.ActiveClip?.Name);

        animated.Player.GroundHeight = 1.4f;
        animated.Step(new Vector3(actor.Position.X, 1.4f, 0.0f));
        Check("stomping it plays its die clip", orcy.IsDefeated && actor.Animation.ActiveClip?.Name == "die" && !actor.Animation.Looping,
            actor.Animation.ActiveClip?.Name);
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
        Check("a killing hazard says so", drowned.State.Message == "Killed by Harbor Water", drowned.State.Message);

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
        Check("falling out of the world says that", fell.State.Message == "Fell out of the world", fell.State.Message);

        fell.Step(Far, (int)((ManaGameState.CauseMessageSeconds + 0.5f) / FrameSeconds));
        Check("and the message goes again once it has been read", fell.State.Message.Length == 0, fell.State.Message);

        var stomped = CreateCourse();
        var thug = stomped.Place(new Vector3(0.0f, 0.6f, 0.0f), node => { node.Name = "Yard Hog"; return new EnemyComponent(node) { Travel = Vector3.Zero }; });
        stomped.World.BeginPlay();
        stomped.Player.GroundHeight = 0.0f;
        stomped.Step(new Vector3(0.0f, 0.0f, 0.0f));
        Check("an enemy is named when it hurts", stomped.State.Message.Contains("Hurt by Yard Hog"), stomped.State.Message);

        // ---------------------------------------------------------------- the sample course is safe to respawn into
        var courseFile = Path.Combine(AppContext.BaseDirectory, "Levels", "Sample Course.manalevel");
        if (!File.Exists(courseFile))
        {
            courseFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Levels", "Sample Course.manalevel"));
        }

        if (File.Exists(courseFile))
        {
            var course = ManaLevel.Load(courseFile);
            var courseHazards = course.Nodes
                .Where(node => node.Scripts?.Any(script => script.Type == HazardComponent.ScriptType) == true)
                .Select(node =>
                {
                    var script = node.Scripts!.First(candidate => candidate.Type == HazardComponent.ScriptType);
                    var size = ReadVector(script.Properties["size"]);
                    var offset = script.Properties.TryGetValue("offset", out var offsetValue)
                        ? ReadVector(offsetValue) : Vector3.Zero;
                    var world = Matrix4x4.CreateRotationY(node.Rotation[1]) * Matrix4x4.CreateTranslation(node.Position[0], node.Position[1], node.Position[2]);
                    return (node.Name, world, size, offset);
                })
                .ToList();

            var respawnPoints = course.Nodes
                .Where(node => node.Kind == ManaLevelSourceKind.PlayerStart
                    || node.Scripts?.Any(script => script.Type == CheckpointComponent.ScriptType) == true)
                .Select(node => (node.Name, feet: new Vector3(node.Position[0], node.Position[1] + 0.5f, node.Position[2])))
                .ToList();

            var unsafeSpots = new List<string>();
            foreach (var (spot, feet) in respawnPoints)
            {
                foreach (var (hazard, world, size, offset) in courseHazards)
                {
                    if (ManaTriggerComponent.Contains(world, offset, size, feet, ManaTriggerComponent.PlayerRadius, ManaPawnNode.ReferenceHeight))
                    {
                        unsafeSpots.Add($"{spot} is inside {hazard}");
                    }
                }
            }

            Check("nobody respawns inside a hazard: the start and every checkpoint are clear of them",
                respawnPoints.Count >= 3 && courseHazards.Count >= 3 && unsafeSpots.Count == 0,
                $"{respawnPoints.Count} respawn points, {courseHazards.Count} hazards; {string.Join("; ", unsafeSpots)}");
        }

        // ---------------------------------------------------------------- the shipped sample level
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Levels", "Sample Course.manalevel");
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Levels", "Sample Course.manalevel"));
        var levelFile = File.Exists(samplePath) ? samplePath : sourcePath;
        if (File.Exists(levelFile))
        {
            var sample = ManaLevel.Load(levelFile);
            var types = sample.Nodes.SelectMany(node => node.Scripts ?? new List<ManaLevelScript>()).Select(script => script.Type).ToList();
            Check("the sample course has coins, hazards, checkpoints, a goal, enemies and moving platforms",
                types.Count(type => type == CollectibleComponent.ScriptType) >= 20
                && types.Count(type => type == CheckpointComponent.ScriptType) >= 2
                && types.Count(type => type == MovingPlatformComponent.ScriptType) >= 2
                && types.Count(type => type == EnemyComponent.ScriptType) >= 3
                && types.Contains(HazardComponent.ScriptType)
                && types.Contains(CheckpointComponent.ScriptType)
                && types.Contains(GoalComponent.ScriptType)
                && types.Contains(EnemyComponent.ScriptType)
                && types.Contains(MovingPlatformComponent.ScriptType),
                string.Join(", ", types));
        }
    }
}
