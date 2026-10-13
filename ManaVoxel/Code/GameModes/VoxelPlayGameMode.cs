using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code.Planet;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.UI.ManaUi;
using ManaUI;
using Silk.NET.Input;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// The game, standing on its own: one level, one character, no editor. This is what
    /// <c>ManaVoxel --play &lt;level&gt;</c> runs, and what the editor's Play In New Window launches.
    /// <para>
    /// A separate mode rather than the editor with its panels switched off, because "no editor" is not a
    /// display setting. This one never builds the planet, the border overlays, the reference models or the
    /// demo props -- so it starts in the time it takes to read one level file, which is the only reason a
    /// standalone run is worth having next to the in-editor F5.
    /// </para>
    /// </summary>
    public sealed class VoxelPlayGameMode : BaseGameMode
    {
        // A plain far past any level (4 km), until the terrain system gives play a real ground: the editor's 256 m
        // room floor ended partway along the Sample Course, and the player fell off its edge.
        private const float GroundSize = 4096.0f;

        private readonly string _levelPath;
        private readonly CharacterSpawnService _characterSpawn = new();
        private readonly PlayHudState _hud = new();

        private ManaPawnController? _pawnController;
        private readonly ManaGameModeRunner _gameModes = new();
        private Mana.Code.Automation.ManaCommandRunner? _commandRunner;
        private bool _exitRequested;

        /// <summary>What play is with no character: the world running, a camera to look round it with.</summary>
        public const string DebugCameraHint = "Debug camera  --  WASD and Q/E fly, hold right mouse to look, Esc quit  (add a Character game mode to the level to play as one)";
        private ManaSceneNode? _ground;
        private ManaPawnNode? _player;
        private ManaWebGpuContext? _webGpuContext;
#if !MANA_BROWSER
        private PlanetNode? _planet;
#endif

        public VoxelPlayGameMode(string levelPath, ManaGraphicsBackendKind graphicsBackend)
            : base(graphicsBackend)
        {
            _levelPath = levelPath;
        }

        public override bool DeferGpuUploadsOnStartup => true;

        protected override bool EnableManaUi => true;

        protected override string? WindowIconPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");

        /// <summary>Set by <c>--command-file</c>: a script drives the run (screenshots, waits, a game mode's own commands).</summary>
        public string? CommandFilePath { get; set; }

        /// <summary>The character being played, once there is one. Exposed for tests and diagnostics.</summary>
        public ManaPawnNode? Player => _player;

        public void RunWebGpu(string? requestedBackend)
        {
            new GameSilkWebGpu(
                "ManaVoxel - Play",
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

        /// <summary>
        /// The whole UI: a controls hint. There is no menu bar and no dock host, so nothing on screen can
        /// take the pointer away from the game.
        /// </summary>
        protected override void BuildManaUi(ManaUiHost host)
        {
            var playHud = ManaUiPlayHud.Build(host.Root.Theme, () => _hud.Text, () => _player != null ? Context.World.GameState.FormatHud() : "",
                ManaUiPlayResults.Build(
                    host.Root.Theme,
                    Context.World.GameState,
                    Restart,
                    () => Context.Window.Close(),
                    "Quit"),
                ManaUiDeathScreen.Build(host.Root.Theme, Context.World.GameState));

            // The level's game modes draw their own HUDs over it, and may hide it (see ManaLevelGameMode.ShowsPlayHud).
            _gameModes.UseTheme(host.Root.Theme);
            playHud.BindPolled(el => el.Visibility, () => _gameModes.ShowsPlayHud ? Visibility.Visible : Visibility.Collapsed);
            var layer = new Grid();
            layer.Children.Add(playHud);
            layer.Children.Add(_gameModes.HudLayer);
            host.Root.Content = layer;
        }

        public override void Load()
        {
            LoadingCoordinator.Begin();

            var world = Context.World;

            // Nothing here selects or issues walk orders, so the left button is free to be a camera
            // orbit -- see ManaPawnController.MouseLookControlsEnabled.
            _pawnController = new ManaPawnController(Context)
            {
                GroundPlaneHeight = 0.0f,
                ReleaseOnEscape = false,
                MouseLookControlsEnabled = true
            };
            world.MainCamera.ShouldCaptureKeyboardInput = () => _pawnController?.IsCapturingMovementKeys == true;

            Task.Run(() =>
            {
                try
                {
                    Context.DeferGpuUploads = true;

                    Loading.Report(new ManaLoadingProgress("Loading environment reflection map", 0.1f));
                    // Only OpenGL reads the photo through the texture cache; WebGPU draws the sky into its reflections and
                    // decodes the photo itself, only for the Photo sky.
                    if (GraphicsBackend == ManaGraphicsBackendKind.OpenGL)
                    {
                        var envPath = world.EnvironmentReflectionSource.SourcePath;
                        var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                        envTexture.OriginalFilename = envPath;
                        envTexture.Load();
                    }

                    Loading.Report(new ManaLoadingProgress("Building ground", 0.35f));
                    ManaSceneNode ground;
                    lock (world.SceneLock)
                    {
                        var shapes = new ManaShapeFactory(Context, world.DefaultScene);
                        // Centred on the origin, as the editor's room floor is.
                        ground = shapes.AddQuad(
                            new Vector3(-GroundSize * 0.5f, 0.0f, -GroundSize * 0.5f),
                            Colors.White,
                            ManaQuadPlanes.XZ,
                            0,
                            GroundSize,
                            0,
                            GroundSize,
                            "Ground");
                    }

                    // Extended downward from the visual floor (Y=0 stays the walkable surface) and much
                    // thicker than a flush 0.1 units -- see VoxelEditorGameMode's matching comment for why
                    // a slab that thin lets a character landing at normal fall speed tunnel straight
                    // through before Bepu's narrow phase ever sees an overlap.
                    const float GroundThickness = 2.0f;
                    _ground = ground;
                    Context.Physics.AddBoxBody(
                        ground,
                        new Vector3(GroundSize, GroundThickness, GroundSize),
                        new Vector3(GroundSize * 0.5f, -GroundThickness * 0.5f, GroundSize * 0.5f));

                    Loading.Report(new ManaLoadingProgress($"Loading {Path.GetFileName(_levelPath)}", 0.6f));
                    LoadLevel();

                    LoadingCoordinator.CompleteBackgroundWork();
                }
                catch (Exception exception)
                {
                    Trace.TraceError($"Play mode failed to load: {exception}");
                    LoadingCoordinator.Fail(exception);
                }
            });
        }

        private void LoadLevel()
        {
            if (!File.Exists(_levelPath))
            {
                _hud.Text = $"Level '{Path.GetFileName(_levelPath)}' does not exist.";
                return;
            }

            var level = ManaLevel.Load(_levelPath);
            var result = ManaLevelLoader.Load(level, Context, Context.World.DefaultScene);
#if !MANA_BROWSER
            _planet = result.Planet;
#endif

            foreach (var warning in result.Warnings)
            {
                Trace.TraceWarning($"Level load warning: {warning}");
            }

        }

        /// <summary>
        /// Holds the loading screen until the WebGPU environment map is up. Without it the first frame of
        /// the game is drawn against an uninitialised environment -- the same wait the editor does, for the
        /// same reason.
        /// </summary>
        protected override bool CanCompleteLoading()
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return true;
            }

            return _webGpuContext is { IsEnvironmentInitializationPending: false };
        }

        /// <summary>
        /// The character is spawned here rather than with the rest of the level, because spawning one needs
        /// a settled world: the capsule is sized from the pawn's final rendered height and dropped into a
        /// physics scene that has to already contain the ground it lands on.
        /// </summary>
        protected override void OnLoadingComplete()
        {
            var start = ManaPlayerStartNode.Find(Context.World);
            if (start != null)
            {
                start.IsMarkerVisible = false;
            }

            // The character comes only when a level's game mode asks for it (the Character mode). Otherwise the world
            // runs and the camera flies free -- or a mode with a camera of its own drives it.
            var modes = Context.World.Scenes.SelectMany(scene => scene.GameModes).ToList();
            if (!ManaGameModeRunner.AnyWantsPlayerCharacter(modes))
            {
                if (_pawnController != null)
                {
                    _pawnController.MouseLookControlsEnabled = false;
                }

                Context.World.BeginPlay();
                BeginModes(modes);
                _hud.Text = modes.Count > 0 ? "" : DebugCameraHint;
                return;
            }

            // The ground's centre, not the origin, when there is no Player Start -- a capsule dropped
            // exactly onto a box collider's corner edge can land with no contact generated at all (see
            // PlayModeController.DefaultSpawnPosition's matching comment), which reads as falling through
            // a floor that is visibly right there.
            var spawnPosition = start?.SpawnPosition ?? Vector3.Zero;
            spawnPosition.Y += 0.5f;
            _spawnPosition = spawnPosition;
            _spawnYaw = start?.SpawnYawRadians ?? 0.0f;

            lock (Context.World.SceneLock)
            {
                _player = _characterSpawn.SpawnFor(start, Context.World, spawnPosition, _pawnController);
            }

            if (_player == null || _pawnController == null)
            {
                _hud.Text = _characterSpawn.LastError ?? "No character could be spawned.";
                return;
            }

            _player.Rotation = new Vector3(0.0f, start?.SpawnYawRadians ?? 0.0f, 0.0f);
            _player.UpdateTransform();
            // Double jump. FlyingMountComponent is parked -- see PlayModeController.
            _player.MaxJumpCount = PlayModeController.JumpsFor(modes);

            _pawnController.Possess(_player);
            _pawnController.SetCameraMode(ManaCameraMode.Follow);

            // Before BeginPlay: the game state resets there, and course pieces read the player from it.
            Context.World.GameState.Player = _player;
            Context.World.BeginPlay();
            BeginModes(modes);

            _hud.Text = $"{_player.Name}  --  WASD move, Shift run, Space jump (again in mid-air to double jump), C camera, R restart, Esc quit";
        }

        private void BeginModes(List<ManaLevelGameMode> modes)
        {
            if (modes.Count == 0)
            {
                return;
            }

            EnsureCommandRunner();
            // A mode that brings its own world has no use for the play floor (see ManaLevelGameMode.KeepsEditorFloor).
            if (!modes.All(mode => mode.KeepsEditorFloor) && _ground != null)
            {
                foreach (var renderer in _ground.GetComponents<MeshRendererComponent>())
                {
                    renderer.IsVisible = false;
                }
            }

            _gameModes.Begin(modes, new ManaPlaySession(Context, () => UiWantsMouse, () => UiWantsKeyboard, () => _exitRequested = true, standalone: true)
            {
                Commands = _commandRunner
            });
        }

        private void EnsureCommandRunner()
        {
            if (_commandRunner != null || string.IsNullOrEmpty(CommandFilePath))
            {
                return;
            }

            _commandRunner = new Mana.Code.Automation.ManaCommandRunner(
                CommandFilePath,
                Context,
                _webGpuContext == null ? null : _webGpuContext.CaptureFrameAsync,
                _webGpuContext == null ? null : _webGpuContext.DumpEnvironmentFaces);
        }

        public override void Closing()
        {
            _gameModes.End();
            base.Closing();
        }

        public override void Update(double deltaTime)
        {
            EnsureCommandRunner();
            _commandRunner?.Tick();
            _gameModes.Tick((float)deltaTime);
            if (_exitRequested)
            {
                Context.Window.Close();
                return;
            }

            _pawnController?.UpdateDriveInput(UiWantsKeyboard);
            Context.World.GameState.Tick((float)deltaTime);
#if !MANA_BROWSER
            _planet?.UpdateAutoLod(Context.World.MainCamera);
            _planet?.UpdateNearOcean(Context.World.MainCamera);
            _planet?.UpdateDetailChase();
            _planet?.ProcessPendingSplits();
            _planet?.ProcessPendingMerges();
#endif

            // Escape quits rather than releasing the character: there is no editor to hand control back to.
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            var modeKeys = _gameModes.HandlesExitKeys;
            var restartKeyDown = keyboard?.IsKeyPressed(Key.R) == true;
            if (restartKeyDown && !_restartKeyWasDown && !UiWantsKeyboard && !modeKeys)
            {
                Restart();
            }

            _restartKeyWasDown = restartKeyDown;

            var quitKeyDown = keyboard?.IsKeyPressed(Key.Escape) == true;
            if (quitKeyDown && !_quitKeyWasDown && !modeKeys)
            {
                Context.Window.Close();
            }

            _quitKeyWasDown = quitKeyDown;
        }

        private bool _quitKeyWasDown;
        private bool _restartKeyWasDown;
        private Vector3 _spawnPosition;
        private float _spawnYaw;

        private void Restart()
        {
            if (_player != null)
            {
                Context.World.GameState.Restart(Context.World, _spawnPosition, _spawnYaw);
            }
        }

        /// <summary>A single mutable string is the entire view model this mode has.</summary>
        private sealed class PlayHudState
        {
            public string Text { get; set; } = "Loading...";
        }
    }
}
