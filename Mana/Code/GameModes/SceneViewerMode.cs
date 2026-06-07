using ImGuiNET;
using Mana.Code.Nodes;

namespace Mana.Code
{
    public class SceneViewerMode : BaseGameMode
    {
        public static SceneViewerMode Instance = null!;


        public SceneViewerMode()
        {
            Instance = this;
        }



        GameWebGpu? gameWebGpu;

        public override void Load()
        {
            base.Load();

            var world = Context.World;

            ManaShapeFactory shapes = new ManaShapeFactory(Context, world.DefaultScene);

            var sun = shapes.AddSphere(new Vector3(5, 5, -5), Colors.Yellow, 2.0f);
            sun.Name = "Debug Sun";

            if (sun.Components[0] is not MeshRendererComponent sunRenderer)
            {
                return;
            }

            var sunMaterial = sunRenderer.DynamicMaterial;
            sunMaterial.Name = "Debug Sun Material";
            sunMaterial.Emission.Value = Colors.Gold;
            sunMaterial.EmissionStrength = 10.0f;

            ManaMaterial steelMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/used-stainless-steel2-bl.zip");
            ManaMaterial pirateGoldMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/pirate-gold-bl.zip");
            ManaMaterial brickMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/beaten-down-brick-bl.zip");
            ManaMaterial grassMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/whispy-grass-meadow-bl.zip");

            world.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
            world.Scenes[1].Name = "Flight Helmet Scene";
            var helmetNode = world.Scenes[1].Children[0];
            helmetNode.Name = "Flight Helmet";
            helmetNode.Position = new Vector3(2, 0, 2);

            ManaMeshSceneNode goldIngot = new ManaMeshSceneNode(Context, new ManaIngot(Context, Colors.White, Vector3.One));
            goldIngot.Name = "Pirate Gold Ingot";
            goldIngot.MeshRenderer.Material = pirateGoldMaterial;
            goldIngot.Position = new Vector3(2, 1.1f, 5);
            world.Scenes[1].Children[0].AddChild(goldIngot);

            ManaMeshSceneNode steelBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
            steelBall.Name = "Stainless Steel Ball";
            steelBall.MeshRenderer.Material = steelMaterial;
            steelBall.Position = new Vector3(2, 0, 3);
            world.Scenes[1].Children[0].AddChild(steelBall);

            ManaMeshSceneNode pirateGoldBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
            pirateGoldBall.Name = "Pirate Gold Ball";
            pirateGoldBall.MeshRenderer.Material = pirateGoldMaterial;
            pirateGoldBall.Position = new Vector3(2, 0, 5);
            world.Scenes[1].Children[0].AddChild(pirateGoldBall);

            ManaMeshSceneNode brick = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, new Vector3(1.0f, 0.5f, 0.5f)));
            brick.Name = "Beaten Brick Block";
            brick.MeshRenderer.Material = brickMaterial;
            brick.Position = new Vector3(2, 0, 7);
            world.Scenes[1].Children[0].AddChild(brick);

            ManaMeshSceneNode grassBlock = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, Vector3.One));
            grassBlock.Name = "Grass Block";
            grassBlock.MeshRenderer.Material = grassMaterial;
            grassBlock.Position = new Vector3(2, 0, 9);
            world.Scenes[1].Children[0].AddChild(grassBlock);

            
            world.MainCamera.AddDebugCameraRayTest(shapes);
            world.Update();
        }



        public override void Update(double deltaTime)
        {
        }

        protected override void DrawImGui(double deltaTime)
        {            
            ImGui.SetNextWindowPos(new Vector2(this._Window.Size.X - 200, 15));
            ImGui.Begin("Mana");
            if (ImGui.Button("WebGpu"))
            {
                gameWebGpu = new GameWebGpu();
                gameWebGpu.RunAsync();
            }
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
            ImGuiDockNative.DockBuilderDockWindow("Camera", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Irradiance", UtilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("World Perf", UtilityDockId);
            ImGuiDockNative.DockBuilderFinish(dockspaceId);
        }

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
        }

        public override void Closing()
        {
            base.Closing();
        }



    }
}
