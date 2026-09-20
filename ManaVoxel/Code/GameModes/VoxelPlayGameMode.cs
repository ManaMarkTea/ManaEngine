using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code.Planet;
using ManaVoxel.Code.Gameplay.Flight;
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
        private const float GroundSize = 256.0f;

        private readonly string _levelPath;
        private readonly CharacterSpawnService _characterSpawn = new();
        private readonly PlayHudState _hud = new();

        private ManaPawnController? _pawnController;
        private ManaPawnNode? _player;
        private ManaWebGpuContext? _webGpuContext;
        private PlanetNode? _planet;

        public VoxelPlayGameMode(string levelPath, ManaGraphicsBackendKind graphicsBackend)
            : base(graphicsBackend)
        {
            _levelPath = levelPath;
        }

        public override bool DeferGpuUploadsOnStartup => true;

        protected override bool EnableManaUi => true;

        protected override string? WindowIconPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");

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
            host.Root.Content = ManaUiPlayHud.Build(host.Root.Theme, () => _hud.Text, () => Context.World.GameState.FormatHud(),
                ManaUiPlayResults.Build(
                    host.Root.Theme,
                    Context.World.GameState,
                    Restart,
                    () => Context.Window.Close(),
                    "Quit"));
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
                    var envPath = world.EnvironmentReflectionSource.SourcePath;
                    var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                    envTexture.OriginalFilename = envPath;
                    envTexture.Load();

                    Loading.Report(new ManaLoadingProgress("Building ground", 0.35f));
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

                    // Extended downward from the visual floor (Y=0 stays the walkable surface) and much
                    // thicker than a flush 0.1 units -- see VoxelEditorGameMode's matching comment for why
                    // a slab that thin lets a character landing at normal fall speed tunnel straight
                    // through before Bepu's narrow phase ever sees an overlap.
                    const float GroundThickness = 2.0f;
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

            // The ground's centre, not the origin, when there is no Player Start -- a capsule dropped
            // exactly onto a box collider's corner edge can land with no contact generated at all (see
            // PlayModeController.DefaultSpawnPosition's matching comment), which reads as falling through
            // a floor that is visibly right there.
            var spawnPosition = start?.SpawnPosition ?? new Vector3(GroundSize * 0.5f, 0.0f, GroundSize * 0.5f);
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
            _player.Components.Add(new FlyingMountComponent(_player));

            _pawnController.Possess(_player);
            _pawnController.SetCameraMode(ManaCameraMode.Follow);

            // Before BeginPlay: the game state resets there, and course pieces read the player from it.
            Context.World.GameState.Player = _player;
            Context.World.BeginPlay();

            _hud.Text = $"{_player.Name}  --  WASD move, Shift run, Space jump (again in mid-air to fly; " +
                        "then Space climbs, Shift surges, A/D turn, S brakes; right-drag steers, left-drag looks), C camera, R restart, Esc quit";
        }

        public override void Update(double deltaTime)
        {
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);
            Context.World.GameState.Tick((float)deltaTime);
            _planet?.UpdateAutoLod(Context.World.MainCamera);
            _planet?.UpdateNearOcean(Context.World.MainCamera);
            _planet?.UpdateDetailChase();
            _planet?.ProcessPendingSplits();
            _planet?.ProcessPendingMerges();

            // Escape quits rather than releasing the character: there is no editor to hand control back to.
            var keyboard = Context.Input?.Keyboards.FirstOrDefault();
            var restartKeyDown = keyboard?.IsKeyPressed(Key.R) == true;
            if (restartKeyDown && !_restartKeyWasDown && !UiWantsKeyboard)
            {
                Restart();
            }

            _restartKeyWasDown = restartKeyDown;

            var quitKeyDown = keyboard?.IsKeyPressed(Key.Escape) == true;
            if (quitKeyDown && !_quitKeyWasDown)
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
