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

        // Same "overwritten every frame" convention as liveFrameStats, for live status text that isn't a
        // duration (e.g. PlanetNode's streaming counts) -- avoids World Perf needing to know about any
        // particular subsystem; callers just push a name/value pair each frame.
        private readonly Dictionary<string, string> liveFrameStatTexts = new();

        public void RecordFrameStat(string name, TimeSpan duration)
        {
            liveFrameStats[name] = duration;
        }

        public void RecordFrameStatText(string name, string value)
        {
            liveFrameStatTexts[name] = value;
        }

        /// <summary>Read-only view for a perf panel; ManaUI's World Perf equivalent polls this directly rather than through ImGui.</summary>
        public IReadOnlyDictionary<string, TimeSpan> LiveFrameStats => liveFrameStats;

        /// <summary>Read-only view of <see cref="RecordFrameStatText"/>'s targets, same reasoning as <see cref="LiveFrameStats"/>.</summary>
        public IReadOnlyDictionary<string, string> LiveFrameStatTexts => liveFrameStatTexts;

        /// <summary>The one-time load-time history, for the same panel's "Load times" section.</summary>
        public IReadOnlyList<ManaPerformanceLog> PerformanceLogs => manaPerformanceLogs;

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
       

        public unsafe ManaSceneNode LoadFromGltf(
            string sceneFile,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaGltfLoader loader = new ManaGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFile(sceneFile);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog() { Name = $"Loading {sceneFile}", Duration = stopwatch.Elapsed });

            Scenes.AddRange(loader.Scenes);
            Meshes.AddRange(loader.Meshes);
            Materials.AddRange(loader.Materials);

            return rootSceneNode;
        }

        public unsafe ManaSceneNode LoadFromGltfMemory(
            ReadOnlyMemory<byte> sceneBytes,
            string virtualFileName,
            Func<string, byte[]?>? externalAssetResolver = null,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaGltfLoader loader = new ManaGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFileFromMemory(sceneBytes.Span, virtualFileName, externalAssetResolver);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog()
            {
                Name = $"Loading {virtualFileName} (memory)",
                Duration = stopwatch.Elapsed
            });

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

            // Futures are queued work that ran here for want of anywhere better, so they are the first thing
            // to suspect when this method spikes and the scene walk does not account for it.
            using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("World: Pre-Update Futures"))
            {
                RunFutures(PreUpdateFutures, activePreUpdateFutures);
            }

            using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("World: Await Lock"))
            {
                Monitor.Enter(SceneLock);
            }

            try
            {
                using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("World: Scene Walk"))
                {
                    foreach (var scene in Scenes)
                    {
                        scene.Update();
                    }
                }
            }
            finally
            {
                Monitor.Exit(SceneLock);
            }

            using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("World: Post-Update Futures"))
            {
                RunFutures(PostUpdateFutures, activePostUpdateFutures);
            }

            // A camera riding a spring arm is posed here rather than in CameraNode.UpdateTransform
            // above, because the thing it follows only reaches its final position during the scene
            // update. Doing it first would leave the camera a frame behind its target, which reads as
            // the character sliding around inside the view whenever it moves quickly.
            if (MainCamera.IsAttached)
            {
                MainCamera.ApplyAttachedPose((float)ManaTime.GlobalTime.DeltaTime.TotalSeconds);
                ViewMatrix = MainCamera._ViewMatrix;
            }
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

            // Peak first, same reasoning as the ManaUI panel: a this-frame column cannot show a hitch,
            // because the frame you are reading is by definition not the one that stuttered.
            ImGui.SeparatorText("Frame timings (peak over last 2s)");
            ImGui.Text(
                $"{ManaCore.Diagnostics.ManaFrameProfiler.HitchCount} hitches over "
                + $"{ManaCore.Diagnostics.ManaFrameProfiler.HitchThresholdMs:0}ms in "
                + $"{ManaCore.Diagnostics.ManaFrameProfiler.FrameCount} frames, worst "
                + $"{ManaCore.Diagnostics.ManaFrameProfiler.WorstFrameMs:0.0}ms");
            foreach (var (name, sample) in ManaCore.Diagnostics.ManaFrameProfiler.TopByPeak(24))
            {
                var calls = sample.LastCalls > 1 ? $" x{sample.LastCalls}" : string.Empty;
                ImGui.Text(
                    $"{sample.PeakMs,7:F2} peak {sample.LastMs,7:F2} now {sample.AverageMs,7:F2} avg "
                    + $"{sample.PeakKb,8:F0}KB  {name}{calls}");
            }

            ImGui.SeparatorText("Live (this frame)");
            ImGui.Text($"Meshes rendered: {LastMeshCount}");
            foreach (var (name, duration) in liveFrameStats)
            {
                ImGui.Text($"{duration.TotalMilliseconds,7:F2}ms - {name}");
            }

            if (liveFrameStatTexts.Count > 0)
            {
                ImGui.SeparatorText("Streaming");
                foreach (var (name, value) in liveFrameStatTexts)
                {
                    ImGui.Text($"{name}: {value}");
                }
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
            // Separately scoped from the walk below because they fail differently: waiting on SceneLock means
            // a background thread is holding the scene, whereas a slow walk means the scene itself is big.
            using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Scene: Await Lock"))
            {
                Monitor.Enter(SceneLock);
            }

            try
            {
                using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Scene: Collect Meshes"))
                {
                    foreach (var scene in Scenes)
                    {
                        scene.CollectRenderMeshInstances(meshes);
                    }
                }

                using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Scene: Collect Lights"))
                {
                    foreach (var scene in Scenes)
                    {
                        foreach (var child in scene.Children)
                        {
                            CollectLightsFromNode(child, collectedLights);
                        }
                    }
                }
            }
            finally
            {
                Monitor.Exit(SceneLock);
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
