#if !MANA_BROWSER
#endif
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Renderers;
using Mana.Code.Shapes;
using Mana.Code.UI;
using Mana.Code.Shapes.Loaders;
using ManaCore.Graphics;
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

    public partial class ManaWorld : ManaObject
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

        /// <summary>Read-only view for a perf panel; ManaUI's World Perf panel polls this directly.</summary>
        public IReadOnlyDictionary<string, TimeSpan> LiveFrameStats => liveFrameStats;

        /// <summary>Read-only view of <see cref="RecordFrameStatText"/>'s targets, same reasoning as <see cref="LiveFrameStats"/>.</summary>
        public IReadOnlyDictionary<string, string> LiveFrameStatTexts => liveFrameStatTexts;

        /// <summary>The one-time load-time history, for the same panel's "Load times" section.</summary>
        public IReadOnlyList<ManaPerformanceLog> PerformanceLogs => manaPerformanceLogs;

        public int LastMeshCount { get; private set; }
        private readonly SceneSelectionState sceneSelectionState = new SceneSelectionState();
        public SceneSelectionState Selection => sceneSelectionState;
        private ManaIrradianceDebugScene? irradianceDebugScene;

        public readonly object SceneLock = new();

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

        public ManaScene DefaultScene { get; set; } = null!;
        public ManaSceneNode CameraNode = null!;
        public CameraComponent MainCamera = null!;

        /// <summary>True between <see cref="BeginPlay"/> and <see cref="EndPlay"/>.</summary>
        public bool IsPlaying { get; private set; }

        public ManaRenderer Renderer = null!;
        public ManaIrradianceSettings IrradianceSettings { get; } = new();
        public ManaEnvironmentReflectionSource EnvironmentReflectionSource { get; set; } = new();
        
        /// <summary>Score, health, checkpoint and clock for the current play session; reset by <see cref="BeginPlay"/>.</summary>
        public ManaGameState GameState { get; } = new();
        public Mana.Code.Audio.ManaAudioCueRouter AudioCues { get; } = new();

        public ManaWorld() : base(null!)
        {
            GameState.SoundRequested += AudioCues.Emit;
#if !MANA_BROWSER
#endif

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

        public void Initialize(ManaContext context, bool createRenderer = true)
        {
            Context = context;

            Context.Window.Resize += _Window_Resize;
            CameraNode = new ManaSceneNode(context, Matrix4x4.Identity);
            CameraNode.Position = new Vector3(6, 3, 10);
            CameraNode.Name = "Main Camera";
            MainCamera = new CameraComponent(CameraNode);
            CameraNode.Components.Add(MainCamera);


#if !MANA_BROWSER
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
#endif

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

        /// <summary>Starts every component currently in the scene graph.</summary>
        public void BeginPlay()
        {
            lock (SceneLock)
            {
                if (IsPlaying)
                {
                    return;
                }

                IsPlaying = true;

                // Before the components start, which is when collectibles and hazards register themselves.
                GameState.Reset();
                foreach (var component in CollectSceneComponentsNoLock())
                {
                    component.BeginPlay();
                }
            }
        }

        /// <summary>Stops components in reverse scene order so play-time state can be restored.</summary>
        public void EndPlay()
        {
            lock (SceneLock)
            {
                if (!IsPlaying)
                {
                    return;
                }

                var components = CollectSceneComponentsNoLock();
                for (var i = components.Count - 1; i >= 0; i--)
                {
                    components[i].EndPlay();
                }

                Context?.Audio.StopAll();
                IsPlaying = false;
            }
        }

        private List<ManaComponent> CollectSceneComponentsNoLock()
        {
            var components = new List<ManaComponent>();
            foreach (var scene in Scenes)
            {
                foreach (var node in scene.Children)
                {
                    CollectNodeComponents(node, components);
                }
            }

            return components;
        }

        private static void CollectNodeComponents(ManaSceneNode node, List<ManaComponent> output)
        {
            output.AddRange(node.Components);
            foreach (var child in node.Children)
            {
                CollectNodeComponents(child, output);
            }
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


        // A placed light of intensity 1 lights a table-lamp's worth of floor a metre or so below it.
        private const float LocalLightPowerScale = 8.0f;

        private void CollectLightsFromNode(
            ManaSceneNode node,
            List<(Vector3 Position, Vector3 Color)> output,
            List<(Vector4 Position, Vector4 Color)> local,
            ref (bool Found, Vector3 Direction, Vector4 Color, bool CastsShadows) sun)
        {
            if (node == null) return;
            foreach (var component in node.Components)
            {
                // A Glow's light is a small point light of its own colour, gathered with the lamps'.
                if (component is Mana.Code.Components.ManaGlowComponent { Enabled: true, LightRange: > 0.0f } glow)
                {
                    if (local.Count < ManaRenderScene.MaxLocalLights)
                    {
                        var at = Vector3.Transform(glow.LightOffset, node.WorldTransform);
                        var colour = new Vector3(glow.Color.X, glow.Color.Y, glow.Color.Z) * glow.CurrentStrength() * glow.LightIntensity * LocalLightPowerScale;
                        local.Add((new Vector4(at, glow.LightRange), new Vector4(colour, 1.0f)));
                    }

                    continue;
                }

                if (component is not ManaLightSourceComponent { Enabled: true } light)
                {
                    continue;
                }

                if (light.LightType == ManaCore.Graphics.ManaLightType.Directional)
                {
                    // Only the first enabled directional light found is honored -- see
                    // ManaRenderScene.HasSun.
                    if (!sun.Found)
                    {
                        var direction = light.Direction.LengthSquared() > 1e-6f
                            ? Vector3.Normalize(light.Direction)
                            : -Vector3.UnitY;
                        sun = (true, direction, light.Color, light.CastsShadows);
                    }

                    continue;
                }

                var lightColorVec3 = new Vector3(light.Color.X, light.Color.Y, light.Color.Z);
                if (light.IsGridLight)
                {
                    // Scale by grid light power scale: these sit 256 units up.
                    output.Add((node.WorldTransform.Translation, lightColorVec3 * light.Intensity * 256.0f * 256.0f));
                }
                else if (local.Count < ManaRenderScene.MaxLocalLights)
                {
                    local.Add((new Vector4(node.WorldTransform.Translation, Math.Max(light.Range, 0.01f)), new Vector4(lightColorVec3 * light.Intensity * LocalLightPowerScale, 1.0f)));
                }
            }

            foreach (var child in node.Children)
            {
                CollectLightsFromNode(child, output, local, ref sun);
            }
        }

        /// <summary>The placed point lights -- lamps, glows -- as a render scene gets them: xyz position, w range; colour.</summary>
        public List<(Vector4 Position, Vector4 Color)> CollectPlacedLights()
        {
            var grid = new List<(Vector3 Position, Vector3 Color)>();
            var local = new List<(Vector4 Position, Vector4 Color)>();
            (bool Found, Vector3 Direction, Vector4 Color, bool CastsShadows) sun = default;
            lock (SceneLock)
            {
                foreach (var scene in Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectLightsFromNode(child, grid, local, ref sun);
                    }
                }
            }
            return local;
        }

        public ManaRenderScene CreateRenderScene()
        {
            var meshes = new List<ManaRenderMeshInstance>();
            var collectedLights = new List<(Vector3 Position, Vector3 Color)>();
            var localLights = new List<(Vector4 Position, Vector4 Color)>();
            (bool Found, Vector3 Direction, Vector4 Color, bool CastsShadows) sun = default;

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
                            CollectLightsFromNode(child, collectedLights, localLights, ref sun);
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
                new Vector3(-debugGridSize * 0.5f, debugGridSize, -debugGridSize * 0.5f),
                new Vector3(debugGridSize * 0.5f, debugGridSize, -debugGridSize * 0.5f),
                new Vector3(-debugGridSize * 0.5f, debugGridSize, debugGridSize * 0.5f),
                new Vector3(debugGridSize * 0.5f, debugGridSize, debugGridSize * 0.5f)
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

            var sky = ManaSky.Tick(IrradianceSettings);
            for (var i = 0; i < lightColors.Length; i++)
            {
                var dimmed = new Vector3(lightColors[i].X, lightColors[i].Y, lightColors[i].Z) * sky.SunTint * sky.Daylight;
                lightColors[i] = new Vector4(dimmed, lightColors[i].W);
            }

            // A scene with no sun of its own is lit by the sky's -- sun by day, moon by night -- and casts
            // shadows from it. A level's own directional light still wins.
            if (!sun.Found && ManaSky.Light(sky) is { } skyLight)
            {
                sun = (true, skyLight.Direction, skyLight.Colour, true);
            }

            return new ManaRenderScene
            {
                Sky = sky,
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
                LightColors = lightColors,
                LocalLightPositions = PadLocal(localLights.Select(l => l.Position)),
                LocalLightColors = PadLocal(localLights.Select(l => l.Color)),
                HasSun = sun.Found,
                SunDirection = sun.Found ? sun.Direction : -Vector3.UnitY,
                SunColor = sun.Found ? sun.Color : Vector4.One,
                SunCastsShadows = sun.Found && sun.CastsShadows
            };
        }

        private static Vector4[] PadLocal(IEnumerable<Vector4> values)
        {
            var slots = new Vector4[ManaRenderScene.MaxLocalLights];
            var i = 0;
            foreach (var value in values)
            {
                slots[i++] = value;
            }

            return slots;
        }

    }
}
