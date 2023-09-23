using ImGuiNET;
using Silk.NET.Assimp;
using System.Diagnostics;

namespace Mana
{
    public struct ManaPerformanceLog
    {
        public string Name { get; set; }
        public TimeSpan Duration { get; set; }        
    }

    public class ManaWorld : ManaObject
    {
        public bool UseRayTracing = false;

        public ManaContext Context;

        private List<ManaPerformanceLog> manaPerformanceLogs = new List<ManaPerformanceLog>();

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

        public ManaRenderer Renderer;

        public Assimp _AssetImporter { get; set; }
        public string ActivePath { get; set; }
        public ManaWorld() : base(new ManaContext())
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
        }

        public void Initialize(ManaContext context)
        {
            Context = context;

            Context.Window.Resize += _Window_Resize;
            CameraNode = new ManaSceneNode(context, Matrix4x4.Identity);
            CameraNode.Position = new Vector3(0, 0, 5);
            CameraNode.Name = "MainCamera";
            MainCamera = new CameraComponent(CameraNode);
            CameraNode.Components.Add(MainCamera);


            //this.Renderer = new ManaDeferredRenderer(context);
            //this.Renderer = new ManaForwardRenderer(context);
            this.Renderer = new ManaIrradianceRenderer(context);
            this.Renderer.Initialize();

            DefaultScene = new ManaScene(context);
            Scenes.Add(DefaultScene);
        }

        private void _Window_Resize(Silk.NET.Maths.Vector2D<int> obj)
        {
            Renderer.Resize(obj.X, obj.Y);
        }
       

        public unsafe void LoadFromGltf(string sceneFile)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            _AssetImporter = Assimp.GetApi();

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

                ManaMaterial manaMaterial = ManaMaterialLoaderGltf.LoadFromGltf(Context, material);
                Materials.Add(manaMaterial);
            }

            for (int i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                ManaMesh model = ManaMeshLoaderGltf.LoadFromGltf(Context, mesh);
                Meshes.Add(model);
            }

            ManaScene manaScene = new ManaScene(Context);
            manaScene.LoadFromGltf(scene);
            Scenes.Add(manaScene);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog() { Name = $"Loading {sceneFile}", Duration = stopwatch.Elapsed });
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

            var window = Context.Window;

            float aspect = (float)window.Size.X / window.Size.Y;

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
            Renderer.Clear();

            Action drawScenes = () =>
            {
                RunFutures(PreDrawFutures, activePreDrawFutures);

                foreach (var scene in Scenes)
                {
                    scene.Draw();
                }

                RunFutures(PostDrawFutures, activePostDrawFutures);
            };

            Renderer.Render(drawScenes);


            ImGui.Begin("World Perf");

            foreach (var perf in manaPerformanceLogs)
            {
                ImGui.Text($"{ perf.Duration.Milliseconds}ms - {perf.Name}");
            }

            ImGui.End();

        }

    }
}



/*
 * 
 * 
 
if (UseRayTracing) { RenderTarget.Bind(); }

            var gl = Context.gl;


            Renderer.Clear();

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
  
 
 * */