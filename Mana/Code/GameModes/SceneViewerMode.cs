using ImGuiNET;
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
        private ManaMaterial _steelMaterial = null!;
        private ManaMaterial _pirateGoldMaterial = null!;
        private ManaMaterial _brickMaterial = null!;
        private ManaMaterial _grassMaterial = null!;

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

                    // 2. Decode materials from Zip archives (CPU decoding)
                    Loading.Report(new ManaLoadingProgress("Loading used-stainless-steel2-bl.zip", 0.2f));
                    _steelMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/used-stainless-steel2-bl.zip");
                    world.Materials.Add(_steelMaterial);

                    Loading.Report(new ManaLoadingProgress("Loading pirate-gold-bl.zip", 0.4f));
                    _pirateGoldMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/pirate-gold-bl.zip");
                    world.Materials.Add(_pirateGoldMaterial);

                    Loading.Report(new ManaLoadingProgress("Loading beaten-down-brick-bl.zip", 0.6f));
                    _brickMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/beaten-down-brick-bl.zip");
                    world.Materials.Add(_brickMaterial);

                    Loading.Report(new ManaLoadingProgress("Loading wispy-grass-meadow-bl.zip", 0.7f));
                    _grassMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/whispy-grass-meadow-bl.zip");
                    world.Materials.Add(_grassMaterial);

                    // 3. Load Flight Helmet scene model
                    Loading.Report(new ManaLoadingProgress("Loading FlightHelmet GLTF model", 0.8f));
                    world.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
                    world.Scenes[1].Name = "Flight Helmet Scene";
                    var helmetNode = world.Scenes[1].Children[0];
                    helmetNode.Name = "Flight Helmet";
                    helmetNode.Position = new Vector3(2, 0, 2);

                    ManaMeshSceneNode goldIngot = new ManaMeshSceneNode(Context, new ManaIngot(Context, Colors.White, Vector3.One));
                    goldIngot.Name = "Pirate Gold Ingot";
                    goldIngot.MeshRenderer.Material = _pirateGoldMaterial;
                    goldIngot.Position = new Vector3(2, 1.1f, 5);
                    world.Scenes[1].Children[0].AddChild(goldIngot);

                    ManaMeshSceneNode steelBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
                    steelBall.Name = "Stainless Steel Ball";
                    steelBall.MeshRenderer.Material = _steelMaterial;
                    steelBall.Position = new Vector3(2, 0, 3);
                    world.Scenes[1].Children[0].AddChild(steelBall);

                    ManaMeshSceneNode pirateGoldBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
                    pirateGoldBall.Name = "Pirate Gold Ball";
                    pirateGoldBall.MeshRenderer.Material = _pirateGoldMaterial;
                    pirateGoldBall.Position = new Vector3(2, 0, 5);
                    world.Scenes[1].Children[0].AddChild(pirateGoldBall);

                    ManaMeshSceneNode brick = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, new Vector3(1.0f, 0.5f, 0.5f)));
                    brick.Name = "Beaten Brick Block";
                    brick.MeshRenderer.Material = _brickMaterial;
                    brick.Position = new Vector3(2, 0, 7);
                    world.Scenes[1].Children[0].AddChild(brick);

                    ManaMeshSceneNode grassBlock = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, Vector3.One));
                    grassBlock.Name = "Grass Block";
                    grassBlock.MeshRenderer.Material = _grassMaterial;
                    grassBlock.Position = new Vector3(2, 0, 9);
                    world.Scenes[1].Children[0].AddChild(grassBlock);

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

        protected override void DrawImGui(double deltaTime)
        {            
            ImGui.SetNextWindowPos(new Vector2(this._Window.Size.X - 200, 15));
            ImGui.Begin("Mana");
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");
            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.End();
        }

        protected override void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
            var utilityRatio = dockspaceSize.X > 0.0f
                ? Math.Clamp(360.0f / dockspaceSize.X, 0.20f, 0.34f)
                : 0.28f;

            ImGuiDockNative.DockBuilderSplitNode(dockspaceId, ImGuiDir.Right, utilityRatio, out var utilityDockId, out _);
            UtilityDockId = utilityDockId;
            OutlinerDockId = UtilityDockId;

            ImGuiDockNative.DockBuilderDockWindow("Outliner", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Inspector", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Camera", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Irradiance", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("World Perf", UtilityDockId);
            ImGuiDockNative.DockBuilderFinish(dockspaceId);
        }

        public override void Closing()
        {
            base.Closing();
        }
    }
}
