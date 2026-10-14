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

        private string? _levelPath;
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

        /// <param name="levelPath">The level to play, or null to open on the level select (<see cref="OffersLevelSelect"/>).</param>
        public VoxelPlayGameMode(string? levelPath, ManaGraphicsBackendKind graphicsBackend)
            : base(graphicsBackend)
        {
            _levelPath = levelPath;
        }

        public override bool DeferGpuUploadsOnStartup => true;

        protected override bool EnableManaUi => true;

        protected override string? WindowIconPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");

        /// <summary>
        /// A play-only app (the phone): a start screen of the published levels, and a pause button in play that goes
        /// back to it. Off for <c>--play</c>, which plays its one level and quits.
        /// </summary>
        public bool OffersLevelSelect { get; set; }

        /// <summary>
        /// The screen's edges the HUD keeps clear of, in UI points -- a phone's notch or Dynamic Island and home bar.
        /// Null on a desktop window, which has none.
        /// </summary>
        public Func<Thickness>? SafeArea { get; set; }

        /// <summary>
        /// How much of the bottom a phone's on-screen controls take, in UI points (0 for none): a bar is drawn there
        /// for them, the game's picture is fitted above it and the HUD keeps clear of it.
        /// </summary>
        public Func<float>? ControlsBar { get; set; }

        private UiRoot? _uiRoot;

        /// <summary>Music looped while the start screen is up (a path under the working folder), or null for none.</summary>
        public string? MenuMusic { get; set; }

        private ManaCore.Audio.IManaAudioVoice? _menuMusic;
        private bool _menuMusicLoading;

        /// <summary>The start screen's music: on while it is up, off when a level starts.</summary>
        private async void SyncMenuMusic()
        {
            var wanted = _levelSelectOpen && _pendingLevel == null && !string.IsNullOrEmpty(MenuMusic);
            if (!wanted)
            {
                _menuMusic?.Stop();
                _menuMusic = null;
                return;
            }
            if (_menuMusic != null || _menuMusicLoading)
            {
                return;
            }
            _menuMusicLoading = true;
            try
            {
                var clip = await Context.Audio.LoadClipAsync(MenuMusic!);
                if (_levelSelectOpen && _pendingLevel == null)
                {
                    _menuMusic = Context.Audio.Play(clip, new ManaCore.Audio.ManaAudioPlayOptions
                    {
                        Loop = true,
                        Bus = ManaCore.Audio.ManaAudioBus.Music,
                        Volume = 0.5f
                    });
                }
            }
            catch (Exception exception)
            {
                Trace.TraceWarning($"No menu music '{MenuMusic}': {exception.Message}");
                MenuMusic = null;
            }
            finally
            {
                _menuMusicLoading = false;
            }
        }

        /// <summary>The start screen is up: a phone hides its game controls.</summary>
        public bool IsLevelSelectOpen => _levelSelectOpen;

        /// <summary>The on-screen controls the level's modes want on a touch device.</summary>
        public ManaTouchControls TouchControls => _gameModes.TouchControls;

        /// <summary>The pause panel is up; nothing in the world moves.</summary>
        public bool IsPaused => _paused;

        private bool _levelSelectOpen;
        private bool _paused;
        private string? _pendingLevel;
        private int _pendingFrames;

        /// <summary>How to play, under the character's name at the bottom; a phone describes its touch controls instead.</summary>
        public string ControlsHint { get; set; } = "WASD move, Shift run, Space jump (again in mid-air to double jump), C camera, R restart, Esc quit";

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
                // Paused: the scene still draws and the menu still works, but nothing moves or counts down.
                deltaTime =>
                {
                    if (!_paused)
                    {
                        update(deltaTime);
                    }
                },
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
                    // With a start screen (the phone, whose window cannot close), leaving goes back to it.
                    () =>
                    {
                        if (OffersLevelSelect)
                        {
                            ShowLevelSelect();
                        }
                        else
                        {
                            Context.Window.Close();
                        }
                    },
                    OffersLevelSelect ? "Choose Level" : "Quit"),
                ManaUiDeathScreen.Build(host.Root.Theme, Context.World.GameState),
                Context.World.GameState);

            // The level's game modes draw their own HUDs over it, and may hide it (see ManaLevelGameMode.ShowsPlayHud).
            _gameModes.UseTheme(host.Root.Theme);
            playHud.BindPolled(el => el.Visibility, () => _gameModes.ShowsPlayHud ? Visibility.Visible : Visibility.Collapsed);
            // Inside the safe area, so nothing sits under a phone's notch or home bar -- or its controls' bar.
            var hud = new Grid();
            hud.Children.Add(playHud);
            hud.Children.Add(_gameModes.HudLayer);
            if (SafeArea != null || ControlsBar != null)
            {
                hud.BindPolled(el => el.Margin, () =>
                {
                    var safe = SafeArea?.Invoke() ?? default;
                    return new Thickness(safe.Left, safe.Top, safe.Right, MathF.Max(safe.Bottom, ControlsBarHeight));
                });
            }
            var layer = new Grid();
            _uiRoot = host.Root;
            if (ControlsBar != null)
            {
                layer.Children.Add(ManaUiPlayMenus.ControlsBar(host.Root.Theme, () => ControlsBarHeight));
            }
            layer.Children.Add(hud);
            if (OffersLevelSelect)
            {
                var theme = host.Root.Theme;
                hud.Children.Add(ManaUiPlayMenus.Pause(theme, () => _levelPath != null && _pendingLevel == null && !_levelSelectOpen && !IsLoading, () => _paused,
                    () => _paused = true, () => _paused = false, () => { _paused = false; Restart(); }, ShowLevelSelect));
                layer.Children.Add(ManaUiPlayMenus.LevelSelect(theme, ManaPlayableLevels.List(), level => ChooseLevel(level.Path),
                    () => _levelSelectOpen && _pendingLevel == null, SafeArea));
            }
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

                    _ground = ground;
                    SetGroundSolid(true);

                    if (_levelPath != null)
                    {
                        Loading.Report(new ManaLoadingProgress($"Loading {Path.GetFileName(_levelPath)}", 0.6f));
                        LoadLevel();
                    }

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
            if (_levelPath == null || !File.Exists(_levelPath))
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
            if (_levelPath == null)
            {
                _levelSelectOpen = true;
                _hud.Text = string.Empty;
                return;
            }
            StartPlay();
        }

        /// <summary>The loaded level's play: its character (when a mode asks for one), its components and its modes.</summary>
        private void StartPlay()
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

            // A mode that moves the character itself (a runner) gets it unpossessed: no walking keys, no follow camera.
            if (!ManaGameModeRunner.AnyDrivesPlayerCharacter(modes))
            {
                _pawnController.Possess(_player);
                _pawnController.SetCameraMode(ManaCameraMode.Follow);
            }

            // Before BeginPlay: the game state resets there, and course pieces read the player from it.
            Context.World.GameState.Player = _player;
            Context.World.BeginPlay();
            BeginModes(modes);

            _hud.Text = string.IsNullOrEmpty(ControlsHint) ? string.Empty : $"{_player.Name}  --  {ControlsHint}";
        }

        private const float GroundThickness = 2.0f;
        private Mana.Code.Components.ManaCollisionComponent? _groundBody;

        // Extended downward from the visual floor (Y=0 stays the walkable surface) and much thicker than a flush
        // 0.1 units: a slab that thin lets a character landing at normal fall speed tunnel straight through.
        private void SetGroundSolid(bool solid)
        {
            if (_ground == null)
            {
                return;
            }
            foreach (var renderer in _ground.GetComponents<MeshRendererComponent>())
            {
                renderer.IsVisible = solid;
            }
            if (!solid && _groundBody != null)
            {
                Context.Physics.RemoveBody(_groundBody.Body);
                _groundBody = null;
            }
            else if (solid && _groundBody == null)
            {
                _groundBody = Context.Physics.AddBoxBody(_ground, new Vector3(GroundSize, GroundThickness, GroundSize),
                    new Vector3(GroundSize * 0.5f, -GroundThickness * 0.5f, GroundSize * 0.5f));
            }
        }

        private void BeginModes(List<ManaLevelGameMode> modes)
        {
            // A mode that brings its own world, or whose gaps are falls, has no floor -- picture or body
            // (see ManaLevelGameMode.KeepsEditorFloor); the next level may want it back.
            SetGroundSolid(modes.All(mode => mode.KeepsEditorFloor));
            if (modes.Count == 0)
            {
                return;
            }

            EnsureCommandRunner();

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
            // The start screen's two ways round, for scripts: play a level, back to the list.
            _commandRunner.Register("open_level", command => ChooseLevel(Mana.Code.Automation.ManaCommandRunner.RequirePath(command)));
            _commandRunner.Register("levels", _ => ShowLevelSelect());
            _commandRunner.Register("ui_click", command => ClickLabel(command.Name));
        }

        public override void Closing()
        {
            _gameModes.End();
            base.Closing();
        }

        /// <summary>Plays another level: the start screen's pick. Loaded a frame later, once "Loading" is on screen.</summary>
        public void ChooseLevel(string path)
        {
            _pendingLevel = path;
            _pendingFrames = 0;
            _levelSelectOpen = false;
            _paused = false;
            _hud.Text = $"Loading {Path.GetFileNameWithoutExtension(path)}...";
        }

        /// <summary>Leaves the level for the start screen.</summary>
        public void ShowLevelSelect()
        {
            _paused = false;
            UnloadLevel();
            _levelSelectOpen = true;
        }

        private void LoadPendingLevel()
        {
            var path = _pendingLevel!;
            _pendingLevel = null;
            UnloadLevel();
            _levelPath = path;
            try
            {
                LoadLevel();
                StartPlay();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Could not play {path}: {exception}");
                UnloadLevel();
                _levelSelectOpen = true;
            }
        }

        /// <summary>
        /// Takes the level out of the world -- its modes, its character, everything it placed -- leaving the ground,
        /// so another can load in its place without rebuilding the renderer.
        /// </summary>
        private void UnloadLevel()
        {
            _gameModes.End();
            Context.World.EndPlay();
            if (_player is { } player)
            {
                lock (Context.World.SceneLock)
                {
                    player.DisablePhysics();
                    player.Parent?.RemoveChild(player);
                    // The character came in its own glTF scene; an empty one left behind would pile up level after level.
                    for (var i = Context.World.Scenes.Count - 1; i >= 0; i--)
                    {
                        var scene = Context.World.Scenes[i];
                        if (scene.Children.Remove(player) && !ReferenceEquals(scene, Context.World.DefaultScene) && scene.Children.Count == 0)
                        {
                            Context.World.Scenes.RemoveAt(i);
                        }
                    }
                }
                _pawnController?.Unregister(player);
            }
            _player = null;
            Context.World.GameState.Player = null;
            ManaLevelLoader.RemoveAuthoredNodes(Context, _ground);
            lock (Context.World.SceneLock)
            {
                foreach (var scene in Context.World.Scenes)
                {
                    scene.GameModes.Clear();
                }
            }
#if !MANA_BROWSER
            _planet = null;
#endif
            if (_ground != null)
            {
                foreach (var renderer in _ground.GetComponents<MeshRendererComponent>())
                {
                    renderer.IsVisible = true;
                }
            }
            _levelPath = null;
            _hud.Text = string.Empty;
        }

        /// <summary>
        /// Clicks the shown text that reads <paramref name="label"/> (a button's label) as a pointer would -- through the
        /// UI's hit testing, so something lying over it gets the click instead, as it would for a finger.
        /// </summary>
        private void ClickLabel(string? label)
        {
            if (_uiRoot?.Content is not { } content || string.IsNullOrEmpty(label))
            {
                return;
            }
            var text = Shown(content).OfType<TextBlock>().FirstOrDefault(block => block.Text == label);
            if (text == null)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: ui_click found no '{label}' on screen.");
                return;
            }
            var at = new System.Numerics.Vector2(text.Bounds.X + text.Bounds.Width * 0.5f, text.Bounds.Y + text.Bounds.Height * 0.5f);
            _uiRoot.Input.InjectPointerMoved(at);
            _uiRoot.Input.InjectPointerPressed(at, PointerButton.Left, Context.Window.Time);
            _uiRoot.Input.InjectPointerReleased(at, PointerButton.Left);
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Command file: ui_click '{label}' at {at}");

            static IEnumerable<UiElement> Shown(UiElement element)
            {
                if (element.Visibility != Visibility.Visible)
                {
                    yield break;
                }
                yield return element;
                foreach (var child in element.VisualChildren.SelectMany(Shown))
                {
                    yield return child;
                }
            }
        }

        private float ControlsBarHeight => ControlsBar?.Invoke() ?? 0.0f;

        /// <summary>The game's picture fitted above the controls' bar (the whole window when there is none).</summary>
        private void FitViewportAboveControls()
        {
            if (ControlsBar == null || _uiRoot is not { } root || root.Size.Y <= 0.0f)
            {
                return;
            }
            // As a share of the UI's height: the camera measures the window in its own units.
            var size = Context.Window.Size;
            var shown = Math.Clamp(1.0f - ControlsBarHeight / root.Size.Y, 0.2f, 1.0f);
            Mana.Code.UI.ManaEditorGizmos.Viewport = new UiRect(0.0f, 0.0f, size.X, size.Y * shown);
        }

        public override void Update(double deltaTime)
        {
            SyncMenuMusic();
            FitViewportAboveControls();
            // Two frames: the one that drew "Loading", then the load.
            if (_pendingLevel != null && ++_pendingFrames > 2)
            {
                LoadPendingLevel();
                return;
            }

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
            if (_gameModes.Restart())
            {
                return;
            }
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
