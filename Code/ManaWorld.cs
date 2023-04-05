using ImGuiNET;
using Mana.Code;
using Mana.Code.Shapes;
using Silk.NET.Assimp;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaWorld
    {
        public bool UseRayTracing = true;

        public List<Action> PostDrawFutures { get; set; }
        public List<Action> PreDrawFutures { get; set; }
        public List<Action> PreUpdateFutures { get; set; }
        public List<Action> PostUpdateFutures { get; set; }


        private List<Action> activePostDrawFutures { get; set; }
        private List<Action> activePreDrawFutures { get; set; }
        private List<Action> activePreUpdateFutures { get; set; }
        private List<Action> activePostUpdateFutures { get; set; }


        public List<ManaMesh> Meshes { get; set; }

        public List<ManaMaterial> Materials { get; set; }

        public List<ManaScene> Scenes { get; set; }

        public Matrix4x4 ProjectionMatrix { get; set; }

        public Matrix4x4 ViewMatrix { get; set; } = Matrix4x4.Identity;

        public ManaScene DefaultScene { get; set; }
        public ManaSceneNode CameraNode;
        public CameraComponent MainCamera;

        public ManaRenderTarget RenderTarget;

        public Assimp _AssetImporter { get; set; }
        public string ActivePath { get; set; }
        public ManaWorld() 
        {
            Meshes = new List<ManaMesh>();
            Scenes = new List<ManaScene>();
            Materials = new List<ManaMaterial>();

            activePostDrawFutures = new List<Action>();
            activePreDrawFutures = new List<Action>();
            activePreUpdateFutures = new List<Action>();
            activePostUpdateFutures = new List<Action>();

            PostDrawFutures = new List<Action>();
            PreDrawFutures = new List<Action>();
            PreUpdateFutures = new List<Action>();
            PostUpdateFutures = new List<Action>();


            CameraNode = new ManaSceneNode(this, Matrix4x4.Identity);
            CameraNode.Position = new Vector3(0, 0, 1);
            CameraNode.Name = "MainCamera";            
            MainCamera = new CameraComponent(CameraNode);
            CameraNode.Components.Add(MainCamera);

            RenderTarget = new ManaRenderTarget(this, Game.Instance._Window.Size.X, Game.Instance._Window.Size.Y);
            RenderTarget.AddRgbaBuffer("Colors");
            RenderTarget.AddDepthBuffer("Depth");
            RenderTarget.Build();
            RenderTarget.UnBind();

            DefaultScene = new ManaScene(this);
            Scenes.Add(DefaultScene);
            
            Game.Instance._Window.Resize += _Window_Resize;
        }

        private void _Window_Resize(Silk.NET.Maths.Vector2D<int> obj)
        {
            RenderTarget.Resize(obj.X, obj.Y);
        }

        public ManaSceneNode AddCube(Vector3 position, Vector4 color, float size)
        {
            return AddCube(position, color, new Vector3(size, size, size));
        }
        public ManaSceneNode AddSphere(Vector3 position, Vector4 color, float size)
        {
            return AddSphere(position, color, new Vector3(size, size, size));
        }

        public ManaSceneNode AddCube(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(this, Matrix4x4.CreateTranslation(position));
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaCube(this, color, size)));            
            DefaultScene.Children.Add(extraCube);
            return extraCube;
        }

        public ManaSceneNode AddSphere(Vector3 position, Vector4 color, Vector3 size)
        {
            ManaSceneNode extraCube = new ManaSceneNode(this, Matrix4x4.CreateTranslation(position));            
            extraCube.Components.Add(new MeshRendererComponent(extraCube, new ManaSphere(this, color, 0.5f)));
            extraCube.Scale = size;
            DefaultScene.Children.Add(extraCube);
            return extraCube;
        }

        public unsafe void LoadFromGltf(string sceneFile)
        {
            _AssetImporter = Silk.NET.Assimp.Assimp.GetApi();

            var scene = _AssetImporter.ImportFile(sceneFile, (uint)PostProcessSteps.Triangulate);
            ActivePath = System.IO.Path.GetDirectoryName(sceneFile);

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null)
            {
                var error = _AssetImporter.GetErrorStringS();
                throw new Exception(error);
            }

            for (int i = 0; i < scene->MNumMaterials; i++)
            {
                var material = scene->MMaterials[i];

                ManaMaterial manaMaterial = new ManaMaterial(this);
                manaMaterial.LoadFromGltf(material);
                Materials.Add(manaMaterial);
            }

            for (int i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];

                ManaMesh model = new ManaMesh(this);
                model.LoadFromGltf(mesh);

                Meshes.Add(model);
            }

            ManaScene manaScene = new ManaScene(this);            
            manaScene.LoadFromGltf(scene);
            Scenes.Add(manaScene);
        }

        private void RunFutures(List<Action> futures, List<Action> activeFutures)
        {
            activeFutures.AddRange(futures);
            futures.Clear();
            foreach (var activeFuture in activeFutures)
            {
                activeFuture();
            }
            activeFutures.Clear();
        }

        public void Update()
        {
            CameraNode.UpdateTransform();

            ViewMatrix = MainCamera._ViewMatrix;

            //Matrix4x4 projMatrix = Matrix4x4.CreateOrthographic(-800, 800 * aspect, -1000, 1000);
            //Matrix4x4 projMatrix = Matrix4x4.CreateOrthographic(-0.5f, -0.5f * aspect, -1000, 1000);
            //Matrix4x4 projMatrix = Matrix4x4.CreateOrthographic(-50.5f, -50.5f * aspect, -1000, 1000);

            float aspect = (float)Game.Instance._Window.Size.X / Game.Instance._Window.Size.Y;

            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(55.0f.ToRadians(), aspect, 0.001f, 1000.0f);

            RunFutures(PreUpdateFutures, activePreUpdateFutures);

            foreach (var scene in Scenes)
            {
                scene.Update();
            }

            RunFutures(PostUpdateFutures, activePostUpdateFutures);
        }

        public void Draw()
        {
            if (UseRayTracing) { RenderTarget.Bind(); }

            var gl = Game.Instance.gl;
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));


            RunFutures(PreDrawFutures, activePreDrawFutures);

            foreach (var scene in Scenes)
            {
                scene.Draw();
            }

            RunFutures(PostDrawFutures, activePostDrawFutures);


            if (UseRayTracing)
            {
                RenderTarget.UnBind();

                RenderTarget.DrawFullScreenQuad();
            }

            ImGui.Begin("Camera");

            ImGui.Checkbox("RayTrace", ref this.UseRayTracing);


            ImGui.End();



        }

    }
}
