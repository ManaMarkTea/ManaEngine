using Mana;
using Mana.Code;
using Mana.Code.Audio;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaAudio;
using ManaCore.Audio;
using ManaUI;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class CourseAudioChecks
{
    private sealed class Recorder : IManaAudioBackend
    {
        public readonly List<ManaAudioPlayOptions> Starts = new();
        public readonly HashSet<int> Live = new();
        public readonly Dictionary<int, float> Gains = new();
        public bool IsAvailable => true;
        public bool IsUnlocked => true;
        public int Play(ManaAudioClip clip, ManaAudioPlayOptions options)
        {
            Starts.Add(options); int id = Starts.Count; Live.Add(id); Gains[id] = options.Volume; return id;
        }
        public bool IsPlaying(int voice) => Live.Contains(voice);
        public void Configure(int voice, float gain, float pitch, Vector3? position) => Gains[voice] = gain;
        public void Stop(int voice) => Live.Remove(voice);
        public void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }
        public void Dispose() => Live.Clear();
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool value) => check(name, value, "");
        var recording = new Recorder();
        using var audio = new ManaAudioSystem(recording);
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { Audio = audio, DeferGpuUploads = true };
        world.Context = context;
        world.DefaultScene = new ManaScene(context);
        world.Scenes.Add(world.DefaultScene);
        var player = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false };
        player.Components.Add(new ManaPawnAudioComponent(player));
        world.GameState.Player = player;
        world.DefaultScene.Children.Add(player);
        ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(0.05);

        ManaSceneNode Node(string name)
        {
            var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name, LevelSource = ManaLevelSource.Group };
            world.DefaultScene.Children.Add(node);
            return node;
        }
        ManaAudioSourceComponent Source(ManaSceneNode node, ManaSoundCue cue, bool global = false)
        {
            var source = new ManaAudioSourceComponent(node)
            {
                Clip = ManaAudioDiagnostics.TestTone, Cue = cue, Global = global, Spatial = false, PlayOnBegin = false
            };
            node.Components.Add(source);
            return source;
        }
        var profile = Node("Profile");
        Source(profile, ManaSoundCue.Footstep, true);
        Source(profile, ManaSoundCue.Jump, true);
        Source(profile, ManaSoundCue.Land, true);
        var hurt = Source(profile, ManaSoundCue.Hurt, true);
        Source(profile, ManaSoundCue.LowHealth, true);
        Source(profile, ManaSoundCue.Death, true);
        var respawn = Source(profile, ManaSoundCue.Respawn, true); respawn.DelaySeconds = 0.1f;
        Source(profile, ManaSoundCue.Goal, true);
        Source(profile, ManaSoundCue.AllCoins, true);
        var coin = Node("Coin");
        var pickup = new CollectibleComponent(coin); coin.Components.Add(pickup);
        Source(coin, ManaSoundCue.Pickup);
        var enemy = Node("Enemy");
        var enemyLogic = new EnemyComponent(enemy) { Travel = Vector3.UnitX, ChaseRadius = 8, Offset = new Vector3(0, 0.7f, 0) };
        enemy.Components.Add(enemyLogic);
        Source(enemy, ManaSoundCue.EnemyAlert);
        Source(enemy, ManaSoundCue.EnemyStomp);
        var platform = Node("Platform");
        var platformLogic = new MovingPlatformComponent(platform) { Travel = Vector3.UnitX, Speed = 1, PauseSeconds = 1 };
        platform.Components.Add(platformLogic);
        var motor = Source(platform, ManaSoundCue.PlatformMove); motor.Loop = true; motor.FadeSeconds = 0.1f;
        Source(platform, ManaSoundCue.PlatformStop);

        world.AudioCues.Emit(ManaSoundCue.Hurt);
        ManaAudioCueRouter.EmitAt(coin, ManaSoundCue.Pickup);
        Check("cues are silent in edit mode", recording.Starts.Count == 0);
        world.BeginPlay();
        // Overlap is noticed by the node's trigger volume, which is what tells the coin.
        pickup.Trigger.UpdateComponent(); pickup.Trigger.UpdateComponent();
        Check("one coin emits one pickup and one all-coins cue", recording.Starts.Count == 2);
        int before = recording.Starts.Count;
        player.DriveDirection = Vector3.UnitX;
        for (int frame = 0; frame < 30 && recording.Starts.Count == before; frame++) { player.UpdateTransform(); }
        Check("the component emits footsteps through the node update", recording.Starts.Count == before + 1);
        player.DriveDirection = Vector3.Zero;
        player.Position = Vector3.Zero;
        player.UpdateTransform();
        before = recording.Starts.Count;
        world.GameState.Damage(1); world.GameState.Damage(1);
        Check("invulnerability prevents repeated hurt sounds", recording.Starts.Count == before + 1);
        world.GameState.Tick(2);
        before = recording.Starts.Count;
        world.GameState.Damage(1);
        Check("last health emits hurt and low-health once", recording.Starts.Count == before + 2);
        world.GameState.Die();
        before = recording.Starts.Count;
        respawn.UpdateComponent();
        Check("respawn cue waits its configured delay", recording.Starts.Count == before);
        respawn.UpdateComponent();
        Check("respawn cue plays after its delay", recording.Starts.Count == before + 1);

        before = recording.Starts.Count;
        platformLogic.UpdateComponent(); motor.UpdateComponent();
        Check("moving platform starts one motor loop", recording.Starts.Count == before + 1 && motor.Voice is { IsPlaying: true });
        for (int i = 0; i < 5; i++) { platformLogic.UpdateComponent(); motor.UpdateComponent(); }
        Check("motor does not retrigger every frame", recording.Starts.Count == before + 1);
        for (int i = 0; i < 24; i++) { platformLogic.UpdateComponent(); motor.UpdateComponent(); }
        Check("endpoint emits one clunk and fades the motor out", recording.Starts.Count == before + 2 && motor.Voice == null);

        player.Position = new Vector3(3, 0, 0);
        player.UpdateTransform();
        before = recording.Starts.Count;
        enemyLogic.UpdateComponent(); enemyLogic.UpdateComponent();
        Check("chase transition emits one alert", recording.Starts.Count == before + 1);
        player.GroundHeight = 1.3f;
        player.Position = new Vector3(enemy.Position.X, 1.3f, enemy.Position.Z);
        player.UpdateTransform();
        before = recording.Starts.Count;
        enemyLogic.UpdateComponent(); enemyLogic.Trigger.UpdateComponent(); enemyLogic.UpdateComponent(); enemyLogic.Trigger.UpdateComponent();
        Check("enemy stomp sound happens once", enemyLogic.IsDefeated && recording.Starts.Count == before + 1);

        world.GameState.Win();
        before = recording.Starts.Count;
        world.GameState.Win();
        Check("winning does not retrigger the goal cue", recording.Starts.Count == before);
        world.GameState.Die(); // Leaves a delayed respawn pending for teardown.
        world.EndPlay();
        before = recording.Starts.Count;
        respawn.UpdateComponent(); respawn.UpdateComponent();
        world.AudioCues.Emit(ManaSoundCue.Hurt);
        Check("EndPlay clears voices, subscriptions and delayed cues", recording.Live.Count == 0 && recording.Starts.Count == before);
        world.BeginPlay();
        before = recording.Starts.Count;
        world.AudioCues.Emit(ManaSoundCue.Hurt);
        Check("restarting does not duplicate subscriptions", recording.Starts.Count == before + 1);
        SceneEditorOperations.RemoveComponent(null, hurt);
        before = recording.Starts.Count;
        world.AudioCues.Emit(ManaSoundCue.Hurt);
        Check("removing a source unregisters it", recording.Starts.Count == before);
        var pawnAudio = player.GetComponent<ManaPawnAudioComponent>()!;
        SceneEditorOperations.RemoveComponent(null, pawnAudio);
        before = recording.Starts.Count;
        player.DriveDirection = Vector3.UnitX;
        for (int frame = 0; frame < 30; frame++) { player.UpdateTransform(); }
        Check("removing the pawn audio component stops movement cues", recording.Starts.Count == before);
        player.DriveDirection = Vector3.Zero;
        world.EndPlay();

        string levelPath = FindLevel();
        var level = ManaLevel.FromJson(File.ReadAllText(levelPath));
        // Retain real saved scripts and transforms, replacing only graphics-heavy geometry with groups.
        var audioOnly = new ManaLevel { Name = "Audio fixture" };
        // The course is grouped into sections and categories, so its nodes are read through the hierarchy, at their world positions.
        foreach (var entry in LevelWalk.Flatten(level.Nodes))
        {
            var scripts = entry.Scripts?.Where(x => x.Type == ManaAudioSourceComponent.ScriptType).ToList();
            if (scripts is { Count: > 0 })
            {
                audioOnly.Nodes.Add(new ManaLevelNode { Name = entry.Name, Kind = ManaLevelSourceKind.Group,
                    Position = entry.Position, Rotation = entry.Rotation, Scale = entry.Scale, Scripts = scripts });
            }
        }
        var fixtureWorld = new ManaWorld();
        var fixtureContext = new ManaContext(null!, null!, fixtureWorld) { Audio = audio, DeferGpuUploads = true };
        fixtureWorld.Context = fixtureContext;
        fixtureWorld.DefaultScene = new ManaScene(fixtureContext); fixtureWorld.Scenes.Add(fixtureWorld.DefaultScene);
        var loaded = ManaLevelLoader.Load(audioOnly, fixtureContext, fixtureWorld.DefaultScene, replaceAuthoredNodes: false);
        var sources = loaded.Nodes.SelectMany(n => n.Components.OfType<ManaAudioSourceComponent>()).ToArray();
        Task.WhenAll(sources.Select(s => s.PrepareAsync())).GetAwaiter().GetResult();
        int expected = audioOnly.Nodes.Sum(n => n.Scripts?.Count ?? 0);
        Check("every saved course sound source loads independently", sources.Length == expected && expected >= 90);
        var captured = ManaLevelCapture.CaptureScene(fixtureWorld, fixtureWorld.DefaultScene, "Audio fixture", includeCamera: false);
        var roundTrip = ManaLevel.FromJson(captured.ToJson());
        Check("multiple audio sources on a node survive save/load", roundTrip.Nodes.Sum(n => n.Scripts?.Count ?? 0) == expected);
        Check("runtime clip, voice and load task are never serialized", !captured.ToJson().Contains("\"voice\"") && !captured.ToJson().Contains("\"clip\""));
        fixtureWorld.BeginPlay();
        foreach (var source in sources) { source.UpdateComponent(); }
        fixtureWorld.EndPlay();
        Check("level stop silences ambience", recording.Live.Count == 0);

        var bad = Source(Node("Missing clip"), ManaSoundCue.Pickup);
        bad.Clip = null; bad.ClipPath = "Assets/Audio/does-not-exist.wav";
        try { bad.PrepareAsync().GetAwaiter().GetResult(); } catch (FileNotFoundException) { }
        bad.BeginPlay(); bad.UpdateComponent(); ManaAudioCueRouter.EmitAt(bad.Owner, ManaSoundCue.Pickup);
        Check("missing audio is reported without crashing playback", bad.LoadError != null && bad.Voice == null);
        bad.Dispose();
        world.DefaultScene.Children.Remove(bad.Owner);
        foreach (var source in sources) { source.Dispose(); }

        context.Physics = new ManaPhysics(context);
        try
        {
            var surface = Node("Footstep ground");
            context.Physics.AddBoxBody(surface, new Vector3(10, 1, 10), new Vector3(0, -0.5f, 0));
            using var body = ManaCharacterBody.Create(context, Vector3.Zero, 0.3f, 2);
            body.RefreshGroundState();
            Check("ground probe identifies the node for surface footsteps", body.IsGrounded && ReferenceEquals(body.GroundNode, surface));
            body.Teleport(new Vector3(0, 10, 0));
            body.RefreshGroundState();
            Check("airborne ground probes clear the previous surface", !body.IsGrounded && body.GroundNode == null);
            var jumper = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false };
            jumper.Components.Add(new ManaPawnAudioComponent(jumper));
            world.DefaultScene.Children.Add(jumper);
            world.GameState.Player = jumper;
            jumper.EnablePhysics();
            world.BeginPlay();
            before = recording.Starts.Count;
            jumper.PressJump();
            jumper.UpdateTransform();
            Check("a physics jump reaches the audio component through its event", recording.Starts.Count == before + 1);
            jumper.ReleaseJump();
            var movement = jumper.GetComponent<ManaPawnAudioComponent>()!;
            movement.EndPlay();
            movement.BeginPlay();
            movement.BeginPlay();
            jumper.TeleportTo(Vector3.Zero);
            before = recording.Starts.Count;
            for (int frame = 0; frame < 8; frame++) { jumper.UpdateTransform(); }
            Check("restarting the component subscribes to landing only once", recording.Starts.Count == before + 1);
            world.EndPlay();
            before = recording.Starts.Count;
            jumper.PressJump();
            jumper.UpdateTransform();
            Check("ending play detaches pawn audio events", recording.Starts.Count == before);
        }
        finally
        {
            context.Physics.Simulation.Dispose();
            context.Physics.ThreadDispatcher.Dispose();
            context.Physics.BufferPool.Clear();
        }
    }

    private static string FindLevel()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Content", "Levels", "Sample Course.manalevel");
            if (File.Exists(path)) { return path; }
        }
        throw new FileNotFoundException("Sample Course.manalevel");
    }
}
