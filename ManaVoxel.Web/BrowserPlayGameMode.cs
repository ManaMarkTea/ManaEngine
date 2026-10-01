using Mana;
using Mana.Code.Nodes;
using ManaCore.Loading;
using ManaGraphics.Code.Graphics;
using ManaGraphics.Code.Graphics.Browser;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Services;
using System.ComponentModel;
using System.Diagnostics;

namespace ManaVoxel.Web
{
    /// <summary>
    /// Play mode for the browser: one level, one character, no editor.
    /// <para>
    /// A sibling of ManaVoxel's <c>VoxelPlayGameMode</c> rather than that class reused directly --
    /// it lives in the ManaVoxel WinExe, whose editor surface has no browser-wasm
    /// build. The parts that matter (ground, level load, pawn controller, follow camera) are small
    /// and mirrored here; the level-loading types themselves ARE shared, linked in by source.
    /// </para>
    /// <para>
    /// The one real behavioural difference: loading happens inline instead of on a
    /// <c>Task.Run</c> background thread. Browser wasm is single-threaded by default, so a
    /// background load buys nothing and only risks the "loading screen waits on loading" deadlock
    /// already documented for the environment map. A bundled level is a filesystem read out of the
    /// wasm VFS -- fast enough that blocking one frame on it is invisible.
    /// </para>
    /// </summary>
    public sealed class BrowserPlayGameMode : BaseGameMode
    {
        private const float GroundSize = 256.0f;

        private readonly string _levelPath;
        private readonly CharacterSpawnService _characterSpawn = new();
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

            // Desktop shows the loading screen through ManaUI, which does not exist here: EnableManaUi
            // is off below. The browser's screen is the HTML overlay in index.html
            // instead, which has the decisive advantage of already being on screen during the wasm
            // download -- the longest part of the wait, and one no in-engine screen can ever cover
            // because no C# is running yet. This forwards the same ManaLoadingViewModel the
            // in-engine screens bind to, so all three show the same progress from the same source.
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

        protected override bool EnableManaUi => false;

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

                    webGpuContext.UiRoot = null;
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
                // ImageSharp, and the level pulls meshes out of a 5 MB zip, so those two dominate.
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
                _player = _characterSpawn.SpawnNextCharacter(Context.World, spawnPosition, _pawnController);
            }

            if (_player == null || _pawnController == null)
            {
                Console.WriteLine($"[BrowserPlay] Character spawn failed: {_characterSpawn.LastError ?? "unknown reason"}");
                return;
            }

            _player.Rotation = new Vector3(0.0f, start?.SpawnYawRadians ?? 0.0f, 0.0f);
            _player.UpdateTransform();
            // Double jump, matching desktop play (VoxelPlayGameMode).
            _player.MaxJumpCount = 2;

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

            Console.WriteLine($"[BrowserPlay] Spawned '{_player.Name}' at {spawnPosition}. WASD move, Shift run, Space jump, C camera.");

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
            // UiWantsKeyboard, not a raw poll: when a text field has focus, WASD is typing, not walking.
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);
            // The clock, deaths and respawns, as the desktop play mode runs them.
            var state = Context.World.GameState;
            state.Tick((float)deltaTime);

            // There is no HUD in the browser yet, so the score goes to the console as it changes.
            if (state.Coins != _loggedCoins || state.Deaths != _loggedDeaths)
            {
                _loggedCoins = state.Coins;
                _loggedDeaths = state.Deaths;
                Console.WriteLine($"[BrowserPlay] Coins {state.Coins}/{state.TotalCoins}, deaths {state.Deaths}");
            }

            var restartKeyDown = Context.Input?.Keyboards.FirstOrDefault()?.IsKeyPressed(Silk.NET.Input.Key.R) == true;
            if (restartKeyDown && !_restartKeyWasDown && !UiWantsKeyboard)
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
