using Mana.Code.Automation;
using Mana.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Loading;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;
using ManaVoxel.Code.Gameplay.TrainYardWars.View;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// Train Yard Wars, the couch game (Plans/Couch-Co-op-GameDesignDocuments/TrainYardWars.md): one hex board, you
    /// against bots, mouse to build. <c>ManaVoxel --train-yard-wars [--players N] [--seed N]</c> runs it.
    /// <para>
    /// The rules live in <see cref="TywGame"/> with no scene at all; this mode turns the mouse into its actions and
    /// <see cref="TywBoardView"/> shows the result.
    /// </para>
    /// </summary>
    public sealed class TrainYardWarsGameMode : BaseGameMode
    {
        private const int HumanSeat = 0;

        private readonly int _players;
        private readonly TywHudState _hud = new();
        private readonly Dictionary<Key, bool> _keysDown = new();
        private readonly List<TywBot> _bots = new();

        private int _seed;
        private TywGame? _game;
        private TywBoardView? _view;
        private ManaWebGpuContext? _webGpuContext;
        private ManaCommandRunner? _commandRunner;
        private float _zoom = 0.85f;

        // Set by scripts (tyw_pointer, tyw_press, tyw_release) to drive the same input path the mouse does.
        private Vector2? _scriptPointer;
        private bool? _scriptLeftDown;
        private bool _leftWasDown;
        private TywHex? _pressHex;
        private bool _dragged;
        private float _framingRadius = 20.0f;

        public TrainYardWarsGameMode(int players, int seed, ManaGraphicsBackendKind graphicsBackend) : base(graphicsBackend)
        {
            _players = Math.Clamp(players, 2, 8);
            _seed = seed;
        }

        public string? CommandFilePath { get; set; }

        public override bool DeferGpuUploadsOnStartup => true;

        protected override bool EnableManaUi => true;

        protected override string? WindowIconPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");

        public void RunWebGpu(string? requestedBackend)
        {
            new GameSilkWebGpu(
                "Train Yard Wars",
                requestedBackend,
                () => Context.World.CreateRenderScene(),
                (window, input) => LoadForGraphicsBackend(window, input, new ManaWebGpuGraphicsDevice(requestedBackend)),
                deltaTime => update(deltaTime),
                deltaTime => Render(deltaTime),
                (webGpuContext, input) =>
                {
                    _webGpuContext = webGpuContext;
                    if (Context.GraphicsDevice is ManaWebGpuGraphicsDevice webGpuGraphicsDevice)
                    {
                        webGpuGraphicsDevice.Context = webGpuContext;
                    }

                    webGpuContext.UiRoot = ManaUi?.Root;
                },
                iconPath: WindowIconPath
            ).Run();
        }

        protected override void BuildManaUi(ManaUiHost host)
        {
            host.Root.Content = TywHud.Build(host.Root.Theme, _hud);
        }

        public override void Load()
        {
            LoadingCoordinator.Begin();
            var camera = Context.World.MainCamera;
            // The board camera is ours: no fly keys, no mouse look.
            camera.ShouldCaptureMouseInput = () => true;
            camera.ShouldCaptureKeyboardInput = () => true;
            camera.VerticalFlyKeysNeedLook = true;

            var mouse = Context.Input.Mice.FirstOrDefault();
            if (mouse != null)
            {
                mouse.Scroll += (_, wheel) =>
                {
                    if (!UiWantsMouse)
                    {
                        _zoom = Math.Clamp(_zoom * MathF.Pow(0.9f, wheel.Y), 0.45f, 1.6f);
                    }
                };
            }

            Task.Run(() =>
            {
                try
                {
                    Context.DeferGpuUploads = true;
                    Loading.Report(new ManaLoadingProgress("Loading models", 0.2f));
                    _view = new TywBoardView(Context, Context.World.DefaultScene);
                    _view.Preload();
                    Loading.Report(new ManaLoadingProgress("Laying out the board", 0.7f));
                    StartMatch();
                    LoadingCoordinator.CompleteBackgroundWork();
                }
                catch (Exception exception)
                {
                    Trace.TraceError($"Train Yard Wars failed to load: {exception}");
                    LoadingCoordinator.Fail(exception);
                }
            });
        }

        protected override bool CanCompleteLoading() =>
            GraphicsBackend != ManaGraphicsBackendKind.WebGpu || _webGpuContext is { IsEnvironmentInitializationPending: false };

        private void StartMatch()
        {
            _game = new TywGame(_players, _seed, humans: 1);
            _bots.Clear();
            foreach (var player in _game.Players)
            {
                _bots.Add(new TywBot(player.Index, _seed));
            }

            _hud.Game = _game;
            _hud.Message = $"Map {_seed}: drag from your depot to lay track toward a forest";
            _hud.MessageUntil = 8.0f;
            _view?.Show(_game);
            var radius = _game.Board.Radius;
            _framingRadius = (radius + 0.8f) * TywGeometry.HexRadius * TywHex.Sqrt3;
            ManaLog.WriteDiagnostic($"Train Yard Wars: map {_seed}, {_players} players");
        }

        public override void Update(double deltaTime)
        {
            var dt = (float)deltaTime;
            TickCommandFile();
            HandleKeys();
            PlaceCamera();

            var game = _game;
            if (game == null || _view == null)
            {
                return;
            }

            HandleMouse(game);

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

            _view.Sync(dt);
        }

        private void PlaceCamera()
        {
            var camera = Context.World.MainCamera;
            var pitch = -0.98f;
            var forward = new Vector3(0.0f, MathF.Sin(pitch), -MathF.Cos(pitch));
            var aspect = MathF.Max(0.5f, camera.AspectRatio);
            var halfFov = camera.EffectiveFieldOfViewDegrees * MathF.PI / 360.0f;
            // Far enough that the board fits the narrower way, a little more for the HUD along the edges.
            var fit = _framingRadius / MathF.Tan(halfFov) / MathF.Min(1.0f, aspect / 1.15f);
            var distance = fit * 1.05f * _zoom;
            var target = new Vector3(0.0f, 0.0f, 3.0f);
            camera.Owner.Position = target - forward * distance;
            camera.Owner.Rotation = new Vector3(pitch, 0.0f, 0.0f);
            camera.Owner.UpdateTransform();
            camera.UpdateViewMatrix();
        }

        private bool Pressed(IKeyboard keyboard, Key key)
        {
            var down = keyboard.IsKeyPressed(key);
            _keysDown.TryGetValue(key, out var was);
            _keysDown[key] = down;
            return down && !was;
        }

        private void HandleKeys()
        {
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            if (keyboard == null || UiWantsKeyboard)
            {
                return;
            }

            if (Pressed(keyboard, Key.Escape))
            {
                Context.Window.Close();
            }

            if (IsLoading || _game == null)
            {
                return;
            }

            if (Pressed(keyboard, Key.Space))
            {
                _hud.Paused = !_hud.Paused;
            }

            if (Pressed(keyboard, Key.F))
            {
                _hud.Speed = _hud.Speed >= 8.0f ? 1.0f : _hud.Speed * 2.0f;
            }

            if (Pressed(keyboard, Key.B))
            {
                _hud.Autoplay = !_hud.Autoplay;
                Say(_hud.Autoplay ? "A bot is playing your seat (B to take it back)" : "Your seat again");
            }

            if (Pressed(keyboard, Key.T))
            {
                Say(_game.BuyTrain(HumanSeat) is { } why ? $"No train: {why}" : "A new train leaves the depot");
            }

            if (Pressed(keyboard, Key.N))
            {
                _seed = Environment.TickCount & 0xffff;
                lock (Context.World.SceneLock)
                {
                    StartMatch();
                }
            }
        }

        private void Say(string text)
        {
            _hud.Message = text;
            _hud.MessageUntil = (_game?.Time ?? 0.0f) + 3.0f;
        }

        private TywHex? HexUnderMouse(TywGame game)
        {
            var mouse = Context.Input.Mice.FirstOrDefault();
            if (mouse == null)
            {
                return null;
            }

            var pointer = _scriptPointer ?? mouse.Position;
            var ray = Context.World.MainCamera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            if (MathF.Abs(ray.Direction.Y) < 1e-4f)
            {
                return null;
            }

            var t = (TywGeometry.TopY - ray.Origin.Y) / ray.Direction.Y;
            if (t < 0.0f)
            {
                return null;
            }

            var point = ray.Origin + ray.Direction * t;
            var hex = TywHex.FromWorld(new Vector2(point.X, point.Z), TywGeometry.HexRadius);
            return game.Board.Contains(hex) ? hex : null;
        }

        private void HandleMouse(TywGame game)
        {
            var view = _view!;
            var mouse = Context.Input.Mice.FirstOrDefault();
            var hex = (UiWantsMouse && _scriptPointer == null) || game.IsOver ? null : HexUnderMouse(game);
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
            TywAction? action = hex is { } at ? game.ActionAt(HumanSeat, at) : null;
            TywTrackPlan? plan = null;
            if (hex is { } target && (_dragged || action?.Kind == TywActionKind.Track))
            {
                plan = game.PlanTrack(HumanSeat, target);
            }

            if (plan != null && plan.NewLinks.Count > 0)
            {
                var affordable = player.CanAfford(plan.Cost);
                view.ShowPlan(plan.Path, affordable ? Vector4.Lerp(color, Vector4.One, 0.35f) : new Vector4(1.0f, 0.25f, 0.2f, 1.0f));
                _hud.Hint = $"Track: {plan.NewLinks.Count} {(plan.NewLinks.Count == 1 ? "hex" : "hexes")} for {TywCosts.Format(plan.Cost)}" +
                            (affordable ? "" : "  (not enough -- lays what you can pay for)");
            }
            else
            {
                view.ShowPlan(null, color);
                _hud.Hint = hex is { } here ? Describe(game, here, action) : "";
            }

            view.ShowHover(hex, action?.Blocked == null && action?.Kind is not (null or TywActionKind.None)
                ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                : new Vector4(0.6f, 0.6f, 0.6f, 1.0f));

            if (!leftDown && _leftWasDown && hex is { } released && !_hud.Autoplay)
            {
                if (_dragged || action?.Kind == TywActionKind.Track)
                {
                    if (plan != null && plan.NewLinks.Count > 0)
                    {
                        var laid = game.BuildTrack(HumanSeat, plan);
                        Say(laid == 0 ? $"Can't afford track ({TywCosts.Format(TywCosts.Track)} a hex)" : $"Laid {laid} {(laid == 1 ? "hex" : "hexes")} of track");
                    }
                }
                else if (_pressHex == released)
                {
                    var why = game.Act(HumanSeat, released);
                    if (why != null)
                    {
                        Say($"{action?.Label}: {why}");
                    }
                }
            }

            _leftWasDown = leftDown;
        }

        private static string Describe(TywGame game, TywHex hex, TywAction? action)
        {
            var tile = game.Board[hex]!;
            var what = tile.Terrain switch
            {
                TywTerrain.Town => $"Town wants {tile.Demand}",
                TywTerrain.Depot => $"{game.Players[tile.BuildingOwner].Name}'s depot",
                TywTerrain.Lake => "Lake",
                _ when tile.IsResource => $"{tile.Terrain} ({tile.Produces}) {new string('*', tile.Richness)}",
                _ => tile.Terrain.ToString()
            };

            if (tile.Building is TywBuilding.Extractor or TywBuilding.Factory)
            {
                what += $"  {game.Players[tile.BuildingOwner].Name}'s {tile.Building.ToString().ToLowerInvariant()}, {MathF.Floor(tile.Stock)} waiting";
            }

            if (tile.IsDamaged(game.Time))
            {
                what += $"  SABOTAGED {tile.DamagedUntil - game.Time:0}s";
            }

            if (action is { Kind: not TywActionKind.None })
            {
                what += $"   --   click: {action.Label} ({TywCosts.Format(action.Cost)})";
                if (action.Blocked != null)
                {
                    what += $" -- {action.Blocked}";
                }
            }

            return what;
        }

        // ---- Scripts ---------------------------------------------------------------------------------------

        private void TickCommandFile()
        {
            if (IsLoading || string.IsNullOrEmpty(CommandFilePath))
            {
                return;
            }

            if (_commandRunner == null)
            {
                _commandRunner = new ManaCommandRunner(
                    CommandFilePath,
                    Context,
                    _webGpuContext == null ? null : _webGpuContext.CaptureFrameAsync,
                    _webGpuContext == null ? null : _webGpuContext.DumpEnvironmentFaces);
                // tyw_autoplay: a bot takes your seat. tyw_speed: frames = how many times real speed.
                _commandRunner.Register("tyw_autoplay", command => _hud.Autoplay = command.Visible ?? true);
                _commandRunner.Register("tyw_speed", command => _hud.Speed = Math.Max(1, command.Frames));
                _commandRunner.Register("tyw_zoom", command => _zoom = command.Position is { Length: > 0 } z ? z[0] : 0.85f);
                // Window pixels [x, y]; no position hands the pointer back to the mouse.
                _commandRunner.Register("tyw_pointer", command =>
                    _scriptPointer = command.Position is { Length: >= 2 } p ? new Vector2(p[0], p[1]) : null);
                _commandRunner.Register("tyw_press", _ => _scriptLeftDown = true);
                _commandRunner.Register("tyw_release", _ => _scriptLeftDown = false);
                _commandRunner.Register("tyw_hover_log", _ =>
                    ManaLog.WriteDiagnostic($"Command file: tyw hint '{_hud.Hint}', message '{_hud.Message}'"));
                _commandRunner.Register("tyw_log", _ =>
                {
                    if (_game is { } game)
                    {
                        foreach (var p in game.Players)
                        {
                            ManaLog.WriteDiagnostic($"Command file: tyw {p.Name} {game.VictoryPoints(p.Index)} VP ({game.VictoryBreakdown(p.Index)}), " +
                                                    $"{game.ClaimedHexes(p.Index)} hexes, {game.TrainCount(p.Index)} trains, time {game.Time:0}");
                        }
                    }
                });
            }

            _commandRunner.Tick();
        }
    }
}
