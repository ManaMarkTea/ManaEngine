using ImGuiNET;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.UI;
using Mana.Code.Shapes.Loaders;
using ManaCore.Graphics;
using Silk.NET.Assimp;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Numerics;

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

        // Live, per-frame timings (overwritten every frame, unlike manaPerformanceLogs above which is
        // a one-time load history) — keyed so repeated calls update in place instead of growing forever.
        private readonly Dictionary<string, TimeSpan> liveFrameStats = new();

        public void RecordFrameStat(string name, TimeSpan duration)
        {
            liveFrameStats[name] = duration;
        }

        public int LastMeshCount { get; private set; }
        private readonly SceneSelectionState sceneSelectionState = new SceneSelectionState();
        public SceneSelectionState Selection => sceneSelectionState;
        private readonly SceneOutlinerPanelView sceneOutlinerPanelView;
        private readonly SceneInspectorPanelView sceneInspectorPanelView;
        private readonly CameraComponentImGuiView cameraComponentImGuiView = new CameraComponentImGuiView();
        private readonly IrradianceSettingsPanelView irradianceSettingsPanelView = new IrradianceSettingsPanelView();
        private ManaIrradianceDebugScene? irradianceDebugScene;

        public readonly object SceneLock = new();

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
        public ManaIrradianceSettings IrradianceSettings { get; } = new();
        public ManaEnvironmentReflectionSource EnvironmentReflectionSource { get; set; } = new();
        
        public ManaWorld() : base(null!)
        {
            sceneOutlinerPanelView = new SceneOutlinerPanelView(sceneSelectionState);
            sceneInspectorPanelView = new SceneInspectorPanelView(sceneSelectionState);

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

        public void Initialize(ManaContext context, bool createRenderer = true)
        {
            Context = context;

            Context.Window.Resize += _Window_Resize;
            CameraNode = new ManaSceneNode(context, Matrix4x4.Identity);
            CameraNode.Position = new Vector3(20, 32, 90);
            CameraNode.Name = "Main Camera";
            MainCamera = new CameraComponent(CameraNode);
            CameraNode.Components.Add(MainCamera);


            if (createRenderer)
            {
                //this.Renderer = new ManaDeferredRenderer(context);
                //this.Renderer = new ManaForwardRenderer(context);
                this.Renderer = new ManaIrradianceRenderer(context);
                if (!context.DeferGpuUploads)
                {
                    this.Renderer.Initialize();
                }
            }

            DefaultScene = new ManaScene(context);
            DefaultScene.Name = "Default Scene";
            Scenes.Add(DefaultScene);
            irradianceDebugScene = new ManaIrradianceDebugScene(context, IrradianceSettings);
            irradianceDebugScene.SyncMembership();
        }

        private void _Window_Resize(Vector2D<int> obj)
        {
            Renderer?.Resize(obj.X, obj.Y);
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

            lock (SceneLock)
            {
                foreach (var scene in Scenes)
                {
                    scene.Update();
                }
            }

            RunFutures(PostUpdateFutures, activePostUpdateFutures);
        }

        public void Draw()
        {
            if (Renderer == null)
            {
                return;
            }

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
            // Tab order within a dock node follows Begin() submission order, so Inspector
            // is drawn first here to keep it as the leftmost/default-selected utility tab.
            sceneInspectorPanelView.Draw(Context.GameMode.BarlowCondensedImGuiFont);
            cameraComponentImGuiView.Draw(MainCamera);
            Renderer?.DrawImGui();
            var irradiancePanelResult = irradianceSettingsPanelView.Draw(IrradianceSettings);
            if (irradiancePanelResult.DebugWorldChanged)
            {
                irradianceDebugScene?.SyncMembership();
            }

            if (IrradianceSettings.DrawDebugWorld && irradiancePanelResult.DebugMaterialChanged)
            {
                irradianceDebugScene?.SyncMaterials();
            }

            if (IrradianceSettings.DrawDebugWorld && irradiancePanelResult.DebugSceneScaleChanged)
            {
                irradianceDebugScene?.SyncTransform();
            }

            RunFutures(ImGuiFutures, activeImGuiFutures);
            sceneOutlinerPanelView.Draw(this);

            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("World Perf");

            ImGui.SeparatorText("Live (this frame)");
            ImGui.Text($"Meshes rendered: {LastMeshCount}");
            foreach (var (name, duration) in liveFrameStats)
            {
                ImGui.Text($"{duration.TotalMilliseconds,7:F2}ms - {name}");
            }

            ImGui.SeparatorText("Load times");
            foreach (var perf in manaPerformanceLogs)
            {
                ImGui.Text($"{ perf.Duration.Milliseconds}ms - {perf.Name}");
            }

            ImGui.End();
        }

        private void CollectLightsFromNode(ManaSceneNode node, List<(Vector3 Position, Vector3 Color)> output)
        {
            if (node == null) return;
            foreach (var component in node.Components)
            {
                if (component is ManaLightSourceComponent light && light.Enabled)
                {
                    var lightColorVec3 = new Vector3(light.Color.X, light.Color.Y, light.Color.Z);
                    var lightColor = lightColorVec3 * light.Intensity * 256.0f * 256.0f; // Scale by grid light power scale
                    output.Add((node.WorldTransform.Translation, lightColor));
                }
            }

            foreach (var child in node.Children)
            {
                CollectLightsFromNode(child, output);
            }
        }

        public ManaRenderScene CreateRenderScene()
        {
            var meshes = new List<ManaRenderMeshInstance>();
            var collectedLights = new List<(Vector3 Position, Vector3 Color)>();

            var collectStopwatch = Stopwatch.StartNew();
            lock (SceneLock)
            {
                foreach (var scene in Scenes)
                {
                    scene.CollectRenderMeshInstances(meshes);
                }

                foreach (var scene in Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectLightsFromNode(child, collectedLights);
                    }
                }
            }
            RecordFrameStat("Scene Graph Collect", collectStopwatch.Elapsed);
            LastMeshCount = meshes.Count;

            var lightPositions = new Vector4[4];
            var lightColors = new Vector4[4];

            // Default grid light definitions (matching ManaIrradianceRenderer defaults)
            const float debugGridSize = 256.0f;
            var defaultPositions = new Vector3[]
            {
                new Vector3(0.0f, debugGridSize, 0.0f),
                new Vector3(debugGridSize, debugGridSize, 0.0f),
                new Vector3(0.0f, debugGridSize, debugGridSize),
                new Vector3(debugGridSize, debugGridSize, debugGridSize)
            };
            var defaultWeights = new float[] { 1.0f, 0.8f, 0.5f, 0.3f };
            const float gridLightPowerScale = debugGridSize * debugGridSize;

            for (int i = 0; i < 4; i++)
            {
                if (i < collectedLights.Count)
                {
                    lightPositions[i] = new Vector4(collectedLights[i].Position, 1.0f);
                    lightColors[i] = new Vector4(collectedLights[i].Color, 1.0f);
                }
                else
                {
                    // Fall back to default grid lights if none are instantiated in the scene
                    var lightPosition = defaultPositions[i];
                    var lightColor = IrradianceSettings.LightColor.ToVector3()
                        * (IrradianceSettings.LightIntensity * defaultWeights[i])
                        * gridLightPowerScale;

                    lightPositions[i] = new Vector4(lightPosition, 1.0f);
                    lightColors[i] = new Vector4(lightColor, 1.0f);
                }
            }

            return new ManaRenderScene
            {
                Camera = new ManaCameraData(
                    MainCamera._ViewMatrix,
                    MainCamera._ProjectionMatrix,
                    MainCamera.Owner.Position,
                    MainCamera.NearClipPlane,
                    MainCamera.FarClipPlane),
                Meshes = meshes,
                IrradianceSettings = IrradianceSettings.Clone(),
                EnvironmentReflectionSource = EnvironmentReflectionSource.Clone(),
                LightPositions = lightPositions,
                LightColors = lightColors
            };
        }

    }
}



/*
 * 
 * 
 
if (UseRayTracing) { RenderTarget.Bind(); }

            var gl = Context.RequireOpenGl();


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
