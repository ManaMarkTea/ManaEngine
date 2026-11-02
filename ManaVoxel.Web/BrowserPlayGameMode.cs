using Mana;
using ManaUI;
using Mana.Code.Nodes;
using Mana.Code.UI.ManaUi;
using ManaVoxel.Code.UI.ManaUi;
using ManaCore.Loading;
using ManaGraphics.Code.Graphics;
using ManaGraphics.Code.Graphics.Browser;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Services;
using System.ComponentModel;
using System.Diagnostics;

namespace ManaVoxel.Web
{
    /// <summary>Standalone browser play with synchronous loading on the browser main thread.</summary>
    public sealed class BrowserPlayGameMode : BaseGameMode
    {
        private const float GroundSize = 256.0f;

        private readonly string _levelPath;
        private readonly CharacterSpawnService _characterSpawn = new();
        // The level's own game modes (its rules), run as standalone play runs them.
        private readonly Mana.Code.GameModes.ManaGameModeRunner _gameModes = new();
        private ManaSceneNode? _ground;
        private bool _exitRequested;

        /// <summary>A running mode takes Escape for itself (its own pause), so the page's menu leaves it alone.</summary>
        public bool ModeHandlesEscape => _gameModes.HandlesExitKeys;

        public string TouchControlsName => _gameModes.TouchControls switch
        {
            Mana.Code.GameModes.ManaTouchControls.Swipe => "swipe",
            Mana.Code.GameModes.ManaTouchControls.Pointer => "pointer",
            Mana.Code.GameModes.ManaTouchControls.TwinStick => "twin",
            _ => "stick"
        };

        public bool PrefersLandscape => _gameModes.UiDesignSize != null;

        public void SetTouchAim(Vector2 aim)
        {
            if (_playSession != null)
            {
                _playSession.TouchAim = aim;
            }
        }

        private Mana.Code.GameModes.ManaPlaySession? _playSession;
        private ManaPawnController? _pawnController;
        private ManaPawnNode? _player;

        /// <summary>The character being played, once there is one.</summary>
        public ManaPawnNode? Player => _player;

        /// <summary>The game being played, for the page's menu to pause and restart.</summary>
        public static BrowserPlayGameMode? Current { get; private set; }

        /// <summary>Set while the page's menu is open.</summary>
        public static bool Paused { get; set; }

        /// <summary>Back to the start of the level, as R does.</summary>
        public void Restart()
        {
            if (_player != null)
            {
                Context.World.GameState.Restart(Context.World, _spawnPosition, _spawnYaw);
            }
        }

        public BrowserPlayGameMode(string levelPath, ManaGraphicsBackendKind graphicsBackend)
            : base(graphicsBackend)
        {
            _levelPath = levelPath;
            Current = this;

            // The loading screen is the HTML overlay in index.html, already on screen during the wasm
            // download -- the longest part of the wait, and one no in-engine screen can cover because no
            // C# is running yet. This forwards the same ManaLoadingViewModel the in-engine screens bind to.
            Loading.PropertyChanged += OnLoadingProgressChanged;
        }

        private void OnLoadingProgressChanged(object? sender, PropertyChangedEventArgs e)
        {
            var snapshot = Loading.Snapshot;

            if (LoadingCoordinator.Stage == ManaLoadingStage.Failed)
            {
                ManaBrowserInterop.ReportLoadingFailed(snapshot.Status);
                return;
            }

            ManaBrowserInterop.ReportLoadingProgress(snapshot.Status, snapshot.Fraction);
        }

        public override bool DeferGpuUploadsOnStartup => true;

        // The HUD, results and death screens are ManaUI, as on desktop.
        protected override bool EnableManaUi => true;

        private string _hudText = "Loading...";

        protected override void BuildManaUi(ManaUiHost host)
        {
            var playHud = ManaUiPlayHud.Build(host.Root.Theme, () => _hudText, () => _player != null ? Context.World.GameState.FormatHud() : "",
                ManaUiPlayResults.Build(host.Root.Theme, Context.World.GameState, Restart, BackToLevels, "Back to levels"),
                ManaUiDeathScreen.Build(host.Root.Theme, Context.World.GameState));

            // The level's game modes draw their own HUDs over it, and may hide it (see ManaLevelGameMode.ShowsPlayHud).
            _gameModes.UseTheme(host.Root.Theme);
            playHud.BindPolled(el => el.Visibility, () => _gameModes.ShowsPlayHud ? Visibility.Visible : Visibility.Collapsed);
            var layer = new Grid();
            layer.Children.Add(playHud);
            layer.Children.Add(_gameModes.HudLayer);
            host.Root.Content = layer;
        }

        // The page's level selector is the same page without ?level=.
        private static void BackToLevels()
        {
            using var location = System.Runtime.InteropServices.JavaScript.JSHost.GlobalThis.GetPropertyAsJSObject("location");
            if (location == null)
            {
                return;
            }
            var search = location.GetPropertyAsString("search") ?? "";
            var settings = search.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(parameter => !parameter.StartsWith("level=", StringComparison.Ordinal)
                    && !parameter.StartsWith("mode=", StringComparison.Ordinal));
            // Keep play explicit when the same bundle is hosted with an editor default on itch.io.
            location.SetProperty("search", "?mode=play&" + string.Join("&", settings));
        }

        public void RunWebGpu()
        {
            new GameSilkWebGpu(
                "ManaVoxel - Web",
                requestedBackend: null,
                () => Context.World.CreateRenderScene(),
                (window, input) => LoadForGraphicsBackend(window, input, new ManaWebGpuGraphicsDevice(null)),
                // Paused by the page's menu: the scene still draws, but nothing moves or counts down.
                deltaTime =>
                {
                    if (!Paused)
                    {
                        update(deltaTime);
                    }
                },
                deltaTime => Render(deltaTime),
                (webGpuContext, input) =>
                {
                    if (Context.GraphicsDevice is ManaWebGpuGraphicsDevice webGpuGraphicsDevice)
                    {
                        webGpuGraphicsDevice.Context = webGpuContext;
                    }

                    webGpuContext.UiRoot = ManaUi?.Root;
                }
            ).Run();
        }

        public override void Load()
        {
            LoadingCoordinator.Begin();

            try
            {
                Context.DeferGpuUploads = true;

                var world = Context.World;

                // The fractions below span 0..0.85, where ManaLoadingCoordinator.Update picks up
                // with "Initializing graphics renderer". They are weighted by what each step
                // actually costs in the browser -- the environment map is a 5 MB PNG decoded by
                // the browser, and the level pulls meshes out of a 5 MB zip, so those two dominate.
                Loading.Report(new ManaLoadingProgress("Preparing scene", 0.05f));

                _pawnController = new ManaPawnController(Context)
                {
                    GroundPlaneHeight = 0.0f,
                    ReleaseOnEscape = false,

                    // Same as the desktop play mode: no selection here, so the left button orbits.
                    MouseLookControlsEnabled = true
                };
                world.MainCamera.ShouldCaptureKeyboardInput = () => _pawnController?.IsCapturingMovementKeys == true;

                ManaSceneNode ground;
                lock (world.SceneLock)
                {
                    var shapes = new ManaShapeFactory(Context, world.DefaultScene);
                    ground = shapes.AddQuad(
                        Vector3.Zero,
                        Colors.White,
                        ManaQuadPlanes.XZ,
                        0,
                        GroundSize,
                        0,
                        GroundSize,
                        "Ground");
                }

                _ground = ground;

                // Extended downward from the visual floor (Y=0 stays the walkable surface) and much
                // thicker than a flush 0.1 units -- see VoxelEditorGameMode's matching comment for why a
                // slab that thin lets a character landing at normal fall speed tunnel straight through
                // before Bepu's narrow phase ever sees an overlap.
                const float GroundThickness = 2.0f;
                Context.Physics.AddBoxBody(
                    ground,
                    new Vector3(GroundSize, GroundThickness, GroundSize),
                    new Vector3(GroundSize * 0.5f, -GroundThickness * 0.5f, GroundSize * 0.5f));

                Loading.Report(new ManaLoadingProgress("Loading level", 0.45f));
                LoadLevel();

                Loading.Report(new ManaLoadingProgress("Spawning character", 0.75f));
                SpawnPlayer();

                LoadingCoordinator.CompleteBackgroundWork();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Browser play mode failed to load: {exception}");
                Console.WriteLine($"[BrowserPlay] Load failed: {exception}");

                // Fail() drives the overlay through OnLoadingProgressChanged, so the page shows the
                // reason instead of a blank canvas. The message alone there; the full trace here.
                LoadingCoordinator.Fail(exception);
            }
        }

        private void LoadLevel()
        {
            if (!File.Exists(_levelPath))
            {
                Console.WriteLine($"[BrowserPlay] Level '{_levelPath}' not found in the wasm filesystem.");
                return;
            }

            var level = ManaLevel.Load(_levelPath);
            var result = ManaLevelLoader.Load(level, Context, Context.World.DefaultScene);

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"[BrowserPlay] Level load warning: {warning}");
            }

            Console.WriteLine($"[BrowserPlay] Loaded '{level.Name}': {Context.World.DefaultScene.Children.Count} root nodes in the default scene.");

        }

        /// <summary>
        /// Spawns the character at the level's ManaPlayerStartNode, possesses it, and puts the
        /// camera behind it. Mirrors VoxelPlayGameMode's own spawn sequence.
        /// </summary>
        private void SpawnPlayer()
        {
            // A character only when the level's game modes ask for one (the Character mode); otherwise the world runs
            // under a free camera. Other modes are not run in the browser yet.
            var modes = Context.World.Scenes.SelectMany(scene => scene.GameModes).ToList();
            if (!Mana.Code.GameModes.ManaGameModeRunner.AnyWantsPlayerCharacter(modes))
            {
                if (_pawnController != null)
                {
                    _pawnController.MouseLookControlsEnabled = false;
                }

                Context.World.BeginPlay();
                BeginModes(modes);
                Console.WriteLine(modes.Count > 0
                    ? $"[BrowserPlay] Running game modes: {string.Join(", ", modes.Select(mode => mode.Type))}."
                    : "[BrowserPlay] Spawned a debug camera (the level has no Character game mode).");
                _hudText = modes.Count > 0 ? "" : "Debug camera  --  WASD and Q/E fly, hold right mouse to look, Esc menu";
                return;
            }

            if (!_characterSpawn.IsAvailable)
            {
                Console.WriteLine("[BrowserPlay] No character packs found under Assets/; running without a player.");
                return;
            }

            var start = ManaPlayerStartNode.Find(Context.World);
            if (start != null)
            {
                // The start marker is level-authoring furniture, not something to look at while playing.
                start.IsMarkerVisible = false;
            }

            // The ground's centre, not the origin, when there is no Player Start -- a capsule dropped
            // exactly onto a box collider's corner edge can land with no contact generated at all (see
            // PlayModeController.DefaultSpawnPosition's matching comment), which reads as falling through
            // a floor that is visibly right there.
            var spawnPosition = start?.SpawnPosition ?? new Vector3(GroundSize * 0.5f, 0.0f, GroundSize * 0.5f);
            spawnPosition.Y += 0.5f;

            lock (Context.World.SceneLock)
            {
                // The character the start asks for, at its height: speeds and jump scale with height, so the first
                // character found at the default height walked noticeably slower than desktop play.
                _player = _characterSpawn.SpawnFor(start, Context.World, spawnPosition, _pawnController);
            }

            if (_player == null || _pawnController == null)
            {
                Console.WriteLine($"[BrowserPlay] Character spawn failed: {_characterSpawn.LastError ?? "unknown reason"}");
                return;
            }

            _player.Rotation = new Vector3(0.0f, start?.SpawnYawRadians ?? 0.0f, 0.0f);
            _player.UpdateTransform();
            // The Character mode's jumps, as on desktop (PlayModeController.JumpsFor).
            _player.MaxJumpCount = modes.OfType<Mana.Code.GameModes.ManaCharacterGameMode>()
                .Select(mode => Math.Max(1, mode.MaxJumps)).DefaultIfEmpty(2).Max();

            _pawnController.Possess(_player);
            _pawnController.SetCameraMode(ManaCameraMode.Follow);

            // Components sit dormant until play actually begins -- TrainCarComponent, for one,
            // ships Paused = true so trains hold still in the editor and only run once BeginPlay
            // reaches them. Without this the level loads and renders correctly but nothing in it
            // moves, which looks like a broken animation rather than a lifecycle step never taken.
            // The player goes in first, as on desktop: the game state resets in BeginPlay, coins and
            // hazards find the player through it, and the player's own sounds only play for the pawn it names.
            _spawnPosition = spawnPosition;
            _spawnYaw = start?.SpawnYawRadians ?? 0.0f;
            Context.World.GameState.Player = _player;
            Context.World.BeginPlay();
            BeginModes(modes);

            Console.WriteLine($"[BrowserPlay] Spawned '{_player.Name}' at {spawnPosition}. WASD move, Shift run, Space jump, C camera.");
            _hudText = $"{_player.Name}  --  WASD move, Shift run, Space jump (again in mid-air to double jump), C camera, R restart, Esc menu";

        }

        private void BeginModes(List<Mana.Code.GameModes.ManaLevelGameMode> modes)
        {
            if (modes.Count == 0)
            {
                return;
            }

            // A mode that brings its own world has no use for the play floor (see ManaLevelGameMode.KeepsEditorFloor).
            if (!modes.All(mode => mode.KeepsEditorFloor) && _ground != null)
            {
                foreach (var renderer in _ground.GetComponents<MeshRendererComponent>())
                {
                    renderer.IsVisible = false;
                }
            }

            // A mode's Quit goes back to the level selector: there is no window to close.
            _playSession = new Mana.Code.GameModes.ManaPlaySession(Context, () => UiWantsMouse, () => UiWantsKeyboard,
                () => _exitRequested = true, standalone: true);
            _gameModes.Begin(modes, _playSession);
        }

        private bool _dismissedLoadingScreen;

        /// <summary>
        /// Takes the loading screen down one frame after loading finishes rather than the moment it
        /// does. <c>OnLoadingComplete</c> runs a line before the coordinator marks loading finished,
        /// and the frame carrying the first real picture has not been submitted at that point -- so
        /// hiding there swaps the screen for an empty canvas for a frame or two, which reads as the
        /// game having crashed right at the end of a fifteen-second wait.
        /// </summary>
        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);

            if (_dismissedLoadingScreen || IsLoading)
            {
                return;
            }

            _dismissedLoadingScreen = true;
            Loading.PropertyChanged -= OnLoadingProgressChanged;
            ManaBrowserInterop.HideLoadingScreen();
        }

        public override void Update(double deltaTime)
        {
            _gameModes.Tick((float)deltaTime);
            if (ManaUi != null)
            {
                ManaUi.DesignSize = _gameModes.UiDesignSize;
            }
            if (_exitRequested)
            {
                _exitRequested = false;
                BackToLevels();
                return;
            }

            // UiWantsKeyboard, not a raw poll: when a text field has focus, WASD is typing, not walking.
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);
            // The clock, deaths and respawns, as the desktop play mode runs them.
            var state = Context.World.GameState;
            state.Tick((float)deltaTime);

            // Also to the console as it changes, for scripted checks (webdrive.mjs reads the page log).
            if (state.Coins != _loggedCoins || state.Deaths != _loggedDeaths)
            {
                _loggedCoins = state.Coins;
                _loggedDeaths = state.Deaths;
                Console.WriteLine($"[BrowserPlay] Coins {state.Coins}/{state.TotalCoins}, deaths {state.Deaths}");
            }

            var restartKeyDown = Context.Input?.Keyboards.FirstOrDefault()?.IsKeyPressed(Silk.NET.Input.Key.R) == true;
            if (restartKeyDown && !_restartKeyWasDown && !UiWantsKeyboard && !_gameModes.HandlesExitKeys)
            {
                Restart();
            }
            _restartKeyWasDown = restartKeyDown;
        }

        private bool _restartKeyWasDown;
        private int _loggedCoins;
        private int _loggedDeaths;
        private Vector3 _spawnPosition;
        private float _spawnYaw;
    }
}
