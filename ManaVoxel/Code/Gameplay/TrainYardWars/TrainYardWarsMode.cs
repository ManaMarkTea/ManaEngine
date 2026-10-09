using Mana;
using Mana.Code.Automation;
using Mana.Code.GameModes;
using ManaCore.Diagnostics;
using ManaUI;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;
using ManaVoxel.Code.Gameplay.TrainYardWars.View;

namespace ManaVoxel.Code.Gameplay.TrainYardWars
{
    /// <summary>
    /// Train Yard Wars as a level's game mode (Plans/Couch-Co-op-GameDesignDocuments/TrainYardWars.md): a generated
    /// hex continent, you against bots, mouse to build. A level that lists it plays it --
    /// <c>Content/Levels/Train Yard Wars.manalevel</c> -- and its settings below are per level.
    /// <para>
    /// The rules live in <see cref="TywGame"/> with no scene at all; this mode turns the mouse into its actions,
    /// <see cref="TywBoardView"/> shows the result in a scene of its own (so nothing of it is ever saved into the
    /// level), and the game's notices become popups and sounds.
    /// </para>
    /// </summary>
    public sealed partial class TrainYardWarsMode : ManaLevelGameMode
    {
        public const string ModeType = "TrainYardWars";

        private const int HumanSeat = 0;

        // Someone has played Red with the mouse or keyboard. Until then the first pad to join takes Red, so a couch of
        // pads alone has no seat standing idle.
        private bool _mousePlayed;
        private const float CameraPitch = -0.98f;
        // 1 shows the whole map, which is where play sits: it is one screen for everyone on the couch. Closer is for a look.
        private const float DefaultZoom = 1.0f;
        private const float NearestZoom = 0.18f;

        // ---- Settings (saved with the level, shown in the Inspector) -------------------------------------

        /// <summary>Seats at the table, 2 to 8. You are Red; the rest are bots until couch input arrives.</summary>
        [System.ComponentModel.DataAnnotations.Range(2, 8)]
        public int Players { get; set; } = 4;

        /// <summary>The world to generate. 0 makes a new one every time play starts.</summary>
        [System.ComponentModel.DataAnnotations.Range(0, 65535)]
        public int Seed { get; set; }

        /// <summary>Prestige that wins the match outright.</summary>
        [System.ComponentModel.DataAnnotations.Range(3, 40)]
        public int PrestigeTarget { get; set; } = 15;

        /// <summary>Length of a match in minutes; the most prestige wins when it runs out.</summary>
        [System.ComponentModel.DataAnnotations.Range(2.0, 30.0)]
        public float MatchMinutes { get; set; } = 12.0f;

        /// <summary>Seconds between a bot's decisions; lower is a quicker, harder bot.</summary>
        [System.ComponentModel.DataAnnotations.Range(0.3, 4.0)]
        public float BotThinkSeconds { get; set; } = 1.4f;

        /// <summary>The hour the match is played at, 0 to 24: 12 is noon, 16.5 late afternoon, 18 a low sun, after 19 night.</summary>
        [System.ComponentModel.DataAnnotations.Range(0.0, 24.0)]
        public float Hour { get; set; } = 16.5f;

        /// <summary>How much of the sun and sky light reaches the board, 0.1 to 1. Lower makes torches and town lamps matter.</summary>
        [System.ComponentModel.DataAnnotations.Range(0.1, 1.0)]
        public float Daylight { get; set; } = 0.4f;

        /// <summary>Minutes for a whole day to pass while playing; 0 holds the hour.</summary>
        [System.ComponentModel.DataAnnotations.Range(0.0, 30.0)]
        public float DayMinutes { get; set; }

        // ---- Match state ---------------------------------------------------------------------------------

        public override bool HandlesExitKeys => true;

        // It builds a world of its own; the editor's floor would only show round the edge of the sea.
        public override bool KeepsEditorFloor => false;

        // Listed with the level game modes as soon as this assembly loads, without the registry knowing of it.
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register() => ManaVoxel.Code.Levels.ManaLevelGameModes.Register(new ManaVoxel.Code.Levels.ManaLevelGameModes.Definition
        {
            Type = ModeType,
            DisplayName = "Train Yard Wars",
            Description = "A railway race on a generated hex continent, everyone on one screen. Builds its own world and camera.",
            ModeType = typeof(TrainYardWarsMode),
            Create = () => new TrainYardWarsMode()
        });

        private readonly TywHudState _hud = new();
        private readonly Dictionary<Key, bool> _keysDown = new();
        private readonly List<TywBot> _bots = new();

        private ManaPlaySession? _session;
        private ManaScene? _scene;
        private int _seed;
        private TywGame? _game;
        private TywBoardView? _view;
        private TywPopups? _popups;
        private TywSounds? _sounds;
        private float _clock;

        // The camera looks at _focus from a distance set by _zoom; wheel zooms toward the pointer, right-drag pans.
        // It never leaves the map: the focus stays over the land, and the farthest zoom shows the whole world.
        private float _zoom = DefaultZoom;
        private Vector3 _focus;
        private float _pendingScroll;
        private Vector3? _panGrab;
        private Vector3 _mapCenter;
        private Vector2 _mapHalf = new(20.0f, 20.0f);

        // What play borrowed from the host, handed back on EndPlay.
        private Func<bool>? _savedCaptureMouse;
        private Func<bool>? _savedCaptureKeyboard;
        private bool _savedVerticalFlyKeysNeedLook;
        private ManaCore.Graphics.ManaSkyMode _savedSky;
        private float _savedTimeSpeed;
        private float _savedTimeOfDay;
        private bool _savedRealTime;
        private DateTimeOffset _savedSkyDateTime;
        private float _savedLightIntensity;
        private float _savedIblIntensity;

        // What a click does: build (track, mines, sabotage...) or light a torch. 1 and 2 switch.
        private bool _torchTool;
        private int _lightingStep;
        private IMouse? _mouse;

        // Set by scripts (tyw_pointer, tyw_press, tyw_release) to drive the same input path the mouse does.
        private Vector2? _scriptPointer;
        private bool? _scriptLeftDown;
        private bool _leftWasDown;
        private TywHex? _pressHex;
        private bool _dragged;
        private readonly Dictionary<int, (TywHex Hex, int Revision, TywTrackPlan? Plan)> _planCache = new();
        private TywHex? _lastSabotage;
        private bool _introduced;
        private bool _helpPinned;
        private bool _pauseOnSabotage;
        // A script's choice of seats for this play only, never written back to the level's setting.
        private int? _playersOverride;

        private ManaContext Context => _session!.Context;

        private bool UiWantsMouse => _session?.UiWantsMouse() == true;

        private bool UiWantsKeyboard => _session?.UiWantsKeyboard() == true;

        public override void BeginPlay(ManaPlaySession session)
        {
            _session = session;
            var world = Context.World;

            // The board camera is ours while playing: no fly keys, no mouse look.
            var camera = world.MainCamera;
            _savedCaptureMouse = camera.ShouldCaptureMouseInput;
            _savedCaptureKeyboard = camera.ShouldCaptureKeyboardInput;
            _savedVerticalFlyKeysNeedLook = camera.VerticalFlyKeysNeedLook;
            camera.ShouldCaptureMouseInput = () => true;
            camera.ShouldCaptureKeyboardInput = () => true;
            camera.VerticalFlyKeysNeedLook = true;
            var sky = world.IrradianceSettings;
            _savedSky = sky.Sky;
            _savedTimeSpeed = sky.TimeSpeed;
            _savedTimeOfDay = sky.TimeOfDay;
            _savedRealTime = sky.RealTime;
            _savedSkyDateTime = sky.SkyDateTime;
            _savedLightIntensity = sky.LightIntensity;
            _savedIblIntensity = sky.IblIntensity;
            SetLighting(Hour, Daylight);

            _sounds ??= new TywSounds(Context.Audio);
            _mouse = Context.Input.Mice.FirstOrDefault();
            if (_mouse != null)
            {
                _mouse.Scroll += OnScroll;
            }

            // A scene of its own: the level's scene stays exactly what was authored, and is saved as such.
            _scene = new ManaScene(Context) { Name = "Train Yard Wars" };
            lock (world.SceneLock)
            {
                world.Scenes.Add(_scene);
            }

            _view = new TywBoardView(Context, _scene);
            _view.Preload();
            _seed = Seed != 0 ? Seed : Environment.TickCount & 0xffff;
            StartMatch();
            if (session.Commands is { } commands)
            {
                RegisterCommands(commands);
            }
        }

        public override void EndPlay()
        {
            var world = Context.World;
            if (_mouse != null)
            {
                _mouse.Scroll -= OnScroll;
                _mouse = null;
            }

            lock (world.SceneLock)
            {
                if (_scene != null)
                {
                    world.Scenes.Remove(_scene);
                    _scene = null;
                }
            }

            var camera = world.MainCamera;
            camera.ShouldCaptureMouseInput = _savedCaptureMouse;
            camera.ShouldCaptureKeyboardInput = _savedCaptureKeyboard;
            camera.VerticalFlyKeysNeedLook = _savedVerticalFlyKeysNeedLook;
            var sky = world.IrradianceSettings;
            sky.Sky = _savedSky;
            sky.TimeSpeed = _savedTimeSpeed;
            sky.TimeOfDay = _savedTimeOfDay;
            sky.RealTime = _savedRealTime;
            sky.SkyDateTime = _savedSkyDateTime;
            sky.LightIntensity = _savedLightIntensity;
            sky.IblIntensity = _savedIblIntensity;
            _view = null;
            _game = null;
            _hud.Game = null;
            _session = null;
        }

        public override UiElement? BuildHud(UiTheme theme)
        {
            _popups = new TywPopups(theme);
            return TywHud.Build(theme, _hud, _popups.Element, HumanSeat);
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            if (!UiWantsMouse)
            {
                _pendingScroll += wheel.Y;
            }
        }

        // Holds the sky at an hour, or (with DayMinutes) lets the day run from it, with the sun and sky dimmed to
        // <paramref name="daylight"/> of the host's own levels. Placed lights are left alone: that is the contrast.
        private void SetLighting(float hour, float daylight)
        {
            var sky = Context.World.IrradianceSettings;
            daylight = Math.Clamp(daylight, 0.05f, 1.0f);
            // The sun dims faster than the sky: a low sun against a dark sky casts hard black shadows.
            sky.LightIntensity = _savedLightIntensity * daylight;
            sky.IblIntensity = _savedIblIntensity * (0.3f + 0.7f * daylight);
            sky.Sky = ManaCore.Graphics.ManaSkyMode.DayNightCycle;
            sky.RealTime = false;
            sky.TimeOfDay = ManaCore.Graphics.ManaSky.PhaseAt(Math.Clamp(hour, 0.0f, 24.0f));
            sky.TimeSpeed = DayMinutes > 0.0f ? 86400.0f / (DayMinutes * 60.0f) : 0.0f;
        }

        private void StartMatch()
        {
            var players = Math.Clamp(_playersOverride ?? Players, 2, 8);
            _game = new TywGame(players, _seed, humans: 1)
            {
                VictoryTarget = Math.Max(1, PrestigeTarget),
                Duration = MathF.Max(1.0f, MatchMinutes) * 60.0f
            };
            _bots.Clear();
            foreach (var player in _game.Players)
            {
                _bots.Add(new TywBot(player.Index, _seed) { ThinkSeconds = MathF.Max(0.2f, BotThinkSeconds) });
            }

            _hud.Game = _game;
            _hud.Paused = false;
            _hud.Message = $"Map {_seed}: drag from your depot to lay track toward a forest";
            _hud.MessageUntil = 8.0f;
            _planCache.Clear();
            ResetSeats();
            _popups?.Clear();
            _view?.Show(_game);
            (_mapCenter, _mapHalf) = TywBoardView.Extent(_game.Board);
            _zoom = DefaultZoom;
            _focus = HomeFocus();
            _introduced = false;
            _hud.ShowControls = true;
            ManaLog.WriteDiagnostic($"Train Yard Wars: map {_seed}, {players} players");
        }

        private void Restart(int seed)
        {
            _seed = seed;
            lock (Context.World.SceneLock)
            {
                StartMatch();
            }
        }

        public override void Tick(float deltaSeconds)
        {
            var dt = deltaSeconds;
            _clock += dt;
            HandleKeys(dt);
            MoveCamera();

            var game = _game;
            if (game == null || _view == null)
            {
                return;
            }

            HandleMouse(game);
            HandlePads(game, dt);

            if (!_hud.Paused)
            {
                var step = dt * _hud.Speed;
                // Big steps (fast-forward) are cut up so trains never skip a whole hex in one go.
                var slices = Math.Max(1, (int)MathF.Ceiling(step / 0.05f));
                for (var i = 0; i < slices; i++)
                {
                    for (var seat = 0; seat < _bots.Count; seat++)
                    {
                        if (game.Players[seat].IsBot || (seat == HumanSeat && _hud.Autoplay))
                        {
                            _bots[seat].Tick(game);
                        }
                    }

                    game.Tick(step / slices);
                }
            }

            if (!_introduced)
            {
                // Everyone finds their own depot: a name over each for the first few seconds.
                _introduced = true;
                foreach (var player in game.Players)
                {
                    _popups?.Add(TywGeometry.Center(player.Depot) + new Vector3(0, 0.6f, 0),
                        player.Index == HumanSeat && !_hud.Autoplay ? $"{player.Name} (you)" : player.Name,
                        Vector4.Lerp(TywBoardView.PlayerColors[player.Index], Vector4.One, 0.3f), _clock, big: true, seconds: 6.0f);
                }
            }

            ShowNotices(game);
            _view.Sync(dt);
            var camera = Context.World.MainCamera;
            _popups?.Update(camera._ViewMatrix * camera._ProjectionMatrix, _clock);
        }

        // ---- Camera ----------------------------------------------------------------------------------------

        // The whole world in view: everyone plays on the one screen.
        private Vector3 HomeFocus() => _mapCenter;

        private static Vector3 CameraForward => new(0.0f, MathF.Sin(CameraPitch), -MathF.Cos(CameraPitch));

        private void MoveCamera()
        {
            var mouse = Context.Input.Mice.FirstOrDefault();
            if (_pendingScroll != 0.0f)
            {
                // Zoom toward what is under the pointer: keep that board point under it.
                var before = BoardPointUnderPointer();
                _zoom = Math.Clamp(_zoom * MathF.Pow(0.88f, _pendingScroll), NearestZoom, 1.0f);
                _pendingScroll = 0.0f;
                PlaceCamera();
                if (before is { } grabbed && BoardPointUnderPointer() is { } after)
                {
                    _focus += grabbed - after;
                }
            }

            var rightDown = mouse?.IsButtonPressed(MouseButton.Right) == true && !UiWantsMouse;
            if (rightDown)
            {
                PlaceCamera();
                if (_panGrab is { } grab && BoardPointUnderPointer() is { } now)
                {
                    _focus += grab - now;
                }
                else
                {
                    _panGrab = BoardPointUnderPointer();
                }
            }
            else
            {
                _panGrab = null;
            }

            // The sea's outer rings are the border; the view may look out over them but not wander off.
            var reach = _mapHalf * 0.85f;
            _focus = new Vector3(
                Math.Clamp(_focus.X, _mapCenter.X - reach.X, _mapCenter.X + reach.X),
                0.0f,
                Math.Clamp(_focus.Z, _mapCenter.Z - reach.Y, _mapCenter.Z + reach.Y + 4.0f));
            PlaceCamera();
        }

        private void PlaceCamera()
        {
            var camera = Context.World.MainCamera;
            var aspect = MathF.Max(0.5f, camera.AspectRatio);
            var halfFov = camera.EffectiveFieldOfViewDegrees * MathF.PI / 360.0f;
            // At zoom 1 the whole map fits, whichever way is the tighter fit, with room for the HUD along the edges.
            var fit = MathF.Max(_mapHalf.Y * 1.12f, _mapHalf.X / aspect * 1.22f) / MathF.Tan(halfFov);
            camera.Owner.Position = _focus - CameraForward * (fit * _zoom);
            camera.Owner.Rotation = new Vector3(CameraPitch, 0.0f, 0.0f);
            camera.Owner.UpdateTransform();
            camera.UpdateViewMatrix();
        }

        private Vector3? BoardPointUnderPointer()
        {
            var mouse = Context.Input.Mice.FirstOrDefault();
            if (mouse == null)
            {
                return null;
            }

            var pointer = _scriptPointer ?? mouse.Position;
            var ray = Context.World.MainCamera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            if (ray.Direction.Y > -1e-4f)
            {
                return null;
            }

            return ray.Origin + ray.Direction * ((TywGeometry.TopY - ray.Origin.Y) / ray.Direction.Y);
        }

        // ---- Keys ------------------------------------------------------------------------------------------

        private bool Pressed(IKeyboard keyboard, Key key)
        {
            var down = keyboard.IsKeyPressed(key);
            _keysDown.TryGetValue(key, out var was);
            _keysDown[key] = down;
            return down && !was;
        }

        private void HandleKeys(float dt)
        {
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            if (keyboard == null || UiWantsKeyboard)
            {
                return;
            }

            var game = _game;
            if (Pressed(keyboard, Key.Escape))
            {
                // Once pauses, twice quits: a stray Escape on the couch should not end the match.
                if (_hud.Paused || game == null || game.IsOver)
                {
                    _session?.Exit();
                    return;
                }

                _hud.Paused = true;
                Say("Paused -- Esc again to quit, Space to play on");
            }

            if (game == null)
            {
                return;
            }

            var pan = Vector3.Zero;
            if (keyboard.IsKeyPressed(Key.W) || keyboard.IsKeyPressed(Key.Up)) { pan.Z -= 1.0f; }
            if (keyboard.IsKeyPressed(Key.S) || keyboard.IsKeyPressed(Key.Down)) { pan.Z += 1.0f; }
            if (keyboard.IsKeyPressed(Key.A) || keyboard.IsKeyPressed(Key.Left)) { pan.X -= 1.0f; }
            if (keyboard.IsKeyPressed(Key.D) || keyboard.IsKeyPressed(Key.Right)) { pan.X += 1.0f; }
            _focus += pan * (dt * 30.0f * _zoom);

            if (Pressed(keyboard, Key.Home))
            {
                _focus = HomeFocus();
                _zoom = DefaultZoom;
            }

            if (Pressed(keyboard, Key.L))
            {
                // Noon in full light, the level's own evening, night -- and round again.
                _lightingStep = (_lightingStep + 1) % 3;
                switch (_lightingStep)
                {
                    case 0:
                        SetLighting(Hour, Daylight);
                        Say("Evening");
                        break;
                    case 1:
                        SetLighting(22.0f, 1.0f);
                        Say("Night: the towns and your torches carry the light");
                        break;
                    default:
                        SetLighting(12.0f, 1.0f);
                        Say("Noon");
                        break;
                }
            }

            if (Pressed(keyboard, Key.Number1) || Pressed(keyboard, Key.Keypad1))
            {
                _torchTool = false;
                Say("Build: track, mines, stations, repairs, sabotage");
            }

            if (Pressed(keyboard, Key.Number2) || Pressed(keyboard, Key.Keypad2))
            {
                _torchTool = true;
                Say($"Torch: click your own hex ({TywCosts.Format(TywCosts.Torch)}); mines beside a torch work {TywGame.TorchBoost:P0} faster");
            }

            if (Pressed(keyboard, Key.Space))
            {
                _hud.Paused = !_hud.Paused && !game.IsOver;
                _hud.MessageUntil = 0.0f;
            }

            if (Pressed(keyboard, Key.H))
            {
                _helpPinned = !_hud.ShowControls;
                _hud.ShowControls = _helpPinned;
            }

            if (game.Time > 30.0f && !_helpPinned)
            {
                _hud.ShowControls = false;
            }

            if (Pressed(keyboard, Key.F))
            {
                _hud.Speed = _hud.Speed >= 8.0f ? 1.0f : _hud.Speed * 2.0f;
            }

            if (Pressed(keyboard, Key.B))
            {
                _hud.Autoplay = !_hud.Autoplay;
                _mousePlayed = true;
                _sounds?.Play(TywSounds.Toggle, 0.6f);
                Say(_hud.Autoplay ? "A bot is playing your seat (B to take it back)" : "Your seat again");
            }

            if (Pressed(keyboard, Key.T) && !_hud.Paused)
            {
                _mousePlayed = true;
                if (game.BuyTrain(HumanSeat) is { } why)
                {
                    Refuse($"No train: {why}");
                }
            }

            if (Pressed(keyboard, Key.N))
            {
                Restart(Environment.TickCount & 0xffff);
            }

            if (Pressed(keyboard, Key.R))
            {
                Restart(_seed);
            }
        }

        private void Say(string text)
        {
            _hud.Message = text;
            _hud.MessageUntil = (_game?.Time ?? 0.0f) + 3.0f;
        }

        private void Refuse(string text)
        {
            Say(text);
            _sounds?.Play(TywSounds.Error, 0.5f);
        }

        // ---- Mouse -----------------------------------------------------------------------------------------

        private TywHex? HexUnderPointer(TywGame game)
        {
            if (BoardPointUnderPointer() is not { } point)
            {
                return null;
            }

            var hex = TywHex.FromWorld(new Vector2(point.X, point.Z), TywGeometry.HexRadius);
            return game.Board.Contains(hex) ? hex : null;
        }

        // Planning is a search over the whole board; once per hex and board change is plenty.
        private TywTrackPlan? PlanFor(TywGame game, int seat, TywHex hex)
        {
            if (_planCache.TryGetValue(seat, out var cached) && cached.Hex == hex && cached.Revision == game.Revision)
            {
                return cached.Plan;
            }

            var plan = game.PlanTrack(seat, hex);
            _planCache[seat] = (hex, game.Revision, plan);
            return plan;
        }

        private void HandleMouse(TywGame game)
        {
            var view = _view!;
            var mouse = Context.Input.Mice.FirstOrDefault();
            var hex = (UiWantsMouse && _scriptPointer == null) || game.IsOver ? null : HexUnderPointer(game);
            var leftDown = _scriptLeftDown ?? (mouse?.IsButtonPressed(MouseButton.Left) == true && !UiWantsMouse);
            var player = game.Players[HumanSeat];
            var color = TywBoardView.PlayerColors[HumanSeat];

            if (leftDown && !_leftWasDown)
            {
                _pressHex = hex;
                _dragged = false;
            }

            if (leftDown && hex != null && _pressHex != null && hex != _pressHex)
            {
                _dragged = true;
            }

            // What a click (or the end of a drag) here would do, shown before it happens.
            TywAction? action = hex is { } at ? _torchTool ? game.TorchActionAt(HumanSeat, at) : game.ActionAt(HumanSeat, at) : null;
            TywTrackPlan? plan = null;
            if (hex is { } target && !_torchTool && (_dragged || action?.Kind == TywActionKind.Track))
            {
                plan = PlanFor(game, HumanSeat, target);
            }

            if (plan != null && plan.NewLinks.Count > 0)
            {
                var affordable = player.CanAfford(plan.Cost);
                view.ShowPlan(HumanSeat, plan.Path, affordable ? Vector4.Lerp(color, Vector4.One, 0.35f) : new Vector4(1.0f, 0.25f, 0.2f, 1.0f));
                _hud.Hint = $"Track: {plan.NewLinks.Count} {(plan.NewLinks.Count == 1 ? "hex" : "hexes")} for {TywCosts.Format(plan.Cost)}" +
                            (affordable ? "" : "  (not enough -- lays what you can pay for)");
            }
            else
            {
                view.ShowPlan(HumanSeat, null, color);
                _hud.Hint = hex is { } here ? Describe(game, here, action) : "";
            }

            view.ShowHover(hex, action?.Blocked == null && action?.Kind is not (null or TywActionKind.None)
                ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                : new Vector4(0.6f, 0.6f, 0.6f, 1.0f));

            if (!leftDown && _leftWasDown && hex is { } released && !_hud.Autoplay)
            {
                _mousePlayed = true;
                if (_hud.Paused)
                {
                    Refuse("Paused -- Space to play on");
                }
                else if (_dragged || action?.Kind == TywActionKind.Track)
                {
                    if (plan != null && plan.NewLinks.Count > 0 && game.BuildTrack(HumanSeat, plan) == 0)
                    {
                        Refuse($"Can't afford track ({TywCosts.Format(plan.Cost)})");
                    }
                }
                else if (_torchTool)
                {
                    if (_pressHex == released && game.PlaceTorch(HumanSeat, released) is { } why)
                    {
                        Refuse($"Torch: {why}");
                    }
                }
                else if (_pressHex == released && game.Act(HumanSeat, released) is { } why)
                {
                    Refuse($"{action?.Label}: {why}");
                }
            }

            _leftWasDown = leftDown;
        }

        private static string Describe(TywGame game, TywHex hex, TywAction? action)
        {
            var tile = game.Board[hex]!;
            if (tile.Terrain == TywTerrain.Water)
            {
                return "";
            }

            var what = tile.Terrain switch
            {
                TywTerrain.Town when tile.SatisfiedUntil > game.Time => $"Town (just served, wants more in {tile.SatisfiedUntil - game.Time:0}s)",
                TywTerrain.Town => $"Town wants {tile.Demand} (a load of {TywGame.TownLoadMinimum}+ for 1 prestige)",
                TywTerrain.Port => $"Port: trades a load for half as much of whatever you're shortest of",
                TywTerrain.Desert => "Desert",
                TywTerrain.Sand => "Beach",
                TywTerrain.Depot => $"{game.Players[tile.BuildingOwner].Name}'s depot",
                _ when tile.IsResource => $"{tile.Terrain}: {tile.Produces} {new string('*', tile.Richness)}",
                _ => tile.Terrain.ToString()
            };

            if (tile.Building is TywBuilding.Extractor or TywBuilding.Factory)
            {
                what += $" -- {game.Players[tile.BuildingOwner].Name}'s {tile.Building.ToString().ToLowerInvariant()}, {MathF.Floor(tile.Stock)} waiting" +
                        (game.IsLit(tile) ? $", torch-lit (+{TywGame.TorchBoost:P0})" : "");
            }
            else if (tile.Building == TywBuilding.Station)
            {
                what += $" -- {game.Players[tile.BuildingOwner].Name}'s station";
            }

            if (tile.IsDamaged(game.Time))
            {
                what += $" -- SABOTAGED, {tile.DamagedUntil - game.Time:0}s to clear";
            }

            if (action is { Kind: not (TywActionKind.None or TywActionKind.Track) })
            {
                what += $"    Click: {action.Label} ({TywCosts.Format(action.Cost)})";
                if (action.Blocked != null)
                {
                    what += $" -- {action.Blocked}";
                }
            }

            return what;
        }

        // ---- What happened ---------------------------------------------------------------------------------

        // A seat someone is playing: Red on the mouse (unless a bot has it), or any seat a pad holds.
        private bool IsPerson(int seat) =>
            (seat == HumanSeat && !_hud.Autoplay) || _padSeats.Any(s => s.Seat == seat);

        // Popups for everyone's doings, sounds mostly for your own: eight players' clunks would be a din.
        private void ShowNotices(TywGame game)
        {
            // Every person playing hears their own doings; with more than one, prestige shows in its owner's colour
            // rather than one gold that says nothing about whose it is.
            var people = Enumerable.Range(0, game.Players.Count).Where(IsPerson).ToList();
            foreach (var notice in game.TakeNotices())
            {
                var mine = people.Contains(notice.Player);
                var at = TywGeometry.Center(notice.Hex);
                var color = Vector4.Lerp(TywBoardView.PlayerColors[notice.Player], Vector4.One, 0.4f);
                var gold = people.Count > 1 ? color : new Vector4(1.0f, 0.88f, 0.35f, 1.0f);
                switch (notice.Kind)
                {
                    case TywNoticeKind.Banked:
                        // Coal's own colour is near black; lighten every resource so it reads on the board.
                        _popups?.Add(at, $"+{notice.Amount} {notice.Resource}",
                            Vector4.Lerp(TywBoardView.ResourceColors[(int)notice.Resource], Vector4.One, 0.35f), _clock, big: mine);
                        if (mine)
                        {
                            _sounds?.Play(TywSounds.Bank, 0.35f, 0.9f + 0.05f * notice.Amount);
                        }

                        break;

                    case TywNoticeKind.Delivered:
                    case TywNoticeKind.TownReached:
                        _popups?.Add(at, $"+{notice.Amount} prestige", mine ? gold : color, _clock, big: true);
                        _sounds?.Play(mine ? TywSounds.Points : TywSounds.Rival, mine ? 0.7f : 0.25f);
                        break;

                    case TywNoticeKind.Award:
                        _popups?.Add(at, "+2 prestige", mine ? gold : color, _clock, big: true);
                        _sounds?.Play(mine ? TywSounds.Award : TywSounds.Rival, mine ? 0.7f : 0.3f);
                        break;

                    case TywNoticeKind.Sabotaged:
                        _lastSabotage = notice.Hex;
                        if (_pauseOnSabotage)
                        {
                            _pauseOnSabotage = false;
                            _hud.Paused = true;
                        }

                        var victim = people.Contains(notice.Victim);
                        _popups?.Add(at, "Sabotage!", new Vector4(1.0f, 0.4f, 0.3f, 1.0f), _clock, big: victim);
                        if (victim)
                        {
                            _sounds?.Play(TywSounds.Alarm, 0.7f);
                            Say(notice.Victim == HumanSeat && people.Count == 1
                                ? $"{game.Players[notice.Player].Name} cut your line! Click the burning hex to repair it"
                                : $"{game.Players[notice.Player].Name} cut {game.Players[notice.Victim].Name}'s line! Repair the burning hex");
                        }
                        else if (mine)
                        {
                            _sounds?.Play(TywSounds.Hit, 0.6f);
                        }

                        break;

                    case TywNoticeKind.Repaired when mine:
                        _popups?.Add(at, "Repaired", new Vector4(0.6f, 1.0f, 0.6f, 1.0f), _clock);
                        _sounds?.Play(TywSounds.Confirm, 0.5f);
                        break;

                    case TywNoticeKind.Traded:
                        _popups?.Add(at, $"Traded: +{notice.Amount} {notice.Resource}",
                            Vector4.Lerp(TywBoardView.ResourceColors[(int)notice.Resource], Vector4.One, 0.35f), _clock, big: mine);
                        if (mine)
                        {
                            _sounds?.Play(TywSounds.Bank, 0.4f, 1.2f);
                        }

                        break;

                    case TywNoticeKind.TorchLit:
                        _popups?.Add(at, "Torch lit", new Vector4(1.0f, 0.75f, 0.4f, 1.0f), _clock);
                        if (mine)
                        {
                            _sounds?.Play(TywSounds.Build, 0.5f, 1.3f);
                        }

                        break;

                    case TywNoticeKind.TrainBought:
                        _popups?.Add(at, "New train", color, _clock);
                        if (mine)
                        {
                            _sounds?.Play(TywSounds.Train, 0.6f);
                        }

                        break;

                    case TywNoticeKind.TrackLaid when mine:
                        _sounds?.Play(TywSounds.Track, 0.45f, 1.0f + 0.04f * Math.Min(notice.Amount, 5));
                        break;

                    case TywNoticeKind.Built when mine:
                        _sounds?.Play(TywSounds.Build, 0.6f);
                        break;

                    case TywNoticeKind.Won:
                        _sounds?.Play(mine ? TywSounds.Win : TywSounds.Lose, 0.8f);
                        break;
                }
            }
        }

        // ---- Scripts ---------------------------------------------------------------------------------------

        private void RegisterCommands(ManaCommandRunner runner)
        {
            // tyw_autoplay: a bot takes your seat. tyw_speed: frames = how many times real speed.
            runner.Register("tyw_autoplay", command =>
            {
                _hud.Autoplay = command.Visible ?? true;
                _mousePlayed = true;
            });
            runner.Register("tyw_speed", command => _hud.Speed = Math.Max(1, command.Frames));
            // [zoom] or [zoom, x, z]: how close, and where to look, from the middle of the map.
            runner.Register("tyw_zoom", command =>
            {
                _zoom = command.Position is { Length: > 0 } z ? z[0] : DefaultZoom;
                _focus = command.Position is { Length: >= 3 } f ? _mapCenter + new Vector3(f[1], 0.0f, f[2]) : HomeFocus();
            });
            // Window pixels [x, y]; no position hands the pointer back to the mouse.
            runner.Register("tyw_pointer", command =>
                _scriptPointer = command.Position is { Length: >= 2 } p ? new Vector2(p[0], p[1]) : null);
            runner.Register("tyw_press", _ => _scriptLeftDown = true);
            runner.Register("tyw_release", _ => _scriptLeftDown = false);
            // Pauses at the next sabotage, before a bot can repair it; then tyw_look_sabotage frames it.
            runner.Register("tyw_pause_on_sabotage", _ => _pauseOnSabotage = true);
            // Points the camera at the last sabotaged hex, close up (for checking the fire).
            runner.Register("tyw_look_sabotage", _ =>
            {
                if (_lastSabotage is { } hex)
                {
                    _zoom = 0.35f;
                    _focus = TywGeometry.Center(hex) with { Y = 0.0f };
                }
            });
            runner.Register("tyw_hover_log", _ =>
                ManaLog.WriteDiagnostic($"Command file: tyw hint '{_hud.Hint}', message '{_hud.Message}'"));
            runner.Register("tyw_log", _ =>
            {
                if (_game is { } game)
                {
                    foreach (var p in game.Players)
                    {
                        ManaLog.WriteDiagnostic($"Command file: tyw {p.Name} {game.VictoryPoints(p.Index)} prestige ({game.VictoryBreakdown(p.Index)}), " +
                                                $"{game.ClaimedHexes(p.Index)} hexes, {game.TrainCount(p.Index)} trains, time {game.Time:0}");
                    }
                }

                var sky = Context.World.IrradianceSettings;
                ManaLog.WriteDiagnostic($"Command file: tyw light {sky.LightIntensity:0.00}, ibl {sky.IblIntensity:0.00}, sky {sky.Sky}, phase {sky.TimeOfDay:0.000}");
            });

            // A stand-in gamepad: frames = which pad (1, 2...), position = [stick x, stick y] (y down is +), name = the
            // buttons held, comma-separated ("A", "Start,X"). It stays as set until the next tyw_pad for that pad.
            runner.Register("tyw_pad", command =>
            {
                var state = new PadState
                {
                    Stick = command.Position is { Length: >= 2 } stick ? new Vector2(stick[0], stick[1]) : Vector2.Zero
                };
                foreach (var name in (command.Name ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse<ButtonName>(name, true, out var button))
                    {
                        state.Down.Add(button);
                    }
                }

                _scriptPads[Math.Max(1, command.Frames)] = state;
            });
            runner.Register("tyw_seats_log", _ =>
            {
                foreach (var seat in _padSeats)
                {
                    var hex = TywHex.FromWorld(new Vector2(seat.Cursor.X, seat.Cursor.Z), TywGeometry.HexRadius);
                    ManaLog.WriteDiagnostic($"Command file: tyw seat {seat.Seat} ({seat.Name}) cursor {hex}, torch {seat.TorchTool}, " +
                                            $"hexes {_game?.ClaimedHexes(seat.Seat)}, trains {_game?.TrainCount(seat.Seat)}, bot {_game?.Players[seat.Seat].IsBot}, " +
                                            $"bank [{string.Join(",", _game?.Players[seat.Seat].Bank ?? Array.Empty<int>())}], label '{seat.Label}'");
                }
            });

            // [seed, players]: a new match on that map, as N does with a random one.
            runner.Register("tyw_new_map", command =>
            {
                if (command.Position is { Length: >= 2 } setup)
                {
                    _playersOverride = (int)setup[1];
                }

                Restart(command.Seed);
            });
        }
    }
}
