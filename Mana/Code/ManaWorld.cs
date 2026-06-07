using ImGuiNET;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.UI;
using Mana.Code.Shapes.Loaders;
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

        public new ManaContext Context = null!;

        private List<ManaPerformanceLog> manaPerformanceLogs = new List<ManaPerformanceLog>();
        private readonly SceneOutlinerPanelView sceneOutlinerPanelView = new SceneOutlinerPanelView();

        public List<Action> PostDrawFutures { get; set; }
        public List<Action> ImGuiFutures { get; set; }
        public List<Action> PreDrawFutures { get; set; }
        public List<Action> PreUpdateFutures { get; set; }
        public List<Action> PostUpdateFutures { get; set; }


        private List<Action> activePostDrawFutures { get; set; }
        private List<Action> activeImGuiFutures { get; set; }
        private List<Action> activePreDrawFutures { get; set; }
        private List<Action> activePreUpdateFutures { get; set; }
        private List<Action> activePostUpdateFutures { get; set; }


        public List<ManaMesh> Meshes { get; set; }

        public List<ManaMaterial> Materials { get; set; }

        public List<ManaScene> Scenes { get; set; }

        public Matrix4x4 ProjectionMatrix { get; set; }

        public Matrix4x4 ViewMatrix { get; set; } = Matrix4x4.Identity;

        public ManaScene DefaultScene { get; set; } = null!;
        public ManaSceneNode CameraNode = null!;
        public CameraComponent MainCamera = null!;

        public ManaRenderer Renderer = null!;
        
        public ManaWorld() : base(null!)
        {
            Meshes = new List<ManaMesh>();
            Scenes = new List<ManaScene>();
            Materials = new List<ManaMaterial>();

            activePostDrawFutures = new List<Action>();
            activeImGuiFutures = new List<Action>();
            activePreDrawFutures = new List<Action>();
            activePreUpdateFutures = new List<Action>();
            activePostUpdateFutures = new List<Action>();

            PostDrawFutures = new List<Action>();
            ImGuiFutures = new List<Action>();
            PreDrawFutures = new List<Action>();
            PreUpdateFutures = new List<Action>();
            PostUpdateFutures = new List<Action>();
        }

        public void Initialize(ManaContext context)
        {
            Context = context;

            Context.Window.Resize += _Window_Resize;
            CameraNode = new ManaSceneNode(context, Matrix4x4.Identity);
            CameraNode.Position = new Vector3(20, 32, 90);
            CameraNode.Name = "Main Camera";
            MainCamera = new CameraComponent(CameraNode);
            CameraNode.Components.Add(MainCamera);


            //this.Renderer = new ManaDeferredRenderer(context);
            //this.Renderer = new ManaForwardRenderer(context);
            this.Renderer = new ManaIrradianceRenderer(context);
            this.Renderer.Initialize();

            DefaultScene = new ManaScene(context);
            DefaultScene.Name = "Default Scene";
            Scenes.Add(DefaultScene);
        }

        private void _Window_Resize(Silk.NET.Maths.Vector2D<int> obj)
        {
            Renderer.Resize(obj.X, obj.Y);
        }
       

        public unsafe ManaSceneNode LoadFromGltf(string sceneFile)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaGltfLoader loader = new ManaGltfLoader(Context);
            var rootSceneNode = loader.LoadFile(sceneFile);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog() { Name = $"Loading {sceneFile}", Duration = stopwatch.Elapsed });

            Scenes.AddRange(loader.Scenes);
            Meshes.AddRange(loader.Meshes);
            Materials.AddRange(loader.Materials);

            return rootSceneNode;
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
        }

        public void DrawImGui()
        {
            MainCamera.DrawImGui();
            Renderer.DrawImGui();
            RunFutures(ImGuiFutures, activeImGuiFutures);
            sceneOutlinerPanelView.Draw(this);

            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

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
