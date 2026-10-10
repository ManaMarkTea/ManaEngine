using Mana.Code.Nodes;
using System.Diagnostics;
using Silk.NET.OpenGL;
using ManaCore.Loading;
using System.Numerics;

namespace Mana.Code
{
    public class SceneViewerMode : BaseGameMode
    {
        public static SceneViewerMode Instance = null!;

        public override bool DeferGpuUploadsOnStartup => true;

        private ManaMeshSceneNode _lightningBall = null!;
        private ManaMeshSceneNode _liquidBall = null!;
        private ManaShapeFactory _shapes = null!;
        private ManaSceneNode _sun = null!;

        public SceneViewerMode()
        {
            Instance = this;
        }

        public override void Load()
        {
            base.Load();

            LoadingCoordinator.Begin();

            var world = Context.World;

            // Spawn background task to do CPU-heavy resource loading off the main thread
            Task.Run(() =>
            {
                try
                {
                    Context.DeferGpuUploads = true;

                    // 0. Pre-decode environment reflection map CPU data
                    Loading.Report(new ManaLoadingProgress("Loading environment reflection map", 0.05f));
                    var envPath = world.EnvironmentReflectionSource.SourcePath;
                    var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                    envTexture.OriginalFilename = envPath;
                    envTexture.Load();

                    // 1. Generate shapes CPU data
                    Loading.Report(new ManaLoadingProgress("Generating procedural shapes", 0.1f));
                    _shapes = new ManaShapeFactory(Context, world.DefaultScene);

                    _sun = _shapes.AddSphere(new Vector3(5, 5, -5), Colors.Yellow, 2.0f);
                    _sun.Name = "Debug Sun";

                    _lightningBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 1.0f));
                    _lightningBall.Name = "Lightning Ball";
                    _lightningBall.Position = new Vector3(10, 10, 10);
                    _lightningBall.Scale = new Vector3(2, 2, 2);
                    _lightningBall.MeshRenderer.DrawAfterScene = true;
                    world.DefaultScene.Children.Add(_lightningBall);

                    _liquidBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 1.0f));
                    _liquidBall.Name = "Liquid Ball";
                    _liquidBall.Position = new Vector3(14.75f, 10, 10);
                    _liquidBall.Scale = new Vector3(2, 2, 2);
                    _liquidBall.MeshRenderer.DrawAfterScene = true;
                    world.DefaultScene.Children.Add(_liquidBall);

                    // 2. Load Flight Helmet scene model
                    Loading.Report(new ManaLoadingProgress("Loading FlightHelmet GLTF model", 0.8f));
                    world.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
                    world.Scenes[1].Name = "Flight Helmet Scene";
                    var helmetNode = world.Scenes[1].Children[0];
                    helmetNode.Name = "Flight Helmet";
                    helmetNode.Position = new Vector3(2, 0, 2);

                    LoadingCoordinator.CompleteBackgroundWork();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during background asset loading: {ex}");
                    LoadingCoordinator.Fail(ex);
                }
            });
        }

        protected override void OnLoadingComplete()
        {
            // Apply settings and shaders
            var sunRenderer = _sun.Components[0] as MeshRendererComponent;
            if (sunRenderer != null)
            {
                var sunMaterial = sunRenderer.DynamicMaterial;
                sunMaterial.Name = "Debug Sun Material";
                sunMaterial.Emission.Value = Colors.Gold;
                sunMaterial.EmissionStrength = 10.0f;
            }

            _lightningBall.MeshRenderer.DynamicMaterial.Name = "Lightning Ball Material";
            _lightningBall.MeshRenderer.DynamicMaterial.Shader = new ManaLightningBallShader(Context);

            var liquidMaterial = _liquidBall.MeshRenderer.DynamicMaterial;
            liquidMaterial.Name = "Liquid Ball Material";
            liquidMaterial.Shader = new ManaLiquidBallShader(Context);

            Context.World.MainCamera.AddDebugCameraRayTest(_shapes);
            Context.World.Update();
        }

        public override void Update(double deltaTime)
        {
            // Subclass game logic (runs after loading completes)
        }

        // The fps readout and the dock layout that used to sit here were both drawn by the old immediate
        // -mode UI. This harness is a scene viewer, not an editor -- it never had ManaUI panels to move
        // them into, so they are simply gone; the perf numbers live in the editor's own status bar.

        public override void Closing()
        {
            base.Closing();
        }
    }
}
