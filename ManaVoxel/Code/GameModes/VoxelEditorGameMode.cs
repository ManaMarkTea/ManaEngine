using BepuPhysics.Collidables;
using ManaUI;
using System.Linq;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Search;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    public partial class VoxelEditorGameMode : BaseGameMode
    {
        private const string ManaWindowTitle = "Mana";
        // The demo grid room's full width/depth -- see the matching comment where the room is built for
        // why this can't just change there alone.
        public const float GridSize = 256.0f;

        private readonly VoxelEditorViewModel _viewModel = new();
        private readonly PlaceSearchNavigator _navigator = new();
        private readonly VoxelToolController _toolController = new();
        private readonly VoxelFileImportService _fileImportService = new();
        private ManaPawnController? _pawnController;
        private PlayModeController? _playMode;

        // Owned here rather than by the play controller: the HUD that shows its modes is built before Load makes that.
        private readonly ManaGameModeRunner _gameModes = new();
        private TrackDebugVisualization? _trackDebugVisualization;
        private SceneTransformGizmo? _transformGizmo;

        private bool _directEditPointerWasDown;
        private Vector3? _lastDirectEditPosition;
        private HashSet<MeshRendererComponent> _lastOutlinedSelectionComponents = new();
        private HashSet<MeshRendererComponent> _lastStreamingHighlightedComponents = new();
        private readonly HashSet<ManaSceneNode> _pickingExclusions = new();
        private static readonly Vector4 SelectionOutlineColor = new(0.30f, 0.72f, 1.0f, 1.0f);
        private static readonly Vector4 PatchUpresInProgressOutlineColor = new(1.0f, 0.55f, 0.0f, 1.0f);
        private static readonly Vector4 PatchUpresDoneOutlineColor = new(0.25f, 0.85f, 0.25f, 1.0f);
        private static readonly Vector4 PatchUpresFailedOutlineColor = new(0.9f, 0.2f, 0.2f, 1.0f);

        private ManaSceneNode? _lightningBall = null;
        private ManaSceneNode? _liquidBall = null;

        // The planet, true-scale regions and the KML airspace overlay live in EditorPlanetScenery; the game mode
        // only reads the planet, to pick and up-res its patches.
        private EditorPlanetScenery _planetScenery = null!;
        private PlanetNode? _planet => _planetScenery?.Planet;
        private ManaWaterNode? _water;
        private bool _waterRipplePointerWasDown;
        private ManaWebGpuContext? _webGpuContext;

        public override bool DeferGpuUploadsOnStartup => true;

        protected override string? WindowIconPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "AppIcon.ico");

        public VoxelEditorGameMode(ManaGraphicsBackendKind graphicsBackend = ManaGraphicsBackendKind.OpenGL) : base(graphicsBackend)
        {
            // Before any UI is built: the preferences panel and the placement panel both read it as they construct.
            _viewModel.Snap.Restore();
        }

        public void RunWebGpu(string? requestedBackend)
        {
            // Kept so Play In New Window launches on the backend this window is running, rather than
            // whatever the second process would have picked on its own.
            _requestedWebGpuBackend = requestedBackend;

            ManaConsole.AttachForDiagnostics("ManaVoxel WebGPU Diagnostics");
            ManaConsole.WriteDiagnostic("Starting ManaVoxel game mode with WebGPU graphics backend.");
            ManaConsole.WriteDiagnostic($"Requested WGPU backend: {requestedBackend ?? "auto"}");

            new GameSilkWebGpu(
                "ManaVoxel - WebGPU",
                requestedBackend,
                () => Context.World.CreateRenderScene(),
                (window, input) =>
                {
                    LoadForGraphicsBackend(window, input, new ManaWebGpuGraphicsDevice(requestedBackend));
                    LogWebGpuRenderSceneSummary();
                },
                deltaTime => update(deltaTime),
                deltaTime => Render(deltaTime),
                (webGpuContext, input) =>
                {
                    _webGpuContext = webGpuContext;
                    if (Context.GraphicsDevice is ManaWebGpuGraphicsDevice webGpuGraphicsDevice)
                    {
                        webGpuGraphicsDevice.Context = webGpuContext;
                    }

                    // Hands the backend the tree to draw. The engine layer still owns layout and recording;
                    // the context only submits the draw list this produced.
                    webGpuContext.UiRoot = ManaUi?.Root;

                    // Handed to the About panel here rather than fetched by it: the snapshot belongs to
                    // whichever backend is running, and a panel should not have to know which that was.
                    ManaUiDiagnostics.BackendName = Context.GraphicsBackend.ToString();
                    ManaUiDiagnostics.Snapshot =
                        ManaGraphicsDiagnosticsProviderFactory.Create(Context, webGpuContext)?.DiagnosticsSnapshot;
                    webGpuContext.EnvironmentLoading.PropertyChanged += (_, _) => UpdateWebGpuLoadingProgress();
                },
                iconPath: WindowIconPath
            ).Run();
        }

        private void LogWebGpuRenderSceneSummary()
        {
            var renderScene = Context.World.CreateRenderScene();
            ManaConsole.WriteDiagnostic($"VoxelEditor WebGPU render scene: meshes={renderScene.Meshes.Count}.");
            foreach (var mesh in renderScene.Meshes)
            {
                ManaConsole.WriteDiagnostic(
                    $"VoxelEditor WebGPU mesh packet: name='{mesh.Name}', vertices={mesh.MeshData.Positions.Length}, indexes={mesh.MeshData.Indexes.Length}.");
            }
        }

        // The showcase panel is the standing visual regression check for ManaUI's layout, text and SDF
        // primitive.
        protected override bool EnableManaUi => true;

        private readonly ManaUiShowcaseViewModel _manaUiShowcase = new();

        protected override void BuildManaUi(global::Mana.Code.UI.ManaUi.ManaUiHost host)
        {
#if !MANA_BROWSER
            if (Context.GraphicsDevice is ManaWebGpuGraphicsDevice device)
                host.EnableDesktopWindows(window => device.Context?.CreateUiSurface(window), WindowIconPath);
#endif
            // RenderFrame is plain engine state with no change notification, which is exactly the case the
            // polled binding exists for.
            // The editor's one view model -- the toolbar, the menu and the panels all drive it.
            var workspace = ManaUiDockDemo.Attach(
                host.Root,
                _manaUiShowcase,
                () => $"render frame {RenderFrame:N0}",
                editor: _viewModel,
                world: Context.World);

            // Play collapses the whole workspace away and leaves the controls hint. Polled rather than
            // rebuilt on the switch, because _playMode does not exist yet -- this runs before Load.
            // The play HUD for a character game, with the level's game modes' own HUDs over it.
            _gameModes.UseTheme(host.Root.Theme);
            var playHud = ManaUiPlayHud.Build(
                    host.Root.Theme,
                    GetPlayHudText,
                    () => _playMode?.Player != null ? Context.World.GameState.FormatHud() : "",
                    ManaUiPlayResults.Build(
                        host.Root.Theme,
                        Context.World.GameState,
                        () => _playMode?.Restart(),
                        () => _playMode?.Stop(),
                        "Back to Editor"),
                    ManaUiDeathScreen.Build(host.Root.Theme, Context.World.GameState));
            playHud.BindPolled(el => el.Visibility, () => _gameModes.ShowsPlayHud ? Visibility.Visible : Visibility.Collapsed);
            var playLayer = new Grid();
            playLayer.Children.Add(playHud);
            playLayer.Children.Add(_gameModes.HudLayer);
            // Hidden in the top-right corner until the pointer comes near: step out of the match to inspect it.
            playLayer.Children.Add(ManaUiPlayOverlay.EjectButton(host.Root.Theme, ToggleEject));
            host.Root.Content = new ManaUiPlayOverlay(workspace, playLayer, () => IsPlaying,
                ManaUiPlayOverlay.EjectedBanner(host.Root.Theme, () => IsEjected, ToggleEject, () => _playMode?.Stop()));
        }

        public override void Load()
        {
            LoadingCoordinator.Begin();
            var world = Context.World;
            _planetScenery = new EditorPlanetScenery(Context, _viewModel);
            _transformGizmo = new SceneTransformGizmo(world, _viewModel.History, _viewModel.Snap);
            _trackDebugVisualization = new TrackDebugVisualization(world);

            // Shares the editor's picking exclusions so a click-to-move order falls through the grid
            // planes onto the ground plane, exactly like the voxel placement raycast does.
            _pawnController = new ManaPawnController(Context)
            {
                PickingExclusions = _pickingExclusions,
                GroundPlaneHeight = 0.0f
            };
            _viewModel.AttachPawnController(_pawnController);

            InitializePlayMode();

            // Hands WASD to the possessed character. Set here rather than alongside
            // ShouldCaptureMouseInput below because that assignment runs on the background load task,
            // and this one has to be live from the first frame.
            Context.World.MainCamera.ShouldCaptureKeyboardInput = () => _pawnController?.IsCapturingMovementKeys == true;

            Task.Run(() =>
            {
                if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
                {
                    System.Threading.Thread.CurrentThread.Name = "Voxel editor load";
                }

                try
                {
                    Context.DeferGpuUploads = true;

                    // 0. Pre-decode environment reflection map CPU data
                    Loading.Report(new ManaLoadingProgress("Loading environment reflection map", 0.05f));
                    // Only OpenGL reads the photo through the texture cache; WebGPU draws the sky into its reflections and
                    // decodes the photo itself, only for the Photo sky.
                    if (GraphicsBackend == ManaGraphicsBackendKind.OpenGL)
                    {
                        var envPath = world.EnvironmentReflectionSource.SourcePath;
                        var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                        envTexture.OriginalFilename = envPath;
                        envTexture.Load();
                    }

                    // 1. Generate shapes CPU data
                    Loading.Report(new ManaLoadingProgress("Generating procedural shapes", 0.2f));

                    Context.Window.FileDrop += Window_FileDrop;
                    _viewModel.AttachScene(Context, Context.World.DefaultScene);
                    Context.World.MainCamera.ShouldCaptureMouseInput = ShouldCaptureEditorMouseInput;

                    // Same reason the camera above stops zooming: a live placement preview wants the
                    // scroll wheel for its own rotation, not whatever panel happened to keep pointer
                    // capture from the drag that started it. See ManaUiInputBridge.SuppressScroll.
                    if (ManaUi?.InputBridge != null)
                    {
                        ManaUi.InputBridge.SuppressScroll = () => _meshPickerPreviewNode != null;
                    }

                    bool rounded = ManaUi?.Root?.Theme?.Kind == UiThemeKind.Lcars;
                    BuildGridRoom(rounded);

                    // Probe for optional KML placemarks. Deliberately only
                    // a file-existence check -- the actual parse/mesh build happens lazily the first time
                    // View > Show Airspace (KML) is switched on, so startup never pays for an overlay
                    // that's off by default (see SyncKmlAirspaceVisibility).
                    Loading.Report(new ManaLoadingProgress("Checking for KML placemarks", 0.9f));
                    _planetScenery.ProbeKmlAirspace();

                    Loading.Report(new ManaLoadingProgress("Starting graphics renderer", 0.9f));
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
            if (_lightningBall?.Components[0] is MeshRendererComponent lightningRenderer)
            {
                var lightningMaterial = lightningRenderer.DynamicMaterial;
                lightningMaterial.Name = "Lightning Ball Material";
                lightningMaterial.Shader = new ManaLightningBallShader(Context);
                lightningRenderer.DrawAfterScene = true;
            }

            if (_liquidBall?.Components[0] is MeshRendererComponent liquidRenderer)
            {
                var liquidMaterial = liquidRenderer.DynamicMaterial;
                liquidMaterial.Name = "Liquid Ball Material";
                liquidMaterial.Shader = new ManaLiquidBallShader(Context);
                liquidRenderer.DrawAfterScene = true;
            }

        }

        protected override bool CanCompleteLoading()
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return true;
            }

            if (_webGpuContext == null || _webGpuContext.IsEnvironmentInitializationPending)
            {
                UpdateWebGpuLoadingProgress();
                return false;
            }

            return true;
        }

        private void UpdateWebGpuLoadingProgress()
        {
            if (!IsLoading ||
                LoadingCoordinator.Stage != ManaLoadingStage.WaitingForDependencies ||
                _webGpuContext == null)
            {
                return;
            }

            var environmentLoading = _webGpuContext.EnvironmentLoading.Snapshot;
            Loading.Report(new ManaLoadingProgress(
                environmentLoading.Status,
                0.95f + environmentLoading.Fraction * 0.05f));
        }

        // A planet/region inspector hook and a background terrain-cache-size reader used to sit here,
        // both feeding the ImGui SceneInspectorPanelView. ManaUI's TerrainCacheWidget and PlanetWidget are
        // independent replacements that read the same nodes directly, so all of it went with ImGui.

        private static string FormatMetersPerPixel(double metersPerPixel)
        {
            if (metersPerPixel >= 1000.0)
            {
                return $"{metersPerPixel / 1000.0:0.##} km/px";
            }

            if (metersPerPixel >= 100.0)
            {
                return $"{metersPerPixel:0} m/px";
            }

            if (metersPerPixel >= 10.0)
            {
                return $"{metersPerPixel:0.#} m/px";
            }

            return metersPerPixel >= 1.0
                ? $"{metersPerPixel:0.##} m/px"
                : $"{metersPerPixel * 100.0:0.#} cm/px";
        }

        private static double EstimateMeshSpacingMeters(int meshResolution, int sourceResolution, double sourceMetersPerPixel)
        {
            if (meshResolution <= 1 || sourceResolution <= 1)
            {
                return sourceMetersPerPixel;
            }

            return sourceMetersPerPixel * (sourceResolution - 1) / (meshResolution - 1);
        }


        // ManaWebGpuContext (ManaGraphics) can't push into ManaWorld (Mana) directly — same
        // wrong-dependency-direction reason as above — so it just exposes last-frame timings as
        // properties and ManaVoxel (which references both) copies them into the World Perf panel's
        // live stats each frame. One frame of latency (this frame's ClearFrame hasn't run yet when
        // DrawImGui executes) is expected and fine for a live HUD.
        private void PushWebGpuFrameStats()
        {
            if (_webGpuContext == null)
            {
                return;
            }

            var world = Context.World;
            world.RecordFrameStat("GPU: Scene Collect (RefreshRenderScene)", TimeSpan.FromMilliseconds(_webGpuContext.LastCollectMs));
            world.RecordFrameStat("GPU: Draw Meshes", TimeSpan.FromMilliseconds(_webGpuContext.LastDrawMs));
            world.RecordFrameStat("GPU: Submit + Present", TimeSpan.FromMilliseconds(_webGpuContext.LastSubmitMs));
            world.RecordFrameStat("GPU: ClearFrame Total", TimeSpan.FromMilliseconds(_webGpuContext.LastFrameTotalMs));
        }

        /// <summary>
        /// Moves finished mesh previews into the UI atlas. Main thread, a few per frame -- see
        /// MeshThumbnailImages.Pump for why the upload is throttled rather than done as results arrive.
        /// <para>
        /// The cache is created on first use rather than at load: it needs a context, and a session that never
        /// opens the Import panel should not have spun up a worker or touched the cache directory.
        /// </para>
        /// </summary>
        private void PumpMeshThumbnails()
        {
            var cache = ManaUiDockDemo.MeshThumbnails;
            if (cache == null)
            {
                cache = new MeshThumbnailCache(Context, _webGpuContext);
                ManaUiDockDemo.MeshThumbnails = cache;
            }

            if (ManaUi?.Root is { } root)
            {
                using (ManaFrameProfiler.Measure("Thumbnails: Atlas Upload"))
                {
                    MeshThumbnailImages.Pump(root, cache);
                }
            }
        }

        private async void Window_FileDrop(string[] files)
        {
            var mouseDropWorld = GetProjectedPlacementHit();
            var dropPosition = mouseDropWorld.Hit ? mouseDropWorld.Position : (Vector3?)null;
            try
            {
                if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog)
                    foreach (var file in files) await catalog.EnsureAsync(file);
                _fileImportService.ImportDroppedFiles(files, dropPosition, Context.World, _viewModel);
            }
            catch (Exception ex) { _viewModel.Levels.SetStatus($"Import failed: {ex.Message}"); }
        }

        private RayHit GetProjectedPlacementHit()
        {
            var hit = GetProjectedMouseHit();
            if (hit.Hit)
            {
                return hit;
            }

            var mouseState = Context.Input.Mice.FirstOrDefault();
            if (mouseState == null)
            {
                return hit;
            }

            var mouseStart = new Vector3(mouseState.Position, 0.0f);
            var mouseEnd = new Vector3(mouseState.Position, 1.0f);
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var pick = ManaScenePicker.PickWithHit(Context.World.Scenes, ray.Origin, ray.Direction, _pickingExclusions);
            if (!pick.HasValue)
            {
                return hit;
            }

            return new RayHit
            {
                Hit = true,
                Position = pick.Value.Position,
                Normal = pick.Value.Normal,
                Distance = pick.Value.Distance,
            };
        }

        private bool IsPlacementPreview(ManaSceneNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, _meshPickerPreviewNode)) { return true; }
            }
            return false;
        }

        RayHit GetProjectedMouseHit()
        {
            var MouseState = Context.Input.Mice.FirstOrDefault();
            if (MouseState == null)
            {
                return new RayHit();
            }

            var mouseStart = new Vector3(MouseState.Position, 0.0f);
            var mouseEnd = new Vector3(MouseState.Position, 1.0f);

            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);

            var hitHandler = new SingleHitHandler
            {
                Filter = collidable => collidable.Mobility == BepuPhysics.Collidables.CollidableMobility.Static
                    || !Context.Physics.PhysicsMap.TryGetValue(collidable.BodyHandle, out var collision)
                    || !IsPlacementPreview(collision.Owner)
            };
            Context.Physics.Simulation.RayCast(ray.Origin, ray.Direction, 10000.0f, Context.Physics.BufferPool, ref hitHandler, 0);

            RayHit hit = new RayHit();
            if (hitHandler.ClosestHit.HasValue)
            {
                hit = hitHandler.ClosestHit.Value;
            }

            return hit;
        }

        // The construction-plane counterpart to GetProjectedMouseHit: intersects the mouse ray with a
        // fixed plane instead of raycasting against physics colliders, so a work plane lets every tool draw
        // in open air rather than only against whatever geometry the cursor happens to be over.
        private RayHit GetWorkPlaneHit(VoxelWorkPlane plane, float offset)
        {
            var mouseState = Context.Input.Mice.FirstOrDefault();
            if (mouseState == null)
            {
                return new RayHit();
            }

            var mouseStart = new Vector3(mouseState.Position, 0.0f);
            var mouseEnd = new Vector3(mouseState.Position, 1.0f);
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);

            if (!VoxelRasterizer.TryIntersectWorkPlane(ray.Origin, ray.Direction, plane, offset, out var position, out var normal))
            {
                return new RayHit();
            }

            return new RayHit
            {
                Hit = true,
                Position = position,
                Normal = normal,
                Distance = Vector3.Distance(ray.Origin, position),
            };
        }

        public const float TimestepDuration = 1 / 60f;

        public override void Update(double deltaTime)
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return;
            }

            // Hold Alt to see every constellation's lines at once, not just those the view is turned toward.
            var altKeyboard = Context.Input.Keyboards.FirstOrDefault();
            Context.World.IrradianceSettings.ShowAllConstellations = altKeyboard != null
                && (altKeyboard.IsKeyPressed(Key.AltLeft) || altKeyboard.IsKeyPressed(Key.AltRight));

            if (IsPlaying)
            {
                Context.World.GameState.Tick((float)deltaTime);
                _playMode?.Tick((float)deltaTime);
            }

            SyncGameModePreviews();
            PumpMeshThumbnails();
            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();
            SyncWaterVisibility();
            SyncWireframeViewOption();
            SyncNormalsViewOption();
            _planetScenery.SyncKmlAirspaceVisibility();
            SyncGridRoomShape();
            SyncGridRoomWalls();
            SyncVisualBoundsColliders();
            _planetScenery.ProcessPendingRegionRequest();

            _planetScenery.PumpStreaming(UpdateFrame);
            PushPlanetStreamingStatusToWorldPerf();
            UpdateWaterRippleInteraction();
        }

        // Surfaces PlanetNode's current up-res/split/merge/fetch activity in the World Perf panel's
        // "Streaming" section (Mana.Code.Scenes.ManaWorld.DrawImGui) -- added after auto-LOD made it easy
        // to have many patches streaming in at once with no visibility into how much work was in flight.
        private void PushPlanetStreamingStatusToWorldPerf()
        {
            var world = Context.World;

            // Frame-level health, pushed every frame regardless of whether a planet exists: hitch count and
            // worst frame are cumulative, so they answer "did it stutter while I was flying" after the fact.
            world.RecordFrameStatText(
                "Frames",
                $"{ManaFrameProfiler.FrameCount} measured, {ManaFrameProfiler.HitchCount} over "
                + $"{ManaFrameProfiler.HitchThresholdMs:0}ms, worst {ManaFrameProfiler.WorstFrameMs:0.0}ms");

            var gc = ManaFrameProfiler.LastGcDelta;
            world.RecordFrameStatText(
                "GC",
                $"alloc {ManaFrameProfiler.LastAllocatedMb:0.00}MB/frame (peak {ManaFrameProfiler.PeakAllocatedMb:0.0}MB), "
                + $"collections this frame {gc.Gen0}/{gc.Gen1}/{gc.Gen2}");

            // Worst pause rather than the last one: a pause is the thing that stutters, and it is over by the
            // time anyone reads the panel.
            world.RecordFrameStatText(
                "GC pauses",
                $"worst {ManaFrameProfiler.WorstGcPauseMs:0.0}ms, {ManaFrameProfiler.GcPauseTimePercentage:0.0}% of wall clock, "
                + $"heap {ManaFrameProfiler.HeapMb:0}MB, {(ManaFrameProfiler.IsServerGc ? "server" : "workstation")} GC");

            _planetScenery.PushStreamingStatus();
        }

        // TEMP diagnostic instrumentation -- remove once the "50GB memory growth" report is root-caused.
        // Set MANA_LOG_LEVEL=Verbose to see these (see ManaLog.WriteVerbose's own doc comment).
        // 0, not long.MinValue -- Environment.TickCount64 is always non-negative, so "now - lastLogMs"
        // against a MinValue sentinel overflows unchecked long arithmetic and wraps to a huge negative
        // number, permanently satisfying "< interval" and silently blocking every future log forever.
        private long _lastMemoryStatsLogMs;
        private const long MemoryStatsLogIntervalMs = 5000;

        private void LogMemoryStatsPeriodically()
        {
            var now = Environment.TickCount64;
            if (now - _lastMemoryStatsLogMs < MemoryStatsLogIntervalMs)
            {
                return;
            }
            _lastMemoryStatsLogMs = now;

            // Deliberately defensive/self-logging (unlike ManaLog.WriteVerbose's own silent catch) --
            // this is throwaway diagnostic code investigating a real memory issue; if IT throws, that
            // needs to be visible, not swallowed the same way a routine logging failure would be.
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                var generationInfo = gcInfo.GenerationInfo;
                // .NET's GCGenerationInfo array is always ordered Gen0, Gen1, Gen2, LOH, POH.
                var lohSizeMB = generationInfo.Length > 3 ? generationInfo[3].SizeAfterBytes / (1024.0 * 1024.0) : 0;
                var managedMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                var heapSizeMB = gcInfo.HeapSizeBytes / (1024.0 * 1024.0);
                var committedMB = gcInfo.TotalCommittedBytes / (1024.0 * 1024.0);
                var workingSetMB = Environment.WorkingSet / (1024.0 * 1024.0);

                ManaLog.WriteVerbose(
                    $"[MemStats] managed={managedMB:0}MB heap={heapSizeMB:0}MB loh={lohSizeMB:0}MB " +
                    $"committed={committedMB:0}MB workingSet={workingSetMB:0}MB " +
                    $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)} " +
                    $"serverGC={System.Runtime.GCSettings.IsServerGC}");
            }
            catch (Exception ex)
            {
                ManaLog.WriteDiagnostic(ex);
            }
        }


        private string? _meshDownloadPath;
        private string? _meshDownloadFailedPath;
        private int _meshDownloadGeneration;

        private void UpdateVoxelPointerInteraction(bool inputCaptured)
        {
            var MouseState = Context.Input.Mice.FirstOrDefault();
            if (MouseState == null)
            {
                return;
            }

            // Painting a grid map owns the pointer outright: clicks paint cells rather than selecting.
            if (UpdateGridMapPainting(inputCaptured, MouseState))
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                _transformGizmo?.Hide();
                HidePolygonOverlay();
                return;
            }

            if (_viewModel.IsSelectToolActive)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                _viewModel.ClearHoveredVoxel();
                // A polygon's point handles come before the gizmo: a handle is smaller, so it is the likelier aim.
                var polygonCaptured = UpdatePolygonEditing(inputCaptured, MouseState);
                var gizmoCaptured = _transformGizmo?.Update(
                    Context.World.Selection.SelectedNode,
                    _viewModel.TransformGizmoMode,
                    _viewModel.ShowTransformGizmo && ManaEditorGizmos.Visible,
                    inputCaptured || polygonCaptured,
                    MouseState) == true;
                UpdateSelectionPointerInteraction(inputCaptured || gizmoCaptured || polygonCaptured, MouseState);
                return;
            }

            _transformGizmo?.Hide();
            HidePolygonOverlay();

            var toolKind = VoxelToolCatalog.GetToolKind(_viewModel.SelectedToolIndex);
            if (toolKind is VoxelToolKind.Paint or VoxelToolKind.FloodFill or VoxelToolKind.Erase)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                UpdateDirectVoxelEditInteraction(inputCaptured, MouseState, toolKind);
                return;
            }

            bool leftMouseDown = MouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            var toolSettings = _viewModel.CreateToolSettings();

            if (inputCaptured)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                _viewModel.ClearHoveredVoxel();
                return;
            }

            // Auto keeps raycasting against real scene geometry, exactly as before. Any other plane locks
            // the tool to a fixed construction plane instead -- the ray intersects that plane at
            // WorkPlaneOffset rather than whatever surface happens to be under the cursor, which is what
            // lets every tool (not just Circle/Text, which only ever used the plane for orientation) draw
            // in open air at a chosen height/depth.
            RayHit hit = toolSettings.WorkPlane == VoxelWorkPlane.Auto
                ? GetProjectedMouseHit()
                : GetWorkPlaneHit(toolSettings.WorkPlane, _viewModel.WorkPlaneOffset);
            VoxelToolPoint? toolPoint = null;

            if (hit.Hit)
            {
                var pos = VoxelRasterizer.SnapHitToAdjacentCenter(hit.Position, hit.Normal);

                _viewModel.SetHoveredVoxel(pos, hit.Normal, Context.World.MainCamera.Owner.Position);
                toolPoint = new VoxelToolPoint(pos, hit.Normal);

                if (leftMouseDown && _viewModel.HasBakedVoxels)
                {
                    _viewModel.ExplodeBakedVoxelsAt(pos);
                }
            }
            else
            {
                _viewModel.ClearHoveredVoxel();
            }

            if (_viewModel.HasBakedVoxels)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                return;
            }

            var result = _toolController.UpdatePointer(leftMouseDown, toolPoint, toolSettings);
            if (result != null)
            {
                _viewModel.ClearToolPreview();
                _viewModel.CommitToolResult(result);
            }
            else
            {
                _viewModel.SetToolPreview(_toolController.BuildPreview(toolSettings));
            }
        }

        // Paint, Flood Fill and Erase all act on voxels that already exist, so none of them go through
        // _toolController -- it only ever builds NEW geometry from drag points and has no read access to
        // what is already there. Paint/Erase behave like a brush, applying continuously while dragging but
        // debounced to once per cell (the same idea VoxelToolController uses for Stroke); Flood Fill only
        // ever acts once per click, since re-flooding an already-uniform region every frame would be a
        // no-op anyway.
        private void UpdateDirectVoxelEditInteraction(bool inputCaptured, IMouse mouseState, VoxelToolKind toolKind)
        {
            bool leftMouseDown = !inputCaptured && mouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);

            if (inputCaptured || _viewModel.HasBakedVoxels)
            {
                _viewModel.ClearHoveredVoxel();
                _lastDirectEditPosition = null;
                _directEditPointerWasDown = false;
                return;
            }

            var workPlane = (VoxelWorkPlane)Math.Clamp(_viewModel.SelectedWorkPlaneIndex, 0, _viewModel.WorkPlaneOptions.Length - 1);
            var hit = workPlane == VoxelWorkPlane.Auto
                ? GetProjectedMouseHit()
                : GetWorkPlaneHit(workPlane, _viewModel.WorkPlaneOffset);
            if (!hit.Hit)
            {
                _viewModel.ClearHoveredVoxel();
                _lastDirectEditPosition = null;
                _directEditPointerWasDown = false;
                return;
            }

            // The mirror of the placement cursor: this steps INTO the surface, landing on the voxel that
            // was actually hit rather than the empty cell beside it that Stroke/Line/etc would place into.
            var occupiedPos = VoxelRasterizer.SnapHitToOccupiedCenter(hit.Position, hit.Normal);
            _viewModel.SetHoveredVoxel(occupiedPos, hit.Normal, Context.World.MainCamera.Owner.Position);

            var justPressed = leftMouseDown && !_directEditPointerWasDown;
            _directEditPointerWasDown = leftMouseDown;

            if (!leftMouseDown)
            {
                _lastDirectEditPosition = null;
                return;
            }

            if (toolKind == VoxelToolKind.FloodFill)
            {
                if (justPressed)
                {
                    _viewModel.FloodFillAt(occupiedPos);
                }

                return;
            }

            if (_lastDirectEditPosition.HasValue &&
                Vector3.DistanceSquared(_lastDirectEditPosition.Value, occupiedPos) < 0.0001f)
            {
                return;
            }

            // Sweeps from the last touched cell to this one rather than a single dab at just the current
            // position -- a mouse move fast enough to skip frames (or a frame hitch) would otherwise leave
            // a gap of untouched voxels between where the cursor was and where it landed.
            var from = _lastDirectEditPosition ?? occupiedPos;
            _lastDirectEditPosition = occupiedPos;

            if (toolKind == VoxelToolKind.Paint)
            {
                _viewModel.PaintAt(from, occupiedPos);
            }
            else
            {
                _viewModel.EraseAt(from, occupiedPos);
            }
        }

        // A second click on the SAME already-picked patch within this window counts as a double-click.
        // No pixel-distance check -- re-picking already implicitly requires the ray to still land on
        // the same node, which is a stronger check than cursor position alone.
        private const long DoubleClickThresholdMs = 400;
        private long _lastSelectClickTimeMs = long.MinValue;

        // Prefers the picked renderer's identity over the node's, since several patches can share one
        // PlanetNode root node -- double-clicking two different sibling patches in quick succession must
        // NOT register as a double-click on the same target.
        private object? _lastSelectClickedTarget;

        // A selected planet patch reflects its on-demand Terrarium up-res status (see
        // PlanetNode.TryBeginPatchUpres) instead of the plain default selection color -- otherwise
        // there's no way to tell from the 3D view whether selecting a patch actually did anything.
        // Everything else selectable keeps the default color unchanged.
        private Vector4 GetSelectionOutlineColor(MeshRendererComponent? selectedRenderer)
        {
            if (_planet == null || !_planet.TryGetPatchKey(selectedRenderer, out var patchKey))
            {
                return SelectionOutlineColor;
            }

            return _planet.GetPatchUpresStatus(patchKey) switch
            {
                PatchUpresStatus.InProgress => PatchUpresInProgressOutlineColor,
                PatchUpresStatus.Done => PatchUpresDoneOutlineColor,
                PatchUpresStatus.Failed => PatchUpresFailedOutlineColor,
                _ => SelectionOutlineColor,
            };
        }

        // Diffs against last frame's set rather than blindly turning everything on/off every frame --
        // patches finish streaming (or get swapped out entirely by a split/merge finalize) constantly,
        // so this avoids fighting the selection/active-voxel outline blocks above over the same
        // MeshRendererComponent. Component-grained (not node-grained), since several unrelated patches
        // can share one root node -- a streaming patch must not light up its idle siblings.
        private void UpdateStreamingActivityOutlines(MeshRendererComponent? selectedComponent, ManaSceneNode? activeNode)
        {
            var currentComponents = _planet != null
                ? new HashSet<MeshRendererComponent>(_planet.GetStreamingActivityComponents())
                : new HashSet<MeshRendererComponent>();

            foreach (var component in _lastStreamingHighlightedComponents)
            {
                if (currentComponents.Contains(component) || component == selectedComponent || component.Owner == activeNode)
                {
                    continue;
                }

                component.EditorOutlineEnabled = false;
            }

            foreach (var component in currentComponents)
            {
                if (component == selectedComponent || component.Owner == activeNode)
                {
                    continue;
                }

                component.EditorOutlineEnabled = true;
                component.EditorOutlineColor = PatchUpresInProgressOutlineColor;
                component.EditorOutlineWidth = 4.0f;
            }

            _lastStreamingHighlightedComponents = currentComponents;
        }

        private bool ShouldCaptureEditorMouseInput()
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return false;
            }

            // While playing, a held left button is the game's business. Reporting it as an editor capture
            // would stand the camera down and freeze the spring arm mid-orbit.
            if (IsPlaying)
            {
                return false;
            }

            // A live placement preview claims the scroll wheel for its own rotation (see
            // UpdateMeshPickerPlacementPreviewTransform) whether or not a mouse button happens to be
            // held -- the right-click "Place" flow positions the preview with no button down at all.
            if (_meshPickerPreviewNode != null)
            {
                return true;
            }

            var mouse = Context.Input.Mice.FirstOrDefault();
            return _toolController.IsDrawing || mouse?.IsButtonPressed(Silk.NET.Input.MouseButton.Left) == true;
        }


        public override void Render(double deltaTime)
        {
            // See ManaWebGpuContext.SuspendSceneWork. Set before base.Render so it is in place before
            // ClearFrame runs later in the same frame, on the WebGPU path only -- null on OpenGL, which
            // already gets this for free from BaseGameMode.Render never calling DrawWorld while loading.
            if (_webGpuContext != null)
            {
                _webGpuContext.SuspendSceneWork = IsLoading;
            }

            // Was called from inside DrawImGui, back when that was the one method BaseGameMode.Render
            // always reached every non-loading frame. Now that ImGui itself is skipped entirely for this
            // mode (see BaseGameMode.Render's EnableManaUi gate), DrawImGui never runs at all -- so this
            // has to be called from somewhere that still does, or none of it runs either: no mesh-picker
            // placement, no drag-and-drop, no pawn driving, no planet auto-LOD, no view-option syncing.
            if (!IsLoading)
            {
                using (ManaFrameProfiler.Measure("Editor Per-Frame State"))
                {
                    UpdateEditorPerFrameState();
                }
            }

            base.Render(deltaTime);
        }

        /// <summary>
        /// Everything DrawImGui used to do that was not actually drawing an ImGui window: selection/active-
        /// layer outline sync, planet streaming and auto-LOD, the view-option toggles the menu bar drives,
        /// mesh-picker drag-and-drop, voxel pointer interaction, pawn driving input, and place-search
        /// fly-to. All of it has to run every non-loading frame regardless of which UI is on screen.
        /// </summary>
        private void UpdateEditorPerFrameState()
        {
            PushWebGpuFrameStats();
            LogMemoryStatsPeriodically();
            ManaMemoryStats.Sample();

            // Here rather than in OnLoadingComplete, which the loading coordinator calls one line before it
            // marks loading finished -- so IsLoading is still true there, and every level operation guards
            // on exactly that. This method's first call is the first frame that is genuinely past loading.
            OpenStartupLevelIfRequested();
            TickCommandFileIfRequested();

            UpdatePlayModeShortcuts();
            UpdateEditShortcuts();
            _trackDebugVisualization?.SetVisibility(
                _viewModel.ShowTrackBounds,
                _viewModel.ShowTrackPivots,
                _viewModel.ShowTrackSnapPoints);

            // While playing, everything below this line is the editor acting on a pointer that now belongs
            // to the game: picking, voxel placement, mesh drag-and-drop, selection outlines. Only the things
            // the world needs regardless -- streaming, and the character's own input -- carry on.
            if (IsPlaying)
            {
                _transformGizmo?.Hide();
                HidePolygonOverlay();
                ClearSelectionColliderDebug();
                HideGridMapEditorVisuals();
                _planetScenery.PumpStreaming(UpdateFrame);
                _pawnController?.UpdateDriveInput(UiWantsKeyboard);
                return;
            }

            // Keep the active voxel node highlighted through the shared post-process outline pass.
            var activeNode = _viewModel.VoxelNode;
            var activeOutlineColor = new Vector4(1.0f, 0.88f, 0.18f, 1.0f);
            foreach (var scene in Context.World.Scenes)
            {
                foreach (var child in scene.Children)
                {
                    if (child is ManaVoxelsNode voxelNode)
                    {
                        bool isActive = (voxelNode == activeNode);
                        voxelNode.ShowOutline = isActive;

                        foreach (var meshRenderer in voxelNode.Components.OfType<MeshRendererComponent>())
                        {
                            meshRenderer.EditorOutlineEnabled = isActive;
                            meshRenderer.EditorOutlineColor = activeOutlineColor;
                            meshRenderer.EditorOutlineWidth = 4.0f;
                        }

                        // Remove selection cages created by older builds now that selection uses the mask pass.
                        var legacyOutline = voxelNode.Children.FirstOrDefault(node => node.Name == "VoxelSelectionOutline");
                        if (legacyOutline != null)
                        {
                            voxelNode.RemoveChild(legacyOutline);
                        }
                    }
                }
            }

            // Highlight whatever the Outliner or viewport select tool currently has selected, distinct from
            // the active-voxel-layer highlight above (skipped when they're the same node, to avoid flicker).
            // A component-level selection (e.g. one patch on a PlanetNode shared root node) only outlines
            // that ONE renderer, not every sibling on the same node -- a plain node-level selection (KML
            // lines, shapes, a bare shared root) still blanket-highlights every renderer on the node.
            // Diffs against the EXACT set of components lit last frame (not just node/component identity)
            // -- keying off identity alone missed the transition from "whole node lit" to "one specific
            // component on that SAME node lit," since the node itself hadn't changed, leaving every other
            // sibling stuck outlined forever.
            var selectedNode = Context.World.Selection.SelectedNode;
            var selectedComponent = Context.World.Selection.SelectedComponent as MeshRendererComponent;
            SyncSelectionColliderDebug(_viewModel.ShowSelectedColliderBounds ? selectedNode : null);

            HashSet<MeshRendererComponent> targetOutlinedComponents;
            if (selectedComponent != null)
            {
                targetOutlinedComponents = new HashSet<MeshRendererComponent> { selectedComponent };
            }
            else if (selectedNode != null && selectedNode != activeNode)
            {
                targetOutlinedComponents = new HashSet<MeshRendererComponent>(selectedNode.Components.OfType<MeshRendererComponent>());
            }
            else
            {
                targetOutlinedComponents = new HashSet<MeshRendererComponent>();
            }

            foreach (var meshRenderer in _lastOutlinedSelectionComponents)
            {
                if (!targetOutlinedComponents.Contains(meshRenderer))
                {
                    meshRenderer.EditorOutlineEnabled = false;
                }
            }

            if (targetOutlinedComponents.Count > 0)
            {
                var outlineColor = selectedComponent != null ? GetSelectionOutlineColor(selectedComponent) : GetSelectionOutlineColor(null);
                foreach (var meshRenderer in targetOutlinedComponents)
                {
                    meshRenderer.EditorOutlineEnabled = true;
                    meshRenderer.EditorOutlineColor = outlineColor;
                    meshRenderer.EditorOutlineWidth = 4.0f;
                }
            }

            _lastOutlinedSelectionComponents = targetOutlinedComponents;

            // Also highlight every OTHER patch currently doing background streaming work (in-place
            // up-res, splitting, merging) -- not just the selected one. With auto-LOD running, several
            // patches can be streaming at once with no way to see how much/which without this (besides
            // the World Perf panel's aggregate counts, see PushPlanetStreamingStatusToWorldPerf).
            UpdateStreamingActivityOutlines(selectedComponent, activeNode);

            // Called every frame the patch is selected, not just once on selection change -- covers both
            // the Outliner tree AND clicking a patch directly in the 3D view, since both already write to
            // the same SelectedComponent via UpdateSelectionPointerInteraction's ManaScenePicker.Pick call.
            // TryBeginPatchUpres is documented as safe/idempotent to call repeatedly (a no-op once the
            // patch is InProgress/Done/Failed) -- calling it only once per selection change used to leave
            // a patch stuck forever if that single call happened to lose the non-blocking _rebuildLock
            // race to some other in-flight background operation (split/merge/another patch's up-res),
            // since nothing would retry until the user deselected and reselected. Calling every frame
            // lets a lost race self-heal on the very next frame instead.
            if (_planet != null && _planet.TryGetPatchKey(selectedComponent, out var patchKey))
            {
                _planet.TryBeginPatchUpres(patchKey);
            }

            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();
            SyncWaterVisibility();
            SyncWireframeViewOption();
            SyncNormalsViewOption();
            _planetScenery.SyncKmlAirspaceVisibility();
            SyncGridRoomShape();
            SyncGridRoomWalls();
            SyncVisualBoundsColliders();
            _planetScenery.ProcessPendingRegionRequest();

            _planetScenery.PumpStreaming(UpdateFrame);

            // The whole window, in the same coordinate space as the mouse: dropping used to be bounded by
            // the ImGui main viewport (minus a manual carve-out for ImGui's own import window), but with
            // ImGui no longer drawing anything there is no second window to carve out any more. The real
            // exclusion is ManaUI's own hit test below (MeshImportRequests.PointerOverUi) -- this is only
            // the coarse "is the pointer even inside the window" bound it was always meant to be.
            var windowSize = Context.Window.Size;
            var sceneMin = Vector2.Zero;
            var sceneMax = new Vector2(windowSize.X, windowSize.Y);

            var mouseState = Context.Input.Mice.First();

            var mouseStart = new Vector3(mouseState.Position, 0.0f);
            var mouseEnd = new Vector3(mouseState.Position, 1.0f);

            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var rayOrigin = ray.Origin;
            var rayDirection = ray.Direction;
            _viewModel.DebugRayOrigin = rayOrigin;
            _viewModel.DebugRayDirection = rayDirection;

            ProcessMeshPickerPlacement(mouseState, sceneMin, sceneMax);

            // The gizmo used to have to report back whether it was under the pointer, because an ImGui
            // window drawn over the scene is invisible to the scene. As part of the ManaUI tree it is
            // hit-tested like anything else, and UiWantsMouse already knows.
            UpdateVoxelPointerInteraction(UiWantsMouse);
            UpdateFrameSelectedShortcut();
            UpdateDropSelectedToFloorShortcut();
            UpdateEscapeSelectionShortcut();
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);

            // The ManaUI search panel knows a longitude and a latitude and nothing else; the planet, camera
            // and scene it needs are here.
            if (PlaceSearchRequests.TakeFlyTo() is { } destination
                && _planet != null
                && Context.World.MainCamera != null)
            {
                _navigator.FlyTo(destination, Context, _planet, Context.World.MainCamera);
            }
        }

        private void SyncLightningBallVisibility()
        {
            if (_lightningBall == null)
            {
                return;
            }

            var sceneChildren = Context.World.DefaultScene.Children;
            if (_viewModel.ShowLightningBall)
            {
                if (!sceneChildren.Contains(_lightningBall))
                {
                    sceneChildren.Add(_lightningBall);
                }
            }
            else
            {
                sceneChildren.Remove(_lightningBall);
            }
        }

        private void SyncLiquidBallVisibility()
        {
            if (_liquidBall == null)
            {
                return;
            }

            var sceneChildren = Context.World.DefaultScene.Children;
            if (_viewModel.ShowLiquidBall)
            {
                if (!sceneChildren.Contains(_liquidBall))
                {
                    sceneChildren.Add(_liquidBall);
                }
            }
            else
            {
                sceneChildren.Remove(_liquidBall);
            }
        }

        private void SyncWaterVisibility()
        {
            if (_water == null)
            {
                return;
            }

            _water.MeshRenderer.IsVisible = _viewModel.ShowWater;
        }

        // Bridges the View menu's plain bool (VoxelEditorViewModel, ManaVoxel layer) into the renderer-
        // facing settings object (ManaCore layer) that ManaWebGpuContext.Demo.cs's DrawWireframeOverlay
        // actually reads -- same cross-layer path already used for WebGpuDebugMode, just driven from a
        // View menu item instead of the Irradiance panel since that's where every other "Show X" view
        // toggle in this app already lives.
        private void SyncWireframeViewOption()
        {
            Context.World.IrradianceSettings.WireframeEnabled = _viewModel.ShowWireframe;
            Context.World.IrradianceSettings.VertexNormalsEnabled = _viewModel.ShowVertexNormals;
            Context.World.IrradianceSettings.VertexNormalLength = _viewModel.VertexNormalLength;
        }

        // Mode 2 in pbr_irradiance.wgsl's debug ladder -- world normal as n * 0.5 + 0.5.
        private const float WorldNormalDebugMode = 2.0f;
        private float? _debugModeBeforeNormals;

        // Drives the same WebGpuDebugMode the Irradiance panel does, so the previous mode is saved and
        // restored rather than clobbered to 0 on toggle-off.
        private void SyncNormalsViewOption()
        {
            var settings = Context.World.IrradianceSettings;
            if (_viewModel.ShowNormals)
            {
                _debugModeBeforeNormals ??= settings.WebGpuDebugMode;
                settings.WebGpuDebugMode = WorldNormalDebugMode;
            }
            else if (_debugModeBeforeNormals != null)
            {
                settings.WebGpuDebugMode = _debugModeBeforeNormals.Value;
                _debugModeBeforeNormals = null;
            }
        }

        // Demo hook for testing ripples: left-click over the water plane's footprint. Water has no
        // physics collider, so this is a plain ray/plane test against water.Position.Y rather than
        // going through GetProjectedMouseHit() - the ground plane's physics collider sits at the
        // same height as the water, so gating on "physics raycast missed" would almost always skip.
        private void UpdateWaterRippleInteraction()
        {
            var water = _water;
            if (water == null || !_viewModel.ShowWater)
            {
                return;
            }

            var mouse = Context.Input.Mice.FirstOrDefault();
            if (mouse == null)
            {
                return;
            }

            bool leftMouseDown = mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            bool clicked = leftMouseDown && !_waterRipplePointerWasDown;
            _waterRipplePointerWasDown = leftMouseDown;

            if (!clicked)
            {
                return;
            }

            var mouseStart = new Vector3(mouse.Position, 0.0f);
            var mouseEnd = new Vector3(mouse.Position, 1.0f);
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);

            var waterOrigin = water.WorldTransform.Translation;
            var waterNormal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, water.WorldTransform));
            var denominator = Vector3.Dot(ray.Direction, waterNormal);
            if (MathF.Abs(denominator) < 0.0001f)
            {
                return;
            }

            var travel = Vector3.Dot(waterOrigin - ray.Origin, waterNormal) / denominator;
            if (travel <= 0.0f)
            {
                return;
            }

            var worldHit = ray.Origin + ray.Direction * travel;
            if (!Matrix4x4.Invert(water.WorldTransform, out var worldToWater))
            {
                return;
            }

            var localHit = Vector3.Transform(worldHit, worldToWater);
            var halfSize = water.Mesh.Size * 0.5f;
            if (MathF.Abs(localHit.X) > halfSize.X || MathF.Abs(localHit.Z) > halfSize.Y)
            {
                return;
            }

            water.SpawnRipple(worldHit);
        }

    }
}
