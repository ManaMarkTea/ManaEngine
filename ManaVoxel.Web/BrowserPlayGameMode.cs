using Mana;
using Mana.Code.Nodes;
using ManaGraphics.Code.Graphics;
using ManaVoxel.Code.Levels;
using System.Diagnostics;

namespace ManaVoxel.Web
{
    /// <summary>
    /// Play mode for the browser: one level, one character, no editor.
    /// <para>
    /// A sibling of ManaVoxel's <c>VoxelPlayGameMode</c> rather than that class reused directly --
    /// it lives in the ManaVoxel WinExe, whose ImGui/Assimp/editor surface has no browser-wasm
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
        private ManaPawnController? _pawnController;

        public BrowserPlayGameMode(string levelPath, ManaGraphicsBackendKind graphicsBackend)
            : base(graphicsBackend)
        {
            _levelPath = levelPath;
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
                deltaTime => update(deltaTime),
                deltaTime => Render(deltaTime),
                (webGpuContext, input) =>
                {
                    if (Context.GraphicsDevice is ManaWebGpuGraphicsDevice webGpuGraphicsDevice)
                    {
                        webGpuGraphicsDevice.Context = webGpuContext;
                    }

                    _ImGui = webGpuContext.CreateImGuiController(input);
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

                _pawnController = new ManaPawnController(Context)
                {
                    GroundPlaneHeight = 0.0f,
                    ReleaseOnEscape = false
                };
                world.MainCamera.ShouldCaptureKeyboardInput = () => _pawnController?.IsCapturingMovementKeys == true;

                var envPath = world.EnvironmentReflectionSource.SourcePath;
                var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                envTexture.OriginalFilename = envPath;
                envTexture.Load();

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

                Context.Physics.AddBoxBody(
                    ground,
                    new Vector3(GroundSize, 0.1f, GroundSize),
                    new Vector3(GroundSize * 0.5f, 0.0f, GroundSize * 0.5f));

                LoadLevel();

                LoadingCoordinator.CompleteBackgroundWork();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Browser play mode failed to load: {exception}");
                Console.WriteLine($"[BrowserPlay] Load failed: {exception}");
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

        public override void Update(double deltaTime)
        {
            // UiWantsKeyboard, not a raw poll: when a text field has focus, WASD is typing, not walking.
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);
        }
    }
}
