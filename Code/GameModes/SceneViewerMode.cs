using ImGuiNET;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class SceneViewerMode : BaseGameMode
    {
        public static SceneViewerMode Instance;
                

        public SceneViewerMode()
        {
            Instance = this;            
        }



        GameWebGpu gameWebGpu;

        public override void Load()
        {
            base.Load();

            var world = Context.World;

            var sun = world.AddSphere(new Vector3(5, 5, -5), Colors.Yellow, 2.0f);

            var sunMaterial = (sun.Components[0] as MeshRendererComponent).DynamicMaterial;
            sunMaterial.Emission.Value = Colors.Gold;
            sunMaterial.EmissionStrength = 10.0f;

            ManaMaterial steelMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/used-stainless-steel2-bl.zip");
            ManaMaterial pirateGoldMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/pirate-gold-bl.zip");
            ManaMaterial brickMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/beaten-down-brick-bl.zip");
            ManaMaterial grassMaterial = ManaMaterialLoaderZip.LoadFromZip(Context, "Assets/Materials/whispy-grass-meadow-bl.zip");

            world.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
            var helmetNode = world.Scenes[1].Children[0];
            helmetNode.Position = new Vector3(2, 0, 2);

            ManaMeshSceneNode goldIngot = new ManaMeshSceneNode(Context, new ManaIngot(Context, Colors.White, Vector3.One));
            goldIngot.Name = "PirateGoldIngot";
            goldIngot.MeshRenderer.Material = pirateGoldMaterial;
            goldIngot.Position = new Vector3(2, 1.1f, 5);
            world.Scenes[1].Children[0].AddChild(goldIngot);

            ManaMeshSceneNode steelBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
            steelBall.Name = "SteelBall";
            steelBall.MeshRenderer.Material = steelMaterial;
            steelBall.Position = new Vector3(2, 0, 3);
            world.Scenes[1].Children[0].AddChild(steelBall);

            ManaMeshSceneNode pirateGoldBall = new ManaMeshSceneNode(Context, new ManaSphere(Context, Colors.White, 0.5f));
            pirateGoldBall.Name = "PirateGoldBall";
            pirateGoldBall.MeshRenderer.Material = pirateGoldMaterial;
            pirateGoldBall.Position = new Vector3(2, 0, 5);
            world.Scenes[1].Children[0].AddChild(pirateGoldBall);

            ManaMeshSceneNode brick = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, new Vector3(1.0f, 0.5f, 0.5f)));
            brick.Name = "SteelBall";
            brick.MeshRenderer.Material = brickMaterial;
            brick.Position = new Vector3(2, 0, 7);
            world.Scenes[1].Children[0].AddChild(brick);

            ManaMeshSceneNode grassBlock = new ManaMeshSceneNode(Context, new ManaCube(Context, Colors.White, Vector3.One));
            grassBlock.Name = "GrassBlock";
            grassBlock.MeshRenderer.Material = grassMaterial;
            grassBlock.Position = new Vector3(2, 0, 9);
            world.Scenes[1].Children[0].AddChild(grassBlock);


            world.Update();
        }



        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

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


            Context.World.Update();
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
