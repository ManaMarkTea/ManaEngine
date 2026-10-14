using System.Numerics;
using Mana;
using Mana.Code.GameModes;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Diagnostics;
using ManaCore.Audio;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.GridMaps;
using Silk.NET.Input;

namespace ManaVoxel.Code.Gameplay.YachtJousting;

public sealed class YachtJoustingMode : ManaLevelGameMode
{
    public int PointsToWin { get; set; } = 5;
    public override bool KeepsEditorFloor => false;
    public override bool HandlesExitKeys => true;
    private ManaPlaySession? _session;
    private ManaScene? _scene;
    private YachtMatch _match = new();
    private readonly ManaSceneNode?[] _boats = new ManaSceneNode?[2];
    private readonly ManaSceneNode?[] _lances = new ManaSceneNode?[2];
    private readonly ManaSceneNode?[] _shields = new ManaSceneNode?[2];
    private ManaSceneNode? _burst;
    private ManaWaterNode? _water;
    // Frames both boats from the side while playing; the camera does not fly meanwhile.
    private ManaComputedCameraRig? _cameraRig;
    private Vector3 _cameraPosition;
    private Vector3 _cameraRotation;
    private float _accumulator;
    private float _flash;
    private int _impact;
    private bool _paused;
    private bool _auto;
    private YachtInput _script;
    private bool _scriptEnabled;
    private float _simulationSpeed = 1;
    private readonly HashSet<Key> _pressed = new();
    private readonly List<ManaSceneNode> _rain = new();
    private readonly List<ManaSceneNode> _lightning = new();
    private readonly Dictionary<string, GridMapPrototype> _models = new();
    private ManaIrradianceSettings? _savedSky;
    private Task<ManaAudioClip>? _hitSound;
    private static readonly Vector4 Gold = new(1, 0.67f, 0.16f, 1);
    private static readonly Vector4 Blue = new(0.04f, 0.48f, 0.85f, 1);

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
    {
        Type = "YachtJousting", DisplayName = "Yacht Jousting", Description = "Lances, shields and rival captains on a stormy sea.",
        ModeType = typeof(YachtJoustingMode), Create = () => new YachtJoustingMode()
    });

    public override void BeginPlay(ManaPlaySession session)
    {
        _session = session;
        _match = new YachtMatch(PointsToWin);
        _accumulator = _flash = 0;
        _impact = 0;
        _paused = _auto = _scriptEnabled = false;
        _simulationSpeed = 1;
        _pressed.Clear();
        var context = session.Context;
        _savedSky = context.World.IrradianceSettings.Clone();
        var sky = context.World.IrradianceSettings;
        sky.WindSpeed = 14;
        sky.Gustiness = 0.8f;
        sky.Sky = ManaSkyMode.DayNightCycle;
        sky.TimeOfDay = 0.3f;
        sky.TimeSpeed = 0;
        sky.RealTime = false;
        sky.LightIntensity = 1.8f;
        sky.IblIntensity = 1.1f;
        _hitSound = context.Audio.LoadClipAsync("Assets/Audio/Starter/Gameplay/impact_hard.wav");
        _rain.Clear();
        _lightning.Clear();
        _models.Clear();
        var camera = context.World.MainCamera;
        _cameraPosition = camera.Owner.Position;
        _cameraRotation = camera.Owner.Rotation;
        _cameraRig = new ManaComputedCameraRig(CameraPose);
        _scene = new ManaScene(context) { Name = "Yacht Jousting Arena" };
        lock (context.World.SceneLock)
        {
            context.World.Scenes.Add(_scene);
            BuildArena(context, _scene);
        }
        if (session.Commands is { } runner)
        {
            runner.Register("yacht_autoplay", c => _auto = c.Visible ?? true);
            runner.Register("yacht_speed", c => _simulationSpeed = Math.Clamp(c.Frames, 1, 20));
            runner.Register("yacht_input", c =>
            {
                _scriptEnabled = true;
                _script = new YachtInput(new Vector2(c.Position?.ElementAtOrDefault(0) ?? 0, c.Position?.ElementAtOrDefault(1) ?? 0), c.Name == "charge", c.Name == "block");
            });
            runner.Register("yacht_log", _ => ManaLog.WriteDiagnostic($"Command file: yacht score {_match.Boats[0].Score}:{_match.Boats[1].Score}, health {_match.Boats[0].Health:F0}:{_match.Boats[1].Health:F0}, impacts {_match.ImpactSerial}, winner {_match.Winner}"));
        }
        camera.AttachRig(_cameraRig);
    }

    private void BuildArena(ManaContext context, ManaScene scene)
    {
        var shapes = new ManaShapeFactory(context, scene);
        _water = new ManaWaterNode(context, new Vector2(180, 150), 128) { Position = new Vector3(64, 0, 64), Name = "Storm Sea" };
        scene.Children.Add(_water);
        for (var i = 0; i < 13; i++)
        {
            shapes.AddSphere(new Vector3(20 + i * 7, 20 + i % 3, 25), new Vector4(0.12f, 0.18f, 0.25f, 1), new Vector3(18, 8, 12));
        }
        for (var i = 0; i < 80; i++)
        {
            var rain = shapes.AddCube(new Vector3(40 + i * 17 % 50, i * 7 % 18, 48 + i * 11 % 28), new Vector4(0.55f, 0.75f, 0.86f, 1), new Vector3(0.025f, 0.8f, 0.025f));
            rain.Rotation = new Vector3(0, 0, -0.3f);
            _rain.Add(rain);
        }
        for (var i = 0; i < 6; i++)
        {
            var bolt = shapes.AddCube(new Vector3(51 + (i % 2) * 1.2f, 17 - i * 2, 36), new Vector4(0.65f, 0.9f, 1, 1), new Vector3(0.12f, 2.7f, 0.12f));
            bolt.Rotation = new Vector3(0, 0, i % 2 == 0 ? 0.45f : -0.45f);
            foreach (var renderer in bolt.Components.OfType<MeshRendererComponent>())
            {
                renderer.Mesh.Materials[0].Unlit = true;
            }
            _lightning.Add(bolt);
        }
        for (var i = 0; i < 12; i++)
        {
            var x = 28 + i * 6;
            var rock = Model(context, $"zip:kenney_pirate-kit.zip/Models/GLB format/rocks-{(i % 2 == 0 ? "a" : "b")}.glb", 3 + i % 3, false);
            rock.Position = new Vector3(x, -0.3f, 43 - i % 3);
            rock.Rotation = new Vector3(0, i * 1.3f, 0);
            scene.Children.Add(rock);
        }
        var lighthouse = Model(context, "Mana/Assets/Models/Lighthouse/lighthouse.glb", 11, false);
        lighthouse.Position = new Vector3(88, 0, 44);
        scene.Children.Add(lighthouse);
        foreach (var x in new[] { 37f, 87f })
        {
            var island = Model(context, "zip:kenney_pirate-kit.zip/Models/GLB format/patch-sand-foliage.glb", 17, true);
            island.Position = new Vector3(x, 0.15f, 43);
            scene.Children.Add(island);
            for (var i = 0; i < 3; i++)
            {
                var palm = Model(context, "zip:kenney_pirate-kit.zip/Models/GLB format/palm-detailed-bend.glb", 6 + i, false);
                palm.Position = new Vector3(x - 4 + i * 3, 0.3f, 40 - i);
                palm.Rotation = new Vector3(0, i * 1.7f, 0);
                scene.Children.Add(palm);
            }
            var dock = Model(context, "zip:kenney_pirate-kit.zip/Models/GLB format/structure-platform-dock.glb", 7, true);
            dock.Position = new Vector3(x, 0.2f, 49);
            scene.Children.Add(dock);
        }
        for (var i = 0; i < 8; i++)
        {
            var buoy = Model(context, "zip:kenney_watercraft-pack.zip/Models/GLB format/buoy-flag.glb", 1.6f, false);
            buoy.Position = new Vector3(43 + i * 6, 0, 74);
            scene.Children.Add(buoy);
        }
        for (var i = 0; i < 2; i++)
        {
            _boats[i] = BuildBoat(context, scene, shapes, i);
        }
        _burst = shapes.AddIcosahedron(new Vector3(64, -10, 64), Gold, 0.5f);
        _burst.Name = "Joust Impact";
        foreach (var node in scene.Children)
        {
            node.LevelSource = null;
            node.IsEditorTransient = true;
        }
    }

    private ManaSceneNode BuildBoat(ManaContext context, ManaScene scene, ManaShapeFactory shapes, int seat)
    {
        var root = new ManaSceneNode(context, Matrix4x4.Identity) { Name = seat == 0 ? "Captain Astor's Yacht" : "Viscount Vander's Yacht", IsEditorTransient = true };
        scene.Children.Add(root);
        var trim = seat == 0 ? Blue : new Vector4(0.7f, 0.07f, 0.1f, 1);
        ManaSceneNode Adopt(ManaSceneNode node, string name)
        {
            scene.Children.Remove(node);
            node.LevelSource = null;
            node.Name = name;
            root.AddChild(node);
            return node;
        }
        var boatModel = Model(context, $"zip:kenney_watercraft-pack.zip/Models/GLB format/boat-speed-{(seat == 0 ? "g" : "h")}.glb", 8, true);
        root.AddChild(boatModel);
        Adopt(shapes.AddCube(new Vector3(-2.5f, 2.65f, 0), Vector4.One, new Vector3(0.1f, 2, 0.1f)), "Aerial");
        Adopt(shapes.AddCube(new Vector3(-2.1f, 3.3f, 0), trim, new Vector3(0.8f, 0.5f, 0.08f)), "Pennant");
        var captain = Model(context, $"zip:kenney_mini-characters.zip/Models/GLB format/character-male-{(seat == 0 ? "c" : "d")}.glb", 2.3f, false);
        captain.Position = new Vector3(0.6f, 1.4f, 0);
        captain.Rotation = new Vector3(0, MathF.PI / 2, 0);
        root.AddChild(captain);
        _lances[seat] = Adopt(shapes.AddCube(new Vector3(3.7f, 2.1f, 0.35f), Gold, new Vector3(5.8f, 0.13f, 0.13f)), "Lance");
        var tip = shapes.AddPyramid(new Vector3(3, 0, 0), trim, new Vector3(0.65f, 1, 0.65f));
        scene.Children.Remove(tip);
        tip.LevelSource = null;
        tip.Rotation = new Vector3(0, 0, -MathF.PI / 2);
        _lances[seat]?.AddChild(tip);
        _shields[seat] = Adopt(shapes.AddSphere(new Vector3(1.65f, 2.05f, 0.65f), trim, new Vector3(0.22f, 1.2f, 1)), "Shield");
        Adopt(shapes.AddCube(new Vector3(1.8f, 2.05f, 0.65f), Gold, new Vector3(0.1f, 0.7f, 0.12f)), "Shield Crest");
        return root;
    }

    private ManaSceneNode Model(ManaContext context, string asset, float size, bool alongX)
    {
        if (!_models.TryGetValue(asset, out var prototype))
        {
            prototype = GridMapPrototype.Load(asset, context);
            _models[asset] = prototype;
        }
        if (!prototype.HasGeometry)
        {
            throw new InvalidOperationException($"Yacht Jousting asset '{asset}': {prototype.Error}");
        }
        var extent = prototype.BoundsMax - prototype.BoundsMin;
        var center = (prototype.BoundsMax + prototype.BoundsMin) * 0.5f;
        var scale = size / Math.Max(0.001f, alongX ? Math.Max(extent.X, extent.Z) : extent.Y);
        var turn = alongX && extent.Z > extent.X ? Matrix4x4.CreateRotationY(MathF.PI / 2) : Matrix4x4.Identity;
        return prototype.Instantiate(context, Path.GetFileNameWithoutExtension(asset),
            Matrix4x4.CreateTranslation(-center.X, -prototype.BoundsMin.Y, -center.Z) * Matrix4x4.CreateScale(scale) * turn);
    }

    private bool Press(IKeyboard keyboard, Key key)
    {
        if (!keyboard.IsKeyPressed(key))
        {
            _pressed.Remove(key);
            return false;
        }
        return _pressed.Add(key);
    }

    public override void Tick(float deltaSeconds)
    {
        if (_session == null)
        {
            return;
        }
        var keyboard = _session.Context.Input.Keyboards.FirstOrDefault();
        var input = default(YachtInput);
        if (keyboard != null && !_session.UiWantsKeyboard())
        {
            if (Press(keyboard, Key.Escape))
            {
                _paused = !_paused;
            }
            if (Press(keyboard, Key.R))
            {
                _match = new YachtMatch(PointsToWin);
                _impact = 0;
                _paused = false;
            }
            bool Down(Key a, Key b) => keyboard.IsKeyPressed(a) || keyboard.IsKeyPressed(b);
            input = new YachtInput(new Vector2((Down(Key.D, Key.Right) ? 1 : 0) - (Down(Key.A, Key.Left) ? 1 : 0), (Down(Key.S, Key.Down) ? 1 : 0) - (Down(Key.W, Key.Up) ? 1 : 0)), Down(Key.Space, Key.F), Down(Key.ShiftLeft, Key.ShiftRight));
        }
        if (!_paused)
        {
            _accumulator += Math.Min(deltaSeconds, 0.1f) * _simulationSpeed;
            while (_accumulator >= 1f / 60)
            {
                _match.Step(1f / 60, _auto ? _match.Bot(0) : _scriptEnabled ? _script : input, _match.Bot(1));
                _accumulator -= 1f / 60;
            }
        }
        if (_match.ImpactSerial != _impact)
        {
            _impact = _match.ImpactSerial;
            _flash = 0.3f;
            _water?.SpawnRipple(new Vector3(64 + _match.ImpactPosition.X, 0, 64 + _match.ImpactPosition.Y), 3);
            if (_hitSound is { IsCompletedSuccessfully: true } sound)
            {
                _session.Context.Audio.Play(sound.Result, new ManaAudioPlayOptions { Volume = 0.7f });
            }
        }
        _flash = Math.Max(0, _flash - deltaSeconds);
        for (var i = 0; i < _rain.Count; i++)
        {
            var rain = _rain[i];
            rain.Position = new Vector3(40 + i * 17 % 50, 18 - ((_match.Time * 15 + i * 7) % 18), 48 + i * 11 % 28);
            rain.UpdateTransform();
        }
        foreach (var bolt in _lightning)
        {
            foreach (var renderer in bolt.Components.OfType<MeshRendererComponent>())
            {
                renderer.IsVisible = _match.Time % 9 < 0.18f;
            }
        }
        for (var i = 0; i < 2; i++)
        {
            var b = _match.Boats[i];
            if (_boats[i] is { } boat)
            {
                boat.Position = new Vector3(64 + b.Position.X, 0.2f + MathF.Sin(_match.Time * 2.5f + i) * 0.12f, 64 + b.Position.Y);
                boat.Rotation = new Vector3(0, b.Facing > 0 ? 0 : MathF.PI, MathF.Sin(_match.Time * 2 + i) * 0.025f);
                boat.UpdateTransform();
            }
            if (_lances[i] is { } lance)
            {
                lance.Rotation = new Vector3(0, 0, b.Attack > 0 ? 0 : b.Charge * 0.15f + 0.12f);
                lance.UpdateTransform();
            }
            if (_shields[i] is { } shield)
            {
                shield.Scale = new Vector3(b.Blocking ? 1.5f : 1);
                shield.UpdateTransform();
            }
        }
        if (_burst != null)
        {
            _burst.Position = new Vector3(64 + _match.ImpactPosition.X, _flash > 0 ? 2 : -20, 64 + _match.ImpactPosition.Y);
            _burst.Scale = new Vector3(1 + (0.3f - _flash) * 4);
            _burst.UpdateTransform();
            foreach (var renderer in _burst.Components.OfType<MeshRendererComponent>())
            {
                renderer.IsVisible = _flash > 0;
            }
        }
    }

    private (Vector3 Position, Vector3 Rotation) CameraPose()
    {
        var midpoint = (_match.Boats[0].Position + _match.Boats[1].Position) * 0.5f;
        var distance = Math.Clamp(Math.Abs(_match.Boats[0].Position.X - _match.Boats[1].Position.X) * 0.65f + 14, 22, 39);
        return (new Vector3(64 + midpoint.X + (_flash > 0 ? MathF.Sin(_match.Time * 100) * _flash : 0), 2 + distance * 0.34f, 64 + midpoint.Y + distance),
            new Vector3(-0.33f, 0, 0));
    }

    public override UiElement BuildHud(UiTheme theme)
    {
        TextBlock Label(Func<string> text, float size, Vector4 color)
        {
            var label = new TextBlock().FontSize(size).Foreground(color);
            label.BindPolled(t => t.Text, text);
            return label;
        }
        Border Card(UiElement child) => new Border().Fill(new Vector4(0.025f, 0.05f, 0.1f, 0.9f)).Outline(Gold).Rounded(6).PaddingOf(18, 12).Content(child);
        var grid = new Grid { IsHitTestVisible = false };
        for (var i = 0; i < 2; i++)
        {
            var seat = i;
            var stack = new StackPanel().Vertical(5);
            stack.Children.Add(Label(() => seat == 0 ? "CAPTAIN ASTOR  /  YOU" : "VISCOUNT VANDER  /  RIVAL", 18, seat == 0 ? new Vector4(0.4f, 0.85f, 1, 1) : new Vector4(1, 0.45f, 0.35f, 1)));
            stack.Children.Add(Label(() => $"{_match.Boats[seat].Score} / {_match.Target}   HULL  {_match.Boats[seat].Health:F0}%", 23, Vector4.One));
            var health = new ProgressBar { Tone = UiTone.Danger, Thickness = 12, Segments = 5 };
            health.BindPolled(p => p.Fraction, () => _match.Boats[seat].Health / 100);
            stack.Children.Add(health);
            stack.Children.Add(Label(() => $"GUARD  {_match.Boats[seat].Guard:F0}%    LANCE  {_match.Boats[seat].Charge * 100:F0}%", 15, Gold));
            var guard = new ProgressBar { Tone = UiTone.Accent, Thickness = 7 };
            guard.BindPolled(p => p.Fraction, () => _match.Boats[seat].Guard / 100);
            stack.Children.Add(guard);
            var card = Card(stack).Width(330).Top().Margin(24);
            grid.Children.Add(i == 0 ? card.Left() : card.Right());
        }
        grid.Children.Add(Label(() => "YACHT JOUSTING", 24, Gold).CenterH().Top().Margin(0, 130, 0, 0));
        grid.Children.Add(Label(() => _paused ? "PAUSED — ESC TO RESUME" : _match.Message, 30, Vector4.One).CenterH().Top().Margin(0, 182, 0, 0));
        grid.Children.Add(Card(Label(() => "WASD / ARROWS  Steer     SPACE / F  Hold & release lance     SHIFT  Shield     R  Rematch     ESC  Pause", 16, Vector4.One)).CenterH().Bottom().Margin(16));
        return grid;
    }

    public override void EndPlay()
    {
        if (_session is { } session)
        {
            var world = session.Context.World;
            lock (world.SceneLock)
            {
                if (_scene != null)
                {
                    world.Scenes.Remove(_scene);
                }
            }
            var camera = world.MainCamera;
            camera.DetachRig(_cameraRig);
            _cameraRig = null;
            camera.Owner.Position = _cameraPosition;
            camera.Owner.Rotation = _cameraRotation;
            camera.Owner.UpdateTransform();
            if (_savedSky is { } saved)
            {
                var sky = world.IrradianceSettings;
                sky.Sky = saved.Sky;
                sky.TimeOfDay = saved.TimeOfDay;
                sky.TimeSpeed = saved.TimeSpeed;
                sky.RealTime = saved.RealTime;
                sky.LightIntensity = saved.LightIntensity;
                sky.IblIntensity = saved.IblIntensity;
                sky.WindSpeed = saved.WindSpeed;
                sky.Gustiness = saved.Gustiness;
            }
        }
        _scene = null;
        _session = null;
    }
}
