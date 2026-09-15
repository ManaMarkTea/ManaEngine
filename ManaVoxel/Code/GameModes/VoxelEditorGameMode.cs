#if !MANA_BROWSER  // ImGui.NET has no browser-wasm asset; see ManaGraphics.csproj
using ImGuiNET;
#endif
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
using ManaVoxel.Code.Search;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geo;
using ManaGeoTools.Code.GeoTiff;
using ManaGeoTools.Code.GeoTiff.Terrarium;
using ManaGeoTools.Code.Kml;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Region;
using ManaGeoTools.Code.Shapefile;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    public partial class VoxelEditorGameMode : BaseGameMode
    {
        private const string ManaWindowTitle = "Mana";
        private uint _toolDockId;
        private uint _outlinerDockId;
        private uint _manaDockId;
        private uint _utilityDockId;
        private readonly VoxelEditorViewModel _viewModel = new();
#if !MANA_BROWSER  // legacy ImGui-only panel views, superseded by their ManaUi counterparts
        private readonly VoxelEditorMainMenuView _mainMenuView = new();
        private readonly VoxelToolPaletteView _toolPaletteView = new();
        private readonly VoxelEditorPanelsView _panelsView = new();
#endif
        private readonly PlaceSearchNavigator _navigator = new();
#if !MANA_BROWSER  // legacy ImGui-only panel views, superseded by their ManaUi counterparts
        private readonly VoxelEditorAboutPanelView _aboutPanelView = new();
        private readonly VoxelSearchPanelView _searchPanelView = new();
#endif
        private readonly VoxelToolController _toolController = new();
        private readonly VoxelFileImportService _fileImportService = new();
        private ManaPawnController? _pawnController;
        private PlayModeController? _playMode;
        private TrackDebugVisualization? _trackDebugVisualization;
        private SceneTransformGizmo? _transformGizmo;
        private const string ZipAssetPrefix = "zipasset:";
        private bool _firstFrame = true;
        private bool _meshPickerDragWasDown;
        private bool _meshPickerPlaceClickWasDown;
        private bool _meshPickerEscapeWasDown;
        private string? _meshPickerPreviewAssetPath;
        private ManaSceneNode? _meshPickerPreviewNode;
        private int _meshPickerPreviewSceneStartIndex = -1;
        private int _meshPickerPreviewMeshStartIndex = -1;
        private int _meshPickerPreviewMaterialStartIndex = -1;
        private EditorSelectionSnapshot? _meshPickerPreviewSelectionBefore;
        private bool _selectPointerWasDown;
        private bool _directEditPointerWasDown;
        private Vector3? _lastDirectEditPosition;
        private bool _frameSelectedKeyWasDown;
        private bool _escapeSelectionKeyWasDown;
        private HashSet<MeshRendererComponent> _lastOutlinedSelectionComponents = new();
        private HashSet<MeshRendererComponent> _lastStreamingHighlightedComponents = new();
        private readonly HashSet<ManaSceneNode> _pickingExclusions = new();
        private static readonly Vector4 SelectionOutlineColor = new(0.30f, 0.72f, 1.0f, 1.0f);
        private static readonly Vector4 ColliderDebugColor = new(1.0f, 0.86f, 0.05f, 1.0f);
        private static readonly Vector4 PatchUpresInProgressOutlineColor = new(1.0f, 0.55f, 0.0f, 1.0f);
        private static readonly Vector4 PatchUpresDoneOutlineColor = new(0.25f, 0.85f, 0.25f, 1.0f);
        private static readonly Vector4 PatchUpresFailedOutlineColor = new(0.9f, 0.2f, 0.2f, 1.0f);
        private ManaSceneNode? _selectionColliderDebugRoot;
        private ManaSceneNode? _selectionColliderDebugTarget;
        private readonly List<ColliderDebugEntry> _selectionColliderDebugEntries = new();

        public ManaSceneNode Cursor { get; private set; } = null!;
        private ManaSceneNode? _lightningBall = null;
        private ManaSceneNode? _liquidBall = null;
        private PlanetNode? _planet;
        private RegionNode? _region;
        private ManaSceneNode? _kmlDemo;
        private bool _kmlAirspaceAttached;
        private Task<ManaSceneNode?>? _kmlAirspaceLoadTask;
        private ManaSceneNode? _countryBorders;
        private ManaSceneNode? _stateBorders;
        private ManaWaterNode? _water;
        private bool _waterRipplePointerWasDown;
        private ManaWebGpuContext? _webGpuContext;
#if !MANA_BROWSER  // state for the dead terrain-cache-size block below
        private static readonly object TerrainCacheSizeLock = new();
        private static Task<TerrainCacheSizeRefreshResult>? _terrainCacheSizeRefreshTask;
        private static int _terrainCacheSizeRefreshGeneration;
        private static long? _terrainCacheSizeBytes;
        private static DateTimeOffset? _terrainCacheSizeUpdatedAt;
        private static string? _terrainCacheSizeError;

        private readonly record struct TerrainCacheSizeRefreshResult(
            int Generation,
            long? Bytes,
            string? Error);
#endif

        private sealed record ColliderDebugEntry(
            ManaCollisionComponent Collision,
            ManaSceneNode Node,
            Vector3 Size);

        public override bool DeferGpuUploadsOnStartup => true;

        // v3: Inspector now drawn first each frame so it lands as the first utility tab
        // (dock order follows Begin() submission order on a fresh layout, not DockBuilder call order).
        protected override int DockLayoutVersion => 3;

        public VoxelEditorGameMode(ManaGraphicsBackendKind graphicsBackend = ManaGraphicsBackendKind.OpenGL) : base(graphicsBackend)
        {

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
                    _ImGui = webGpuContext.CreateImGuiController(input);
#if !MANA_BROWSER  // ImGui-only setup (docking flag, style, default font) -- ImGui itself is a no-op controller on browser
                    var io = ImGuiNET.ImGui.GetIO();
                    io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;
                    ConfigImStyle();

                    if (GetAvailableImGuiFontPresets().Contains(ImGuiFontPreset.SegoeUIMono))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SegoeUIMono);
                    }
#endif
                }
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

        // ManaUI runs alongside ImGui while the widget library is built out; the showcase panel is the
        // standing visual regression check for layout, text and the SDF primitive.
        protected override bool EnableManaUi => true;

        private readonly ManaUiShowcaseViewModel _manaUiShowcase = new();

        protected override void BuildManaUi(global::Mana.Code.UI.ManaUi.ManaUiHost host)
        {
            // RenderFrame is plain engine state with no change notification, which is exactly the case the
            // polled binding exists for.
            // The same view model the ImGui palette uses, so both drive the editor and stay in step.
            var workspace = ManaUiDockDemo.Attach(
                host.Root,
                _manaUiShowcase,
                () => $"render frame {RenderFrame:N0}",
                editor: _viewModel,
                world: Context.World);

            // Play collapses the whole workspace away and leaves the controls hint. Polled rather than
            // rebuilt on the switch, because _playMode does not exist yet -- this runs before Load.
            host.Root.Content = new ManaUiPlayOverlay(
                workspace,
                ManaUiPlayHud.Build(host.Root.Theme, GetPlayHudText),
                () => IsPlaying);
        }

        public override void Load()
        {
            LoadingCoordinator.Begin();
#if !MANA_BROWSER  // see the InitCustomNodeInspectorHooks definition below
            InitCustomNodeInspectorHooks();
#endif

            var world = Context.World;
            _transformGizmo = new SceneTransformGizmo(world, _viewModel.History);
            _trackDebugVisualization = new TrackDebugVisualization(world);

            // Shares the editor's picking exclusions so a click-to-move order falls through the grid
            // planes onto the ground plane, exactly like the voxel placement raycast does.
            _pawnController = new ManaPawnController(Context)
            {
                PickingExclusions = _pickingExclusions,
                GroundPlaneHeight = 0.0f
            };
            _viewModel.AttachPawnController(_pawnController);
            _pawnController.ChangeCharacterRequested += direction => _viewModel.CycleControlledCharacter(direction);

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
                    var envPath = world.EnvironmentReflectionSource.SourcePath;
                    var envTexture = TextureManager.GetInstance(Context).CreateTexture(envPath);
                    envTexture.OriginalFilename = envPath;
                    envTexture.Load();

                    // 1. Generate shapes CPU data
                    Loading.Report(new ManaLoadingProgress("Generating procedural shapes", 0.2f));

                    Context.Window.FileDrop += Window_FileDrop;
                    _viewModel.AttachScene(Context, Context.World.DefaultScene);
                    Context.World.MainCamera.ShouldCaptureMouseInput = ShouldCaptureEditorMouseInput;

                    // Doubled from the original 256 ("extend those grids farther") -- grid.wgsl/grid.frag's
                    // own whole-plane-boundary tier and UV-reconstruction scale are hardcoded to match, so
                    // changing this alone is not enough; see the RoomSize constant note there.
                    float gridSize = 512.0f;
                    float halfGridSize = gridSize * 0.5f;

                    // The rounded cove is an LCARS-only flourish, not a general redesign of the grid -- the
                    // plain Dark/Light graph-paper floor keeps its original sharp corners. This is decided
                    // once here rather than kept live against later theme switches: swapping the corners'
                    // actual topology on the fly would mean tearing down and rebuilding these nodes (plus
                    // their physics bodies and picking exclusions) from outside this method, which is a
                    // bigger seam than a menu click warrants right now.
                    bool rounded = ManaUi?.Root?.Theme?.Kind == UiThemeKind.Lcars;

                    // How far each wall gets trimmed back from its two neighbours so a rounded cove (see
                    // ManaGridCoveFillet) can fill each gap instead of a sharp seam. Floor and ceiling are
                    // never trimmed -- every wall still meets them at its original, sharp edge. What rounds
                    // is the four vertical seams where the walls meet each other, which is what actually
                    // reads as a rounded room -- the flat walls blending into a cylindrical curve at every
                    // corner -- rather than a bevel where wall meets floor or ceiling.
                    float coveRadius = rounded ? 4.0f : 0.0f;
                    float uvTrim = coveRadius / gridSize;
                    float uvSpan = 1.0f - 2.0f * uvTrim;

                    // Full enclosed box: floor, ceiling, and all four walls. xzPlane/yxPlane/zyPlane keep
                    // their original names and positions (the "near" faces at Y=0/Z=0/X=0); the other three
                    // are their mirrors at Y=gridSize/Z=gridSize/X=gridSize.
                    ManaSceneNode xzPlane, yxPlane, zyPlane, ceilingPlane, wallZFar, wallXFar;
                    ManaSceneNode?[] wallCoves = new ManaSceneNode?[4];

                    ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

                    lock (world.SceneLock)
                    {
                        // Floor and ceiling: full extent, never trimmed -- see the comment above.
                        xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize, "Grid Plane XZ");
                        ceilingPlane = shapes.AddQuad(new Vector3(0, gridSize, 0), Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize, "Grid Plane Ceiling");

                        // The four walls. Each is trimmed at BOTH ends of its horizontal extent once rounded
                        // -- every wall touches two of the room's four vertical corners, not one.
                        yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, gridSize, coveRadius, gridSize - coveRadius, "Grid Plane YX");
                        zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, coveRadius, gridSize - coveRadius, 0, gridSize, "Grid Plane ZY");
                        wallZFar = shapes.AddQuad(new Vector3(0, 0, gridSize), Colors.White, ManaQuadPlanes.YX, 0, gridSize, coveRadius, gridSize - coveRadius, "Grid Plane YX Far");
                        wallXFar = shapes.AddQuad(new Vector3(gridSize, 0, 0), Colors.White, ManaQuadPlanes.ZY, coveRadius, gridSize - coveRadius, 0, gridSize, "Grid Plane ZY Far");

                        if (rounded)
                        {
                            // UV remap so every trimmed wall's tiles keep their true 8-unit size and stay
                            // aligned with their coves -- see grid.wgsl/grid.frag's use of uv_transform.
                            // Floor and ceiling need none: their UV was never touched.
                            //
                            // ManaQuadPlanes.YX: axis=Y (untouched, full height), extrusion=X (trimmed) --
                            // the quad's V (extrusion/UV.y) axis needs remapping for both Z walls.
                            foreach (var yxWall in new[] { yxPlane, wallZFar })
                            {
                                if (yxWall.Components[0] is MeshRendererComponent yxRenderer)
                                {
                                    yxRenderer.Material.UvScale = new Vector2(1.0f, uvSpan);
                                    yxRenderer.Material.UvOffset = new Vector2(0.0f, uvTrim);
                                }
                            }

                            // ManaQuadPlanes.ZY: axis=Z (trimmed), extrusion=Y (untouched, full height) --
                            // the U axis (UV.x) needs remapping for both X walls instead.
                            foreach (var zyWall in new[] { zyPlane, wallXFar })
                            {
                                if (zyWall.Components[0] is MeshRendererComponent zyRenderer)
                                {
                                    zyRenderer.Material.UvScale = new Vector2(uvSpan, 1.0f);
                                    zyRenderer.Material.UvOffset = new Vector2(uvTrim, 0.0f);
                                }
                            }

                            // The four vertical wall-to-wall coves, each swept the full height and placed
                            // at its own corner. crossA/crossB are the two adjoining walls' own inward
                            // normals -- see ManaGridCoveFillet's doc comment for the general two-plane
                            // shape, and crossA x crossB = +Y is kept the same at every corner so all four
                            // sweep the same rotational direction and none end up wound backwards.
                            var corners = new (Vector3 Position, Vector3 CrossA, Vector3 CrossB, string Name)[]
                            {
                                (new Vector3(0, 0, 0), Vector3.UnitZ, Vector3.UnitX, "Grid Cove X0-Z0"),
                                (new Vector3(0, 0, gridSize), Vector3.UnitX, -Vector3.UnitZ, "Grid Cove X0-ZFar"),
                                (new Vector3(gridSize, 0, gridSize), -Vector3.UnitZ, -Vector3.UnitX, "Grid Cove XFar-ZFar"),
                                (new Vector3(gridSize, 0, 0), -Vector3.UnitX, Vector3.UnitZ, "Grid Cove XFar-Z0"),
                            };

                            for (var i = 0; i < corners.Length; i++)
                            {
                                var (position, crossA, crossB, name) = corners[i];
                                var cove = shapes.AddGridCoveFillet(
                                    Vector3.UnitY, crossA, crossB, gridSize, coveRadius, position: position, name: name);
                                cove.LevelSource = null;
                                wallCoves[i] = cove;
                            }
                        }

                        Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
                        Cursor.Name = "CursorBall";

                        // Demo scenery, not level content: the shape factory tags everything it builds so
                        // placed props round-trip, and these are the ones the game mode puts there itself.
                        // Left tagged they would be saved into every level and then loaded a second time on
                        // top of the copies this code always makes. See ManaLevelSource.
                        xzPlane.LevelSource = null;
                        ceilingPlane.LevelSource = null;
                        yxPlane.LevelSource = null;
                        zyPlane.LevelSource = null;
                        wallZFar.LevelSource = null;
                        wallXFar.LevelSource = null;
                        Cursor.LevelSource = null;

                        // A donut that spins and bobs like a pickup. Its motion component has no inspector
                        // widget on purpose: selecting this is the standing demonstration that the
                        // reflection fallback turns a plain component into a working editor.
                        var pickup = shapes.AddTorus(new Vector3(60, 1.5f, 60), Colors.Gold, 2.0f);
                        pickup.Name = "Pickup Donut";
                        pickup.Components.Add(new ManaPickupMotionComponent(pickup));
                        pickup.LevelSource = null;
                    }

                    // Painted with whatever theme the UI already came up in -- BuildManaUi runs before
                    // Load, so ManaUi.Root.Theme reflects the real starting theme by the time this
                    // background task gets here. Without this the planes would sit on the
                    // ManaQuad.GenerateMaterial() fallback colour regardless of theme, since
                    // ManaUiDockDemo's own ApplyGridTheme call fires from BuildManaUi -- before these
                    // nodes exist -- and finds nothing to repaint.
                    ApplyGridTheme(new[] { xzPlane, ceilingPlane, yxPlane, zyPlane, wallZFar, wallXFar }
                        .Concat(wallCoves).ToArray());

                    // Colliders stay full-size flat boxes regardless of the visual trim -- a voxel
                    // placement raycast does not need the coves' few units of curvature to be accurate.
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, 0.1f, gridSize), new Vector3(halfGridSize, 0, halfGridSize));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, 0.1f, gridSize), new Vector3(halfGridSize, gridSize, halfGridSize));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, 0.1f), new Vector3(halfGridSize, halfGridSize, 0));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, 0.1f), new Vector3(halfGridSize, halfGridSize, gridSize));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, gridSize, gridSize), new Vector3(0, halfGridSize, halfGridSize));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, gridSize, gridSize), new Vector3(gridSize, halfGridSize, halfGridSize));

                    // Ground/wall planes are raycast helpers for voxel placement, not meaningful selection targets.
                    _pickingExclusions.Add(xzPlane);
                    _pickingExclusions.Add(ceilingPlane);
                    _pickingExclusions.Add(yxPlane);
                    _pickingExclusions.Add(zyPlane);
                    _pickingExclusions.Add(wallZFar);
                    _pickingExclusions.Add(wallXFar);
                    foreach (var cove in wallCoves)
                    {
                        if (cove != null)
                        {
                            _pickingExclusions.Add(cove);
                        }
                    }

                    _pickingExclusions.Add(Cursor);

                    // Probe for optional KML placemarks. Deliberately only
                    // a file-existence check -- the actual parse/mesh build happens lazily the first time
                    // View > Show Airspace (KML) is switched on, so startup never pays for an overlay
                    // that's off by default (see SyncKmlAirspaceVisibility).
                    Loading.Report(new ManaLoadingProgress("Checking for KML placemarks", 0.9f));
                    _viewModel.KmlAirspaceAvailable = File.Exists(KmlDemoFilePath);

                    LoadingCoordinator.CompleteBackgroundWork();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during background asset loading: {ex}");
                    LoadingCoordinator.Fail(ex);
                }
            });
        }

        /// <summary>
        /// Paints the ground-plane grid quads with the active UI theme's floor/line colours. Also called
        /// from <see cref="ManaVoxel.Code.UI.ManaUi.ManaUiDockDemo"/> on every later theme switch; this
        /// call is the one that matters for the very first frame, since it runs after the planes exist.
        /// </summary>
        private void ApplyGridTheme(params ManaSceneNode?[] planes)
        {
            var theme = ManaUi?.Root?.Theme;
            if (theme == null)
            {
                return;
            }

            foreach (var plane in planes)
            {
                if (plane == null)
                {
                    continue;
                }

                foreach (var renderer in plane.GetComponents<MeshRendererComponent>())
                {
                    // Albedo's alpha is repurposed by ManaGridCoveFillet as an "is this a cove, not a flat
                    // plane" flag the shader reads to suppress its cross-axis line (see grid.wgsl/grid.frag)
                    // -- preserved here rather than overwritten so a later theme switch's recolour doesn't
                    // silently turn every cove back into a flat plane as far as the shader can tell.
                    var isFillet = renderer.Material.Albedo.Value.W;
                    renderer.Material.Albedo.Value = theme.GridFloor with { W = isFillet };
                    renderer.Material.Emission.Value = theme.GridLine;
                    renderer.Material.EmissionStrength = theme.GridLineThickness;
                }
            }
        }

        protected override void OnLoadingComplete()
        {
            if (Cursor.Components[0] is MeshRendererComponent renderer)
            {
                var cursorMaterial = renderer.DynamicMaterial;
                cursorMaterial.Name = "Cursor Material";
                cursorMaterial.Shader = new ManaLiquidShader(Context);
            }

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

        private const string PlanetDetailCacheFolder = @"C:\Users\night\Code\ManaAirSpace\Textures\HeightMaps\ETOPO2022";

        // SceneInspectorPanelView (Mana) can't reference PlanetNode/ManaLineMesh-using nodes
        // (ManaGeoTools) directly — wrong dependency direction — so it exposes a hook that ManaVoxel
        // (which references both) fills in. One hook body, not one registration per node type, since
        // CustomNodeInspectorHook is a single delegate slot.
#if !MANA_BROWSER  // SceneInspectorPanelView custom-node ImGui hooks + the terrain-cache-size machinery that only feeds them -- SceneInspectorPanelView itself is browser-excluded (Mana.csproj); TerrainCacheWidget.cs is ManaUi's independent replacement, doesn't call any of this
        private static void InitCustomNodeInspectorHooks()
        {
            SceneInspectorPanelView.CustomNodeInspectorHook = node =>
            {
                if (node is PlanetNode planet)
                {
                    ImGui.SeparatorText("Planet");
                    var exaggeration = planet.HeightExaggeration;
                    if (ImGui.DragFloat("Height Exaggeration", ref exaggeration, 1.0f, 0.0f, 2000.0f))
                    {
                        planet.HeightExaggeration = exaggeration;
                    }

                    var autoLod = planet.AutoLodEnabled;
                    if (ImGui.Checkbox("Auto LOD", ref autoLod))
                    {
                        planet.AutoLodEnabled = autoLod;
                    }

                    DrawTerrainCacheInspector();
                }

                if (node is RegionNode { TerrainMesh: { } terrain })
                {
                    ImGui.SeparatorText("Region Hex Tiling");
                    ImGui.TextDisabled($"{terrain.HexCellCount:N0} hexes at {terrain.EffectiveHexRadiusMeters:0.#} m");

                    // Both re-tessellate the whole region, too slow for every frame of a drag -- so the
                    // value is written live but the rebuild waits for the mouse to come off the control.
                    var rebuild = false;
                    var hexRadius = terrain.HexRadiusMeters;
                    if (ImGui.DragFloat("Hex Radius (m)", ref hexRadius, 0.5f, 0.5f, 2000.0f))
                    {
                        terrain.HexRadiusMeters = hexRadius;
                    }
                    rebuild |= ImGui.IsItemDeactivatedAfterEdit();

                    var stepStrength = terrain.StepStrength;
                    if (ImGui.SliderFloat("Step Strength", ref stepStrength, 0.0f, 1.0f))
                    {
                        terrain.StepStrength = stepStrength;
                    }
                    rebuild |= ImGui.IsItemDeactivatedAfterEdit();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "1 = flat plateaus with vertical walls between them, 0 = a continuous surface\n" +
                            "that is merely hex-faceted.");
                    }

                    if (rebuild)
                    {
                        terrain.RebuildGeometry();
                    }
                }

                if (node is ManaMeshSceneNode { Mesh: ManaLineMesh lineMesh } lineNode)
                {
                    ImGui.SeparatorText("Line");
                    var thickness = lineMesh.Thickness;
                    if (ImGui.DragFloat("Thickness", ref thickness, 0.01f, 0.01f, 5.0f))
                    {
                        lineMesh.SetThickness(thickness);
                    }

                    // Color/emission are live material properties, not baked into the mesh's geometry
                    // (see ManaLineMesh.SetThickness's doc comment) -- mutated directly here, no
                    // rebuild needed.
                    var material = lineNode.MeshRenderer.Material;
                    var color = material.Albedo.Value;
                    if (ImGui.ColorEdit4("Color", ref color))
                    {
                        material.Albedo.Value = color;
                        material.Emission.Value = color;
                    }

                    var emissionStrength = (float)material.EmissionStrength;
                    if (ImGui.DragFloat("Emission Strength", ref emissionStrength, 0.05f, 0.0f, 10.0f))
                    {
                        material.EmissionStrength = emissionStrength;
                    }
                }
            };

            SceneInspectorPanelView.CustomComponentInspectorHook = component =>
            {
                if (component is not MeshRendererComponent { Mesh: PlanetPatchMesh patchMesh } renderer
                    || renderer.Owner.Parent is not PlanetNode owningPlanet
                    || !owningPlanet.TryGetPatchKey(renderer, out var patchKey))
                {
                    return;
                }

                ImGui.SeparatorText("Terrain Patch");
                var status = owningPlanet.GetPatchUpresStatus(patchKey);
                var detail = owningPlanet.GetPatchDetailInfo(patchKey);
                ImGui.Text($"Up-res status: {status}");
                ImGui.Text($"Mesh resolution: {patchMesh.Resolution} x {patchMesh.Resolution}");
                if (detail.HasValue)
                {
                    var patchDetail = detail.Value;
                    var sourceResolution = patchDetail.SourceResolution;
                    var sourceMetersPerPixel = patchDetail.SourceMetersPerPixel;
                    if (patchDetail.Zoom.HasValue)
                    {
                        ImGui.Text($"Source zoom: z{patchDetail.Zoom.Value}");

                        var tilePaths = owningPlanet.GetPatchTileCacheFilePaths(patchKey);
                        var cachedTilePaths = tilePaths.Where(File.Exists).ToList();
                        ImGui.Text($"Source tiles: {tilePaths.Count} ({cachedTilePaths.Count} cached)");
                        ImGui.BeginDisabled(cachedTilePaths.Count == 0);
                        if (ImGui.Button("Open Source Tile"))
                        {
                            FileExplorer.Reveal(cachedTilePaths[0]);
                        }
                        ImGui.EndDisabled();
                    }
                    if (sourceResolution.HasValue)
                    {
                        ImGui.Text($"Source resolution: {sourceResolution.Value} x {sourceResolution.Value}");
                    }
                    if (sourceMetersPerPixel.HasValue)
                    {
                        ImGui.Text($"Source pixel: {FormatMetersPerPixel(sourceMetersPerPixel.Value)}");
                    }
                    if (sourceResolution.HasValue && sourceMetersPerPixel.HasValue && sourceResolution.Value > patchMesh.Resolution)
                    {
                        var meshSpacing = EstimateMeshSpacingMeters(
                            patchMesh.Resolution,
                            sourceResolution.Value,
                            sourceMetersPerPixel.Value);
                        var coarserFactor = Math.Max(1.0, meshSpacing / sourceMetersPerPixel.Value);
                        ImGui.Text($"Mesh spacing: {FormatMetersPerPixel(meshSpacing)}");
                        ImGui.TextDisabled($"Mesh is still coarser than source data ({coarserFactor:0.#}x).");
                    }
                }

                // Deliberately gated on Done + an explicit button press -- each click increases
                // Terrarium source zoom by one level, splitting only as needed to avoid
                // undersampling that one new zoom level.
                if (status == PatchUpresStatus.Done)
                {
                    if (owningPlanet.IsSplitPending(patchKey))
                    {
                        ImGui.TextDisabled("Increasing source zoom...");
                    }
                    else if (owningPlanet.CanSplitPatch(patchKey))
                    {
                        if (ImGui.Button("Increase source zoom"))
                        {
                            owningPlanet.TrySplitPatch(patchKey);
                        }
                    }
                    else
                    {
                        var meshIsBelowSource = detail.HasValue
                            && detail.Value.SourceResolution.HasValue
                            && detail.Value.SourceResolution.Value > patchMesh.Resolution;
                        if (meshIsBelowSource)
                        {
                            ImGui.TextDisabled("Next source zoom cannot be represented without undersampling");
                        }
                        else
                        {
                            ImGui.TextDisabled("Mesh matches streamed source data");
                        }
                    }
                }
            };
        }

        private static void DrawTerrainCacheInspector()
        {
            PollTerrainCacheSizeRefresh();
            EnsureTerrainCacheSizeRefreshStarted();

            var snapshot = GetTerrainCacheSizeSnapshot();
            ImGui.SeparatorText("Terrain Cache");

            var cacheSizeText = snapshot.Bytes.HasValue
                ? FormatByteCount(snapshot.Bytes.Value)
                : snapshot.IsRefreshing ? "Calculating..." : "Unknown";
            ImGui.Text($"Cache size: {cacheSizeText}");
            if (snapshot.UpdatedAt.HasValue)
            {
                ImGui.TextDisabled($"Updated {snapshot.UpdatedAt.Value:HH:mm:ss}");
            }
            if (snapshot.IsRefreshing)
            {
                ImGui.TextDisabled("Refreshing...");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Error))
            {
                ImGui.TextDisabled($"Cache scan failed: {snapshot.Error}");
            }

            ImGui.TextWrapped(TerrariumTileFetcher.CacheRoot);

            ImGui.BeginDisabled(snapshot.IsRefreshing);
            if (ImGui.Button("Refresh"))
            {
                RequestTerrainCacheSizeRefresh();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Open Cache Folder"))
            {
                FileExplorer.OpenFolder(TerrariumTileFetcher.CacheRoot);
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(snapshot.IsRefreshing);
            if (ImGui.Button("Clear Cache"))
            {
                RequestTerrainCacheSizeRefresh(clearFirst: true, force: true);
            }
            ImGui.EndDisabled();
        }

        private static void EnsureTerrainCacheSizeRefreshStarted()
        {
            lock (TerrainCacheSizeLock)
            {
                if (!_terrainCacheSizeBytes.HasValue && _terrainCacheSizeRefreshTask == null)
                {
                    StartTerrainCacheSizeRefreshLocked(clearFirst: false);
                }
            }
        }

        private static void RequestTerrainCacheSizeRefresh(bool clearFirst = false, bool force = false)
        {
            lock (TerrainCacheSizeLock)
            {
                if (!force && _terrainCacheSizeRefreshTask is { IsCompleted: false })
                {
                    return;
                }

                StartTerrainCacheSizeRefreshLocked(clearFirst);
            }
        }

        private static void StartTerrainCacheSizeRefreshLocked(bool clearFirst)
        {
            _terrainCacheSizeError = null;
            var generation = ++_terrainCacheSizeRefreshGeneration;
            _terrainCacheSizeRefreshTask = Task.Run(() => RefreshTerrainCacheSize(generation, clearFirst));
        }

        private static TerrainCacheSizeRefreshResult RefreshTerrainCacheSize(int generation, bool clearFirst)
        {
            try
            {
                if (clearFirst)
                {
                    TerrariumTileFetcher.ClearCache();
                }

                return new TerrainCacheSizeRefreshResult(
                    generation,
                    TerrariumTileFetcher.GetCacheSizeBytes(),
                    Error: null);
            }
            catch (Exception ex)
            {
                return new TerrainCacheSizeRefreshResult(generation, Bytes: null, ex.Message);
            }
        }

        private static void PollTerrainCacheSizeRefresh()
        {
            Task<TerrainCacheSizeRefreshResult>? completedTask;
            lock (TerrainCacheSizeLock)
            {
                if (_terrainCacheSizeRefreshTask is not { IsCompleted: true } task)
                {
                    return;
                }

                completedTask = task;
                _terrainCacheSizeRefreshTask = null;
            }

            var result = completedTask.GetAwaiter().GetResult();
            lock (TerrainCacheSizeLock)
            {
                if (result.Generation != _terrainCacheSizeRefreshGeneration)
                {
                    return;
                }

                _terrainCacheSizeBytes = result.Bytes;
                _terrainCacheSizeUpdatedAt = DateTimeOffset.Now;
                _terrainCacheSizeError = result.Error;
            }
        }

        private static (long? Bytes, DateTimeOffset? UpdatedAt, string? Error, bool IsRefreshing) GetTerrainCacheSizeSnapshot()
        {
            lock (TerrainCacheSizeLock)
            {
                return (
                    _terrainCacheSizeBytes,
                    _terrainCacheSizeUpdatedAt,
                    _terrainCacheSizeError,
                    _terrainCacheSizeRefreshTask is { IsCompleted: false });
            }
        }

        private static string FormatByteCount(long bytes)
        {
            const double kibibyte = 1024.0;
            const double mebibyte = kibibyte * 1024.0;
            const double gibibyte = mebibyte * 1024.0;

            return bytes switch
            {
                >= (long)gibibyte => $"{bytes / gibibyte:N2} GiB",
                >= (long)mebibyte => $"{bytes / mebibyte:N2} MiB",
                >= (long)kibibyte => $"{bytes / kibibyte:N2} KiB",
                _ => $"{bytes} B",
            };
        }
#endif

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

        // Local convenience path to real-world KML airspace data (AirspaceConverter output, sourced
        // externally, ~10.5MB) — not a repo asset, degrades to a no-op via the File.Exists guard on
        // any machine without it. The KML geometry layer renders LineString/LinearRing/Polygon data
        // with ManaLineMesh, so line-like airspace boundaries draw directly instead of relying on
        // polygon triangulation.
        private const string KmlDemoFilePath = @"C:\Users\night\Code\ManaAirSpace\AirSpaceModels\UnitedStates.xml";

        // Where a loaded RegionNode's center sits in world space. A region is built at true scale (1 unit
        // == 1 meter), so even the smallest offered size is a 1km square -- four times the 256-unit voxel
        // grid on a side. Parked well clear to the west of everything else in this scene (grid at
        // 0..256, planet centered at 200,60,200 with radius 40) so the largest offered region still can't
        // reach any of it.
        private static readonly Vector3 RegionWorldOrigin = new(-3000.0f, 0.0f, 0.0f);

        // Camera arrival framing, both as multiples of the region's half-extent so every offered size is
        // framed the same way: back off far enough to take the whole box in, from an eye height where
        // relief still reads as relief rather than as a flat map.
        private const float RegionArrivalDistanceFactor = 3.0f;
        private const float RegionArrivalHeightFactor = 0.5f;

        // The camera's default 1000-unit far clip was sized for this scene's original contents (a
        // 256-unit voxel grid, a radius-40 planet). A region is true-scale, so even the smallest offered
        // one is a 1km box viewed from ~1.5km back -- entirely beyond that plane. Widened to fit whenever
        // a region loads, and restored from this saved value if a later region needs less.
        //
        // The near plane moves with it. Pushing far out alone would leave a near:far ratio of over a
        // million at the largest region size, which is well past what a 24-bit depth buffer can resolve
        // and shows up as z-fighting across flat terrain. Scaling both keeps the ratio fixed at a
        // comfortable ~10,000:1 regardless of region size.
        private const float RegionNearClipDivisor = 2000.0f;
        private float? _farClipBeforeRegion;
        private float? _nearClipBeforeRegion;

        // Consumes the "load this place as a true-scale region" request the search panel queues (see
        // VoxelSearchPanelView / RegionLoadRequest). Main thread only: this swaps scene-graph children.
        private void ProcessPendingRegionRequest()
        {
            var request = _viewModel.PendingRegionRequest;
            if (request == null)
            {
                return;
            }

            _viewModel.PendingRegionRequest = null;

            try
            {
                lock (Context.World.SceneLock)
                {
                    // One region at a time -- these are large true-scale meshes and there's no placement
                    // scheme for a second one, so a new request replaces the old rather than stacking up.
                    if (_region != null)
                    {
                        Context.World.DefaultScene.Children.Remove(_region);
                    }

                    _region = new RegionNode(
                        Context,
                        request.PlaceName,
                        new LatLong(request.Longitude, request.Latitude),
                        request.HalfExtentMeters,
                        zoom: request.Zoom,
                        resolution: null,
                        hexRadiusMeters: request.HexRadiusMeters,
                        stepStrength: request.StepStrength);
                    _region.Position = RegionWorldOrigin;
                    _region.UpdateTransform();
                    Context.World.DefaultScene.Children.Add(_region);
                }

                var camera = Context.World.MainCamera;
                if (camera != null)
                {
                    _farClipBeforeRegion ??= camera.FarClipPlane;
                    _nearClipBeforeRegion ??= camera.NearClipPlane;
                    camera.FarClipPlane = MathF.Max(
                        _farClipBeforeRegion.Value,
                        request.HalfExtentMeters * (RegionArrivalDistanceFactor + 2.0f));
                    camera.NearClipPlane = MathF.Max(
                        _nearClipBeforeRegion.Value,
                        request.HalfExtentMeters / RegionNearClipDivisor);

                    // Required: NearClipPlane/FarClipPlane are plain fields, and the projection matrix is
                    // only rebuilt on demand (constructor, window resize, or an Inspector edit -- see
                    // CameraComponentImGuiView, which calls this after every clip-plane drag). Without it
                    // the widened range never reaches the matrix and the region stays clipped away
                    // entirely, until some unrelated edit happens to rebuild the projection.
                    camera.UpdateProjection();

                    // Flown to immediately, while the node is still an empty placeholder -- the terrain
                    // pops in underneath the camera a moment later via ProcessPendingLoad. Better than
                    // waiting on the fetch and leaving the user staring at the planet with no feedback.
                    // Aimed above the center rather than at it so a horizontally-oriented camera ends up
                    // looking down at the terrain instead of standing inside it.
                    var arrivalTarget = RegionWorldOrigin + new Vector3(0.0f, request.HalfExtentMeters * RegionArrivalHeightFactor, 0.0f);
                    camera.FlyToWorldPosition(arrivalTarget, request.HalfExtentMeters * RegionArrivalDistanceFactor);
                }

                ManaConsole.WriteDiagnostic($"Loading region '{request.PlaceName}': {_region.DescribeStatus()}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to start region load: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to start region load: {ex}");
            }
        }

        // Parses the KML and builds its scene node WITHOUT attaching it to anything -- runs on a
        // background thread (see SyncKmlAirspaceVisibility), so it must not touch the live scene graph.
        // Returns null if the load failed; the caller turns that into "unavailable".
        private ManaSceneNode? BuildKmlAirspaceNode()
        {
            try
            {
                // Same "shrink ratio" reasoning as the planet, and the same radius, so the two read
                // as the same-scale world: real Earth radius/altitude scaled down to fit this scene.
                const float radius = 40.0f;
                var altitudeScale = radius / PlanetLoader.EarthRadiusMeters;
                var kml = new Kml(KmlDemoFilePath, radius, altitudeScale);

                // Still one draw call per rendered KML mesh with no culling/LOD/batching, so revisit
                // if airspace overlays ever become a frame bottleneck.
                var node = KmlSceneBuilder.BuildPlacemarks(Context, kml);
                node.Name = "US Airspace (KML)";
                return node;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load demo KML: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load demo KML: {ex}");
                return null;
            }
        }

        // Main thread only. Drives the whole lifecycle of the View > Show Airspace (KML) toggle: kicks off
        // the lazy first load, adopts the finished node, and attaches/detaches it on every later toggle.
        // Attachment is kept on the main thread (rather than done by the loading task itself) because the
        // node parents under _planet, whose children are concurrently rewritten by ProcessPendingSplits/
        // ProcessPendingMerges -- same reasoning as the comment on those calls in Update().
        private void SyncKmlAirspaceVisibility()
        {
            if (!_viewModel.KmlAirspaceAvailable)
            {
                return;
            }

            if (_kmlAirspaceLoadTask != null && _kmlAirspaceLoadTask.IsCompleted)
            {
                // BuildKmlAirspaceNode swallows its own failures into a null return, so a faulted task
                // here means something escaped it entirely -- treat both the same way.
                _kmlDemo = _kmlAirspaceLoadTask.IsCompletedSuccessfully ? _kmlAirspaceLoadTask.Result : null;
                _kmlAirspaceLoadTask = null;
                _viewModel.KmlAirspaceLoading = false;

                if (_kmlDemo == null)
                {
                    // Nothing to show and retrying would just fail again -- retire the menu item rather
                    // than leaving a toggle that silently does nothing.
                    _viewModel.KmlAirspaceAvailable = false;
                    _viewModel.ShowKmlAirspace = false;
                    return;
                }
            }

            if (!_viewModel.ShowKmlAirspace)
            {
                DetachKmlAirspace();
                return;
            }

            if (_kmlDemo == null)
            {
                if (_kmlAirspaceLoadTask == null)
                {
                    _viewModel.KmlAirspaceLoading = true;
                    _kmlAirspaceLoadTask = Task.Run(() =>
                    {
                        if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
                        {
                            System.Threading.Thread.CurrentThread.Name = "KML airspace load";
                        }

                        return BuildKmlAirspaceNode();
                    });
                }

                return;
            }

            AttachKmlAirspace();
        }

        private void AttachKmlAirspace()
        {
            if (_kmlDemo == null || _kmlAirspaceAttached)
            {
                return;
            }

            lock (Context.World.SceneLock)
            {
                // Parented under the planet (not the scene root) so it moves with it — its geometry
                // is already built on the same radius/origin convention as the planet (Kml.EarthRadius
                // == the planet's own radius above), so it needs to sit at the planet's local origin,
                // not an arbitrary world-space offset.
                if (_planet != null)
                {
                    _planet.AddChild(_kmlDemo);
                    _kmlDemo.Position = Vector3.Zero;
                }
                else
                {
                    _kmlDemo.Position = new Vector3(280.0f, 60.0f, 200.0f);
                    Context.World.DefaultScene.Children.Add(_kmlDemo);
                }

                _kmlDemo.UpdateTransform();
            }

            _kmlAirspaceAttached = true;
        }

        private void DetachKmlAirspace()
        {
            if (_kmlDemo == null || !_kmlAirspaceAttached)
            {
                return;
            }

            lock (Context.World.SceneLock)
            {
                // Tracked with an explicit bool rather than inferred from Parent: the scene-root path
                // below is a bare list Add, which (unlike ManaSceneNode.AddChild) never sets Parent.
                if (_kmlDemo.Parent != null)
                {
                    _kmlDemo.Parent.RemoveChild(_kmlDemo);
                }
                else
                {
                    Context.World.DefaultScene.Children.Remove(_kmlDemo);
                }
            }

            _kmlAirspaceAttached = false;
        }

        // Natural Earth's admin-0 country boundaries, 50m resolution (~100K raw points across 242
        // countries/territories before decimation -- see CountryBorderLines). A 10m version sits
        // alongside it in the same folder for a future zoom-dependent LOD pass; not wired up yet.
        // Not a repo asset (sourced externally, ~800KB), same treatment as KmlDemoFilePath -- degrades
        // to a no-op via the File.Exists guard on any machine without it.
        private const string CountryBordersZipPath = @"C:\Users\night\Code\ManaRenderer\Data\ne_50m_admin_0_countries.zip";
        private const string CountryBordersShpEntryName = "ne_50m_admin_0_countries.shp";
        private const string StateBordersZipPath = @"C:\Users\night\Code\ManaRenderer\Data\ne_50m_admin_1_states_provinces.zip";
        private const string StateBordersShpEntryName = "ne_50m_admin_1_states_provinces.shp";

        // Natural Earth physical polygons, same non-repo Data folder as the boundary shapefiles --
        // absent files just leave the mask empty and colouring stays elevation-only.
        private const string LakesZipPath = @"C:\Users\night\Code\ManaRenderer\Data\ne_10m_lakes.zip";
        private const string OceanZipPath = @"C:\Users\night\Code\ManaRenderer\Data\ne_10m_ocean.zip";

        private static void LoadWaterMask()
        {
            var mask = new GeoWaterMask();
            mask.AddLakes(GeoPolygonSet.TryLoadFromZip(LakesZipPath, "ne_10m_lakes.shp"));
            mask.Add(GeoPolygonSet.TryLoadFromZip(OceanZipPath, "ne_10m_ocean.shp"));

            if (!mask.HasAnyData)
            {
                ManaConsole.WriteDiagnostic("[Water] No Natural Earth water polygons found; colouring stays elevation-only.");
                return;
            }

            GeoWaterMask.Shared = mask;
            foreach (var set in mask.Sets)
            {
                ManaConsole.WriteDiagnostic($"[Water] Loaded {set.Name}: {set.RingCount:N0} rings.");
            }
        }

        private void AddCountryBordersDemo()
        {
            try
            {
                var node = BuildBoundaryLineNode(
                    CountryBordersZipPath,
                    CountryBordersShpEntryName,
                    "Country Borders",
                    thickness: 0.08f,
                    radiusOffset: 0.01f,
                    minSegmentDegrees: 0.15f,
                    color: new Vector4(1.0f, 0.75f, 0.3f, 1.0f),
                    emissionStrength: 2.0f);
                if (node == null)
                {
                    return;
                }

                _countryBorders = node;
                AddPlanetOverlayNode(_countryBorders);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load country borders: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load country borders: {ex}");
            }
        }

        private void AddStateBordersDemo()
        {
            try
            {
                var node = BuildBoundaryLineNode(
                    StateBordersZipPath,
                    StateBordersShpEntryName,
                    "State/Province Borders",
                    thickness: 0.035f,
                    radiusOffset: 0.014f,
                    minSegmentDegrees: 0.20f,
                    color: new Vector4(0.62f, 0.86f, 1.0f, 1.0f),
                    emissionStrength: 1.35f);
                if (node == null)
                {
                    return;
                }

                _stateBorders = node;
                AddPlanetOverlayNode(_stateBorders);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load state/province borders: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load state/province borders: {ex}");
            }
        }

        private ManaSceneNode? BuildBoundaryLineNode(
            string zipPath,
            string shpEntryName,
            string name,
            float thickness,
            float radiusOffset,
            float minSegmentDegrees,
            Vector4 color,
            float emissionStrength)
        {
            if (_planet == null || !File.Exists(zipPath))
            {
                return null;
            }

            var rings = ReadShapefileRingsFromZip(zipPath, shpEntryName);

            // Built on the bare sphere, then draped onto the terrain below -- radiusOffset becomes a
            // hover above the sampled surface rather than above the sea-level radius.
            var built = CountryBorderLines.BuildDrapeable(
                Context, rings, _planet.Radius, thickness, minSegmentDegrees, color, emissionStrength);

            var draped = new PlanetDrapedLines(_planet.PatchesPerFaceEdge, built.Mesh, built.LinePoints, built.LineLatLongs, radiusOffset);

            // This runs on the background load thread while RedrapePlanetOverlays iterates the list on
            // the main thread -- an unguarded List add against a concurrent read corrupts it.
            lock (_drapedOverlays)
            {
                _drapedOverlays.Add(draped);
            }
            // No initial drape: the baseline height decode snapshots every patch it touches, so the
            // first real terrain arrives through the normal changed-patch path.

            return new ManaMeshSceneNode(Context, built.Mesh)
            {
                Name = name,
                IncludeInEnvironmentCapture = false,
            };
        }

        private readonly List<PlanetDrapedLines> _drapedOverlays = new();

        // Points re-draped per overlay per frame. The work is a batched radius sample plus a vector
        // multiply per point, so this is a small slice of a frame -- measured at well under a millisecond
        // for the whole budget, against 25-75ms for the whole-mesh ribbon rebuild that follows a drain.
        // Generous on purpose for that reason: the points are nearly free and clearing them sooner is what
        // lets the expensive rebuild coalesce into one (see PlanetDrapedLines.RebuildSettleFrames).
        private const int DrapePointsPerFrame = 8192;

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

        // Every per-frame streaming step that has to happen on the main thread, in dependency order:
        // decide what to refine, then adopt whatever finished, then feed the overlays that read the
        // results. Each step is individually budgeted per frame, so calling this more than once per frame
        // would quietly multiply every one of those budgets -- hence the UpdateFrame guard.
        //
        // Three callers, at most one of which pumps on any given frame: Update always runs, DrawImGui
        // only when ImGui owns the UI, and UpdateEditorPerFrameState only when ManaUI does (see
        // Render's comment on why that third one exists at all).
        private long _lastStreamingPumpFrame = -1;

        private void PumpPlanetStreaming()
        {
            if (_lastStreamingPumpFrame == UpdateFrame)
            {
                return;
            }

            _lastStreamingPumpFrame = UpdateFrame;

            // Individually scoped rather than timed as one block: every one of these is a plausible hitch
            // source with a different fix, and the whole point is to find out which.
            using (ManaFrameProfiler.Measure("Streaming: Total"))
            {
                // Must run on the main thread -- finalizing a completed patch split touches the scene
                // graph (AddChild/RemoveChild), which can't safely happen from the background fetch task
                // that detects a split is ready (see PlanetNode.UpresPendingChildrenAsync's comment).
                using (ManaFrameProfiler.Measure("Streaming: AutoLod"))
                {
                    _planet?.UpdateAutoLod(Context.World.MainCamera);
                }

                using (ManaFrameProfiler.Measure("Streaming: NearOcean"))
                {
                    _planet?.UpdateNearOcean(Context.World.MainCamera);
                }

                using (ManaFrameProfiler.Measure("Streaming: DetailChase"))
                {
                    _planet?.UpdateDetailChase();
                }

                using (ManaFrameProfiler.Measure("Streaming: Split Finalize"))
                {
                    _planet?.ProcessPendingSplits();
                }

                using (ManaFrameProfiler.Measure("Streaming: Merge Finalize"))
                {
                    _planet?.ProcessPendingMerges();
                }

                // Same main-thread requirement, for the same reason -- adopts a finished region mesh.
                using (ManaFrameProfiler.Measure("Streaming: Region Adopt"))
                {
                    _region?.ProcessPendingLoad();
                }

                using (ManaFrameProfiler.Measure("Streaming: Overlay Drape"))
                {
                    RedrapePlanetOverlays();
                }
            }
        }

        // Main thread, once per frame from Update. Never blocks: patches the planet is too busy to
        // report on go back on its queue, and draping itself is spread over as many frames as it needs.
        private void RedrapePlanetOverlays()
        {
            if (_planet == null)
            {
                return;
            }

            PlanetDrapedLines[] overlays;
            lock (_drapedOverlays)
            {
                overlays = _drapedOverlays.ToArray();
            }

            // No overlays is the common case (nothing has loaded a border/airspace dataset yet), and the
            // snapshot queue still has to be emptied -- each entry pins a patch's CPU mesh, so leaving it
            // alone grows it for as long as terrain streams.
            if (overlays.Length == 0)
            {
                _planet.DiscardChangedPatches();
                return;
            }

            // Bounded on purpose: QueueChanged is paid in full per snapshot (it scans the whole root
            // patch's point bucket), before the per-point drape budget below gets any say.
            var changed = _planet.DrainChangedPatches();
            if (changed.Count > 0)
            {
                foreach (var overlay in overlays)
                {
                    overlay.QueueChanged(changed);
                }
            }

            // Overlays are told whether the producer still has snapshots queued: a rebuild that fires
            // between two halves of one streaming burst costs the same as one that covers the whole burst.
            var moreChangesInbound = _planet.PendingChangedPatchCount > 0;

            foreach (var overlay in overlays)
            {
                if (overlay.Tick(DrapePointsPerFrame, moreChangesInbound))
                {
                    ManaConsole.WriteDiagnostic(
                        $"[Drape] {overlay.Mesh.Name}: {overlay.LastMovedCount} point(s) moved, " +
                        $"max {overlay.LastMaxDisplacement:0.####} units.");
                    overlay.ResetStats();
                }
            }
        }

        private static List<IReadOnlyList<(double Longitude, double Latitude)>> ReadShapefileRingsFromZip(string zipPath, string shpEntryName)
        {
            var rings = new List<IReadOnlyList<(double Longitude, double Latitude)>>();
            using var archive = ZipFile.OpenRead(zipPath);
            var shpEntry = archive.GetEntry(shpEntryName)
                ?? throw new InvalidOperationException($"'{shpEntryName}' not found in {zipPath}.");
            using var shpStream = shpEntry.Open();
            foreach (var ring in ShapefilePolygonReader.ReadRings(shpStream))
            {
                rings.Add(ring);
            }

            return rings;
        }

        private void AddPlanetOverlayNode(ManaSceneNode node)
        {
            if (_planet == null)
            {
                return;
            }

            // Parented under the planet so overlays move/rotate with it, same reasoning as the KML
            // overlay above.
            _planet.AddChild(node);
            node.Position = Vector3.Zero;
            node.UpdateTransform();
        }

        private static string ResolveManaAssetPath(string relativePath)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(Environment.CurrentDirectory, "Mana", relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return relativePath;
        }

        private void Window_FileDrop(string[] files)
        {
            var mouseDropWorld = GetProjectedPlacementHit();
            var dropPosition = mouseDropWorld.Hit ? mouseDropWorld.Position : (Vector3?)null;
            _fileImportService.ImportDroppedFiles(files, dropPosition, Context.World, _viewModel);
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

            var hitHandler = new SingleHitHandler();
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
#if !MANA_BROWSER  // ImGui main menu bar; superseded by VoxelEditorMainMenuManaView (EnableManaUi=true means this never runs)
        protected override void DrawMainMenuBar()
        {
            _mainMenuView.Draw(_viewModel, this);
        }
#endif

        public override void Update(double deltaTime)
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return;
            }

            PumpMeshThumbnails();
            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();
            SyncWaterVisibility();
            SyncWireframeViewOption();
            SyncNormalsViewOption();
            SyncKmlAirspaceVisibility();
            ProcessPendingRegionRequest();

            PumpPlanetStreaming();
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

            if (_planet == null)
            {
                return;
            }

            world.RecordFrameStatText("Planet snapshot queue", $"{_planet.PendingChangedPatchCount} pending");

            // Surfaced next to the heap figure above because the two are related: this cache never evicts by
            // design (merges rely on a child's fetched tiles still being there), so it is the standing
            // explanation for a heap that grows the longer you fly, and pause times grow with the heap.
            var tileCount = _planet.HeightLibrary?.Tiles.Count ?? 0;
            world.RecordFrameStatText("Height tiles cached", $"{tileCount} (never evicted)");

            var status = _planet.GetStreamingStatus();
            Context.World.RecordFrameStatText("Planet patches resident", status.TotalLeaves.ToString());
            Context.World.RecordFrameStatText("Planet up-res in progress", status.PatchesUpresInProgress.ToString());
            Context.World.RecordFrameStatText("Planet splits in progress", status.PatchesSplitting.ToString());
            Context.World.RecordFrameStatText("Planet merges in progress", status.PatchesMerging.ToString());
            Context.World.RecordFrameStatText("Planet tile fetches active", $"{status.ActiveTileFetches}/{status.MaxConcurrentTileFetches}");
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

#if !MANA_BROWSER  // ImGui editor panels; superseded by ManaUi (EnableManaUi=true means this never runs)
        protected override void DrawImGui(double deltaTime)
        {
            PushWebGpuFrameStats();
            LogMemoryStatsPeriodically();
            ManaMemoryStats.Sample();

            if (_firstFrame)
            {
                ImGui.SetWindowFocus("Inspector");
                _firstFrame = false;
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
                            meshRenderer.OutlineEnabled = isActive;
                            meshRenderer.OutlineColor = activeOutlineColor;
                            meshRenderer.OutlineWidth = 4.0f;
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
                    meshRenderer.OutlineEnabled = false;
                }
            }

            if (targetOutlinedComponents.Count > 0)
            {
                var outlineColor = selectedComponent != null ? GetSelectionOutlineColor(selectedComponent) : GetSelectionOutlineColor(null);
                foreach (var meshRenderer in targetOutlinedComponents)
                {
                    meshRenderer.OutlineEnabled = true;
                    meshRenderer.OutlineColor = outlineColor;
                    meshRenderer.OutlineWidth = 4.0f;
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
            SyncKmlAirspaceVisibility();
            ProcessPendingRegionRequest();

            PumpPlanetStreaming();

            var mainViewport = ImGui.GetMainViewport();
            Vector2 sceneMin = mainViewport.WorkPos;
            Vector2 sceneMax = mainViewport.WorkPos + mainViewport.WorkSize;

            if (_manaDockId != 0)
            {
                ImGui.SetNextWindowDockID(_manaDockId, ImGuiCond.FirstUseEver);
            }

            ImGui.Begin(ManaWindowTitle);
            var manaWindowPos = ImGui.GetWindowPos();
            if (manaWindowPos.X > sceneMin.X + 200.0f && manaWindowPos.X < sceneMax.X)
            {
                sceneMax.X = manaWindowPos.X;
            }

            var MouseState = Context.Input.Mice.First();

            var mouseStart = new Vector3(MouseState.Position, 0.0f);
            var mouseEnd = new Vector3(MouseState.Position, 1.0f);
            
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var rayOrigin = ray.Origin;
            var rayDirection = ray.Direction;
            _viewModel.DebugRayOrigin = rayOrigin;
            _viewModel.DebugRayDirection = rayDirection;

            _panelsView.Draw(_viewModel, ref rayOrigin, ref rayDirection);
            ProcessMeshPickerPlacement(MouseState, sceneMin, sceneMax);

            ImGui.End();

            if (_toolDockId != 0)
            {
                ImGui.SetNextWindowDockID(_toolDockId, ImGuiCond.FirstUseEver);
            }

            _toolPaletteView.Draw(_viewModel);

            // The gizmo used to have to report back whether it was under the pointer, because an ImGui
            // window drawn over the scene is invisible to the scene. As part of the ManaUI tree it is
            // hit-tested like anything else, and UiWantsMouse already knows.
            UpdateVoxelPointerInteraction(UiWantsMouse);
            UpdateFrameSelectedShortcut();
            _pawnController?.UpdateDriveInput(UiWantsKeyboard);

            if (_viewModel.ShowImGuiDemoWindow)
            {
                if (_utilityDockId != 0)
                {
                    ImGui.SetNextWindowDockID(_utilityDockId, ImGuiCond.Always);
                }

                var showDemo = _viewModel.ShowImGuiDemoWindow;
                ImGui.ShowDemoWindow(ref showDemo);
                _viewModel.ShowImGuiDemoWindow = showDemo;
            }

            _aboutPanelView.Draw(_viewModel, Context, _webGpuContext);
            _searchPanelView.Draw(_viewModel, Context, _planet, Context.World.MainCamera);

            // The ManaUI search panel knows a longitude and a latitude and nothing else; the planet, camera
            // and scene it needs are here.
            if (PlaceSearchRequests.TakeFlyTo() is { } destination
                && _planet != null
                && Context.World.MainCamera != null)
            {
                _navigator.FlyTo(destination, Context, _planet, Context.World.MainCamera);
            }
        }
#endif

        private void ProcessMeshPickerPlacement(Silk.NET.Input.IMouse mouseState, Vector2 sceneMin, Vector2 sceneMax)
        {
            if (HandleMeshPickerEscapeCancel())
            {
#if !MANA_BROWSER
                _panelsView.ClearDraggedMeshAssetPath();
#endif
                MeshImportRequests.Clear();
            }

            // Both import panels feed this while ImGui is still on. They cannot both have a request pending
            // in the same frame -- only one of them can be under the pointer.
#if !MANA_BROWSER
            var pendingPath = _panelsView.ConsumePendingMeshSpawnPath() ?? MeshImportRequests.TakePlacement();
#else
            // _panelsView (VoxelEditorPanelsView, ImGui) doesn't exist here; MeshImportRequests
            // (fed by VoxelEditorPanelsManaView) is the only source on this target.
            var pendingPath = MeshImportRequests.TakePlacement();
#endif
            if (!string.IsNullOrWhiteSpace(pendingPath))
            {
                BeginMeshPickerPlacementPreview(pendingPath);
            }

            var leftMouseDown = mouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            var mousePosition = mouseState.Position;
            var isOverScene = mousePosition.X >= sceneMin.X &&
                              mousePosition.X <= sceneMax.X &&
                              mousePosition.Y >= sceneMin.Y &&
                              mousePosition.Y <= sceneMax.Y;
#if !MANA_BROWSER
            var draggedPath = _panelsView.CurrentDraggedMeshAssetPath ?? MeshImportRequests.Dragging;
#else
            var draggedPath = MeshImportRequests.Dragging;
#endif

            // The scene rectangle is the whole ImGui viewport, which the ManaUI columns sit on top of -- so
            // "over the scene" is true above a panel as well. The picker reports what is actually under the
            // cursor, and only that says whether there is scene there to drop onto.
#if !MANA_BROWSER
            var overPanel = _panelsView.CurrentDraggedMeshAssetPath == null && MeshImportRequests.PointerOverUi;
#else
            var overPanel = MeshImportRequests.PointerOverUi;
#endif
            var canDrop = isOverScene && !overPanel;

            if (!string.IsNullOrWhiteSpace(draggedPath) && canDrop)
            {
                BeginMeshPickerPlacementPreview(draggedPath);
            }

            if (_meshPickerPreviewNode != null && canDrop)
            {
                UpdateMeshPickerPlacementPreviewTransform();
            }

            if (!leftMouseDown && _meshPickerDragWasDown)
            {
                if (!string.IsNullOrWhiteSpace(draggedPath))
                {
                    if (canDrop && string.Equals(draggedPath, _meshPickerPreviewAssetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        CommitMeshPickerPlacementPreview();
                    }
                    else
                    {
                        CancelMeshPickerPlacementPreview();
                    }

#if !MANA_BROWSER
                    _panelsView.ClearDraggedMeshAssetPath();
#endif
                    MeshImportRequests.TakeDrag();
                }

                // Polled real button state rather than the row's own PointerReleased, which sometimes never
                // fires (window loses focus mid-drag, the release lands outside the window) -- leaving that
                // row's ghost floating in the viewport and its pointer capture stuck until something else
                // happened to drag over the same row again. This runs every up-edge regardless of whether a
                // mailbox drag was pending, since the two can desync.
                MeshAssetRow.ForceEndActiveDrag();
            }

            var placeClick = leftMouseDown && !_meshPickerPlaceClickWasDown;
            _meshPickerPlaceClickWasDown = leftMouseDown;
            if (placeClick &&
                _meshPickerPreviewNode != null &&
                string.IsNullOrWhiteSpace(draggedPath) &&
                isOverScene &&
                !UiWantsMouse)
            {
                CommitMeshPickerPlacementPreview();
            }

            _meshPickerDragWasDown = leftMouseDown;
        }

        private bool HandleMeshPickerEscapeCancel()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                _meshPickerEscapeWasDown = false;
                return false;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape);
            var pressed = keyDown && !_meshPickerEscapeWasDown;
            _meshPickerEscapeWasDown = keyDown;

            if (!pressed)
            {
                return false;
            }

            CancelMeshPickerPlacementPreview();
            return true;
        }

        private void BeginMeshPickerPlacementPreview(string assetReference)
        {
            if (_meshPickerPreviewNode != null &&
                string.Equals(_meshPickerPreviewAssetPath, assetReference, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CancelMeshPickerPlacementPreview();

            var world = Context.World;
            var sceneStart = world.Scenes.Count;
            var meshStart = world.Meshes.Count;
            var materialStart = world.Materials.Count;

            try
            {
                _meshPickerPreviewSelectionBefore = EditorSelectionSnapshot.Capture(world.Selection);
                var previewNode = LoadMeshPickerAsset(assetReference);
                _meshPickerPreviewAssetPath = assetReference;
                _meshPickerPreviewNode = previewNode;
                _meshPickerPreviewSceneStartIndex = sceneStart;
                _meshPickerPreviewMeshStartIndex = meshStart;
                _meshPickerPreviewMaterialStartIndex = materialStart;
                UpdateMeshPickerPlacementPreviewTransform();
            }
            catch (Exception ex)
            {
                RemoveMeshPickerPreviewWorldData(sceneStart, meshStart, materialStart);
                _meshPickerPreviewAssetPath = null;
                _meshPickerPreviewNode = null;
                _meshPickerPreviewSceneStartIndex = -1;
                _meshPickerPreviewMeshStartIndex = -1;
                _meshPickerPreviewMaterialStartIndex = -1;
                _meshPickerPreviewSelectionBefore = null;
                Trace.TraceWarning($"Failed to start mesh placement preview for '{assetReference}': {ex}");
                ManaConsole.WriteDiagnostic($"Failed to start mesh placement preview for '{assetReference}': {ex}");
            }
        }

        private void UpdateMeshPickerPlacementPreviewTransform()
        {
            if (_meshPickerPreviewNode == null)
            {
                return;
            }

            var mouseHit = GetProjectedPlacementHit();
            if (!mouseHit.Hit)
            {
                return;
            }

            _meshPickerPreviewNode.Position = mouseHit.Position + _viewModel.ImportPositionOffset;
            _meshPickerPreviewNode.Rotation = new Vector3(
                _viewModel.ImportRotationDegrees.X.ToRadians(),
                _viewModel.ImportRotationDegrees.Y.ToRadians(),
                _viewModel.ImportRotationDegrees.Z.ToRadians());
            _meshPickerPreviewNode.Scale = new Vector3(
                MathF.Max(0.01f, _viewModel.ImportScale.X),
                MathF.Max(0.01f, _viewModel.ImportScale.Y),
                MathF.Max(0.01f, _viewModel.ImportScale.Z));
            _meshPickerPreviewNode.UpdateTransform();
        }

        private void CommitMeshPickerPlacementPreview()
        {
            var world = Context.World;
            var placedNode = _meshPickerPreviewNode;
            var assetPath = _meshPickerPreviewAssetPath;
            if (placedNode != null &&
                _meshPickerPreviewSceneStartIndex >= 0 &&
                _meshPickerPreviewMeshStartIndex >= 0 &&
                _meshPickerPreviewMaterialStartIndex >= 0)
            {
                var insertion = EditorWorldInsertion.Capture(
                    world,
                    _meshPickerPreviewSceneStartIndex,
                    _meshPickerPreviewMeshStartIndex,
                    _meshPickerPreviewMaterialStartIndex);
                var selectionBefore = _meshPickerPreviewSelectionBefore
                    ?? EditorSelectionSnapshot.Capture(world.Selection);

                EditorSelectionSnapshot.SelectNode(world.Selection, placedNode, sceneGraphChanged: true);
                if (!insertion.IsEmpty)
                {
                    var displayName = !string.IsNullOrWhiteSpace(assetPath)
                        ? MeshAssetLibrary.DisplayName(assetPath)
                        : placedNode.Name ?? "Actor";
                    _viewModel.History.RecordWorldInsertion(
                        insertion,
                        placedNode,
                        selectionBefore,
                        $"Place {displayName}",
                        world.Selection);
                }
            }

            _meshPickerPreviewAssetPath = null;
            _meshPickerPreviewNode = null;
            _meshPickerPreviewSceneStartIndex = -1;
            _meshPickerPreviewMeshStartIndex = -1;
            _meshPickerPreviewMaterialStartIndex = -1;
            _meshPickerPreviewSelectionBefore = null;
        }

        private void CancelMeshPickerPlacementPreview()
        {
            if (_meshPickerPreviewSceneStartIndex >= 0 ||
                _meshPickerPreviewMeshStartIndex >= 0 ||
                _meshPickerPreviewMaterialStartIndex >= 0)
            {
                RemoveMeshPickerPreviewWorldData(
                    _meshPickerPreviewSceneStartIndex,
                    _meshPickerPreviewMeshStartIndex,
                    _meshPickerPreviewMaterialStartIndex);
            }

            _meshPickerPreviewAssetPath = null;
            _meshPickerPreviewNode = null;
            _meshPickerPreviewSceneStartIndex = -1;
            _meshPickerPreviewMeshStartIndex = -1;
            _meshPickerPreviewMaterialStartIndex = -1;
            _meshPickerPreviewSelectionBefore = null;
        }

        private ManaSceneNode LoadMeshPickerAsset(string assetReference)
        {
            if (TryDecodeZipAssetReference(assetReference, out var zipPath, out var entryPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryPath);
                if (entry == null)
                {
                    throw new FileNotFoundException($"Entry '{entryPath}' not found in zip '{zipPath}'.", zipPath);
                }

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);
                var sidecarResolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entryPath);
                return Context.World.LoadFromGltfMemory(memory.ToArray(), entryPath, sidecarResolver);
            }

            return Context.World.LoadFromGltf(assetReference);
        }

        private void RemoveMeshPickerPreviewWorldData(int sceneStart, int meshStart, int materialStart)
        {
            var world = Context.World;
            lock (world.SceneLock)
            {
                if (sceneStart >= 0 && sceneStart < world.Scenes.Count)
                {
                    world.Scenes.RemoveRange(sceneStart, world.Scenes.Count - sceneStart);
                }

                if (meshStart >= 0 && meshStart < world.Meshes.Count)
                {
                    world.Meshes.RemoveRange(meshStart, world.Meshes.Count - meshStart);
                }

                if (materialStart >= 0 && materialStart < world.Materials.Count)
                {
                    world.Materials.RemoveRange(materialStart, world.Materials.Count - materialStart);
                }
            }
        }

        /// <summary>
        /// Delegated rather than implemented here. Two copies of this encoding is exactly what broke mesh
        /// import: the picker was rewritten with a different separator, this decoder rejected everything it
        /// produced, and every archived mesh was passed on as a filename and failed to open.
        /// </summary>
        private static bool TryDecodeZipAssetReference(string assetReference, out string zipPath, out string entryPath) =>
            MeshAssetLibrary.TryDecodeZip(assetReference, out zipPath, out entryPath);

        private void ImportMeshAssetFromPicker(string filePath)
        {
            try
            {
                var mouseHit = GetProjectedPlacementHit();
                var dropPosition = mouseHit.Hit ? mouseHit.Position : (Vector3?)null;
                _fileImportService.ImportFile(filePath, dropPosition, Context.World, _viewModel);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to import mesh asset '{filePath}': {ex}");
                ManaConsole.WriteDiagnostic($"Failed to import mesh asset '{filePath}': {ex}");
            }
        }

        private void UpdateVoxelPointerInteraction(bool inputCaptured)
        {
            var MouseState = Context.Input.Mice.FirstOrDefault();
            if (MouseState == null)
            {
                return;
            }

            if (_viewModel.IsSelectToolActive)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                _viewModel.ClearHoveredVoxel();
                var gizmoCaptured = _transformGizmo?.Update(
                    Context.World.Selection.SelectedNode,
                    _viewModel.TransformGizmoMode,
                    _viewModel.ShowTransformGizmo,
                    inputCaptured,
                    MouseState) == true;
                UpdateSelectionPointerInteraction(inputCaptured || gizmoCaptured, MouseState);
                return;
            }

            _transformGizmo?.Hide();

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

                Cursor.Position = pos;
                Cursor.UpdateTransform();
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
            Cursor.Position = occupiedPos;
            Cursor.UpdateTransform();
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

        // "F to frame selected" -- edge-detected the same way UpdateSelectionPointerInteraction debounces
        // a mouse click (a bool "was it down last frame" field), so holding F doesn't re-fire every frame.
        // Gated on WantCaptureKeyboard so it doesn't fire while typing in an ImGui text field (e.g.
        // renaming a node) that happens to contain the letter F.
        private void UpdateFrameSelectedShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.F);
            var pressed = keyDown && !_frameSelectedKeyWasDown;
            _frameSelectedKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            var selection = Context.World.Selection;
            Context.World.MainCamera.TryFrameSelection(selection.SelectedComponent as MeshRendererComponent, selection.SelectedNode);
        }

        // Escape backs all the way out: it already releases a possessed pawn (ManaPawnController) and
        // cancels an in-progress mesh-picker placement (HandleMeshPickerEscapeCancel) as two independent
        // edge-detected consumers of the same key -- this is the third, clearing whatever is selected and
        // handing control back to the Select tool rather than leaving the last tool active over nothing.
        private void UpdateEscapeSelectionShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape);
            var pressed = keyDown && !_escapeSelectionKeyWasDown;
            _escapeSelectionKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            Context.World.Selection.SelectedScene = null;
            Context.World.Selection.SelectedNode = null;
            Context.World.Selection.SelectedComponent = null;
            _viewModel.IsSelectToolActive = true;
        }

        private void UpdateSelectionPointerInteraction(bool inputCaptured, IMouse mouseState)
        {
            bool leftMouseDown = !inputCaptured && mouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            bool clicked = leftMouseDown && !_selectPointerWasDown;
            _selectPointerWasDown = leftMouseDown;

            if (!clicked)
            {
                return;
            }

            var mouseStart = new Vector3(mouseState.Position, 0.0f);
            var mouseEnd = new Vector3(mouseState.Position, 1.0f);
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var pickResult = ManaScenePicker.Pick(Context.World.Scenes, ray.Origin, ray.Direction, _pickingExclusions);

            // Pawn clicks get first refusal: clicking a character possesses it (and selects the pawn
            // root rather than the body-mesh child the ray actually hit), and a follow-up click
            // elsewhere is a walk order that must not clear the selection out from under it.
            var pawnOutcome = _pawnController?.HandleClick(pickResult?.Node, ray.Origin, ray.Direction)
                ?? ManaPawnController.ClickOutcome.Ignored;

            if (pawnOutcome == ManaPawnController.ClickOutcome.Ordered)
            {
                return;
            }

            if (pawnOutcome == ManaPawnController.ClickOutcome.Possessed)
            {
                Context.World.Selection.SelectedScene = null;
                Context.World.Selection.SelectedNode = _pawnController?.PossessedPawn;
                Context.World.Selection.SelectedComponent = null;
                return;
            }

            var selectedNode = ResolveEditorSelectableNode(pickResult?.Node, Context.World.Selection.SelectedNode);
            Context.World.Selection.SelectedScene = null;
            Context.World.Selection.SelectedNode = selectedNode;
            Context.World.Selection.SelectedComponent = ReferenceEquals(selectedNode, pickResult?.Node)
                ? pickResult?.Renderer
                : null;

            var now = Environment.TickCount64;
            object? clickedTarget = (object?)pickResult?.Renderer ?? pickResult?.Node;
            var isDoubleClick = clickedTarget != null
                && ReferenceEquals(clickedTarget, _lastSelectClickedTarget)
                && now - _lastSelectClickTimeMs <= DoubleClickThresholdMs;
            _lastSelectClickTimeMs = now;
            _lastSelectClickedTarget = clickedTarget;

            // Double-click is the deliberate-but-quick alternative to the Inspector's "Increase source
            // zoom" button -- still only ever an EXPLICIT gesture, never triggered by plain
            // reselection. No-ops via TrySplitPatch's own guard if the patch isn't currently Done or
            // can't increase source zoom without undersampling.
            if (isDoubleClick && _planet != null && _planet.TryGetPatchKey(pickResult?.Renderer, out var patchKey))
            {
                _planet.TrySplitPatch(patchKey);
            }
        }

        /// <summary>
        /// A click always resolves up to the whole authored object first -- so a FlightHelmet import
        /// (one <see cref="ManaLevelSource"/> on the root, none on its Lens/Metal/etc. child meshes)
        /// selects as one thing no matter which part the ray actually hit -- unless that same object is
        /// already the selection, in which case a further click drills straight to the specific part
        /// under the cursor instead of just reselecting the whole thing again. Clicking a different
        /// object resets back to "select the whole thing" for it; clicking back into an object you're
        /// already drilled into (any of its parts already selected, not just the root) keeps drilling
        /// directly between parts rather than bouncing back up each time.
        /// </summary>
        // Public: SceneSelectionDrillDownTests exercises this directly against a small synthetic
        // hierarchy, rather than only through a live click, since it's a pure function of the pick and
        // the current selection with no other dependency.
        public static ManaSceneNode? ResolveEditorSelectableNode(ManaSceneNode? pickedNode, ManaSceneNode? currentSelection)
        {
            if (pickedNode == null)
            {
                return null;
            }

            var authoredRoot = FindAuthoredRoot(pickedNode) ?? pickedNode;

            if (currentSelection != null && ReferenceEquals(FindAuthoredRoot(currentSelection) ?? currentSelection, authoredRoot))
            {
                return pickedNode;
            }

            return authoredRoot;
        }

        private static ManaSceneNode? FindAuthoredRoot(ManaSceneNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current.LevelSource != null)
                {
                    return current;
                }
            }

            return null;
        }

        private void SyncSelectionColliderDebug(ManaSceneNode? selectedNode)
        {
            var collisions = new List<ManaCollisionComponent>();
            CollectSelectedBoxCollisions(selectedNode, collisions);
            CollectPinnedBoxCollisions(collisions);

            if (collisions.Count == 0)
            {
                ClearSelectionColliderDebug();
                return;
            }

            if (NeedsColliderDebugRebuild(selectedNode, collisions))
            {
                RebuildSelectionColliderDebug(selectedNode, collisions);
            }

            if (!UpdateColliderDebugTransforms())
            {
                ClearSelectionColliderDebug();
            }
        }

        private void CollectSelectedBoxCollisions(ManaSceneNode? selectedNode, List<ManaCollisionComponent> output)
        {
            if (selectedNode == null)
            {
                return;
            }

            var before = output.Count;
            CollectNodeBoxCollisions(selectedNode, output, pinnedOnly: false);
            if (output.Count > before)
            {
                return;
            }

            for (var current = selectedNode.Parent; current != null; current = current.Parent)
            {
                before = output.Count;
                CollectOwnBoxCollisions(current, output, pinnedOnly: false);
                if (output.Count > before)
                {
                    return;
                }
            }
        }

        private void CollectPinnedBoxCollisions(List<ManaCollisionComponent> output)
        {
            lock (Context.World.SceneLock)
            {
                foreach (var scene in Context.World.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectNodeBoxCollisions(child, output, pinnedOnly: true);
                    }
                }
            }
        }

        private void CollectNodeBoxCollisions(
            ManaSceneNode node,
            List<ManaCollisionComponent> output,
            bool pinnedOnly)
        {
            if (ReferenceEquals(node, _selectionColliderDebugRoot))
            {
                return;
            }

            CollectOwnBoxCollisions(node, output, pinnedOnly);

            foreach (var child in node.Children)
            {
                CollectNodeBoxCollisions(child, output, pinnedOnly);
            }
        }

        private void CollectOwnBoxCollisions(
            ManaSceneNode node,
            List<ManaCollisionComponent> output,
            bool pinnedOnly)
        {
            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                if (pinnedOnly && !collision.ShowDebug)
                {
                    continue;
                }

                if (collision.Shape is not (Box or ConvexHull)
                    || !Context.Physics.Simulation.Bodies.BodyExists(collision.Body))
                {
                    continue;
                }

                if (!output.Any(existing => ReferenceEquals(existing, collision)))
                {
                    output.Add(collision);
                }
            }
        }

        private bool NeedsColliderDebugRebuild(
            ManaSceneNode? selectedNode,
            IReadOnlyList<ManaCollisionComponent> collisions)
        {
            if (!ReferenceEquals(_selectionColliderDebugTarget, selectedNode)
                || _selectionColliderDebugRoot == null
                || _selectionColliderDebugEntries.Count != collisions.Count)
            {
                return true;
            }

            for (var i = 0; i < collisions.Count; i++)
            {
                if (!ReferenceEquals(_selectionColliderDebugEntries[i].Collision, collisions[i]))
                {
                    return true;
                }

                if (!_selectionColliderDebugEntries[i].Size.Equals(CollisionSize(collisions[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildSelectionColliderDebug(
            ManaSceneNode? selectedNode,
            IReadOnlyList<ManaCollisionComponent> collisions)
        {
            lock (Context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();

                var root = new ManaSceneNode(Context, Matrix4x4.Identity)
                {
                    Name = "Selection Collider Debug (Transient)",
                    IncludeInEnvironmentCapture = false,
                    IsEditorTransient = true,
                    LevelSource = null
                };

                Context.World.DefaultScene.Children.Add(root);
                _pickingExclusions.Add(root);

                _selectionColliderDebugRoot = root;
                _selectionColliderDebugTarget = selectedNode;

                foreach (var collision in collisions)
                {
                    var size = CollisionSize(collision);
                    var debugMesh = CreateColliderDebugMesh(collision, size);

                    var node = new ManaSceneNode(Context, Matrix4x4.Identity)
                    {
                        Name = $"Collider {CollisionOwnerName(collision.Owner)}",
                        IncludeInEnvironmentCapture = false,
                        IsEditorTransient = true,
                        LevelSource = null
                    };

                    node.Components.Add(new MeshRendererComponent(node, debugMesh)
                    {
                        DrawAfterScene = true
                    });

                    root.AddChild(node);
                    _pickingExclusions.Add(node);
                    _selectionColliderDebugEntries.Add(new ColliderDebugEntry(collision, node, size));
                }
            }
        }

        private bool UpdateColliderDebugTransforms()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                if (!Context.Physics.Simulation.Bodies.BodyExists(entry.Collision.Body))
                {
                    return false;
                }

                var pose = Context.Physics.Simulation.Bodies[entry.Collision.Body].Pose;
                var transform = Matrix4x4.CreateFromQuaternion(pose.Orientation)
                    * Matrix4x4.CreateTranslation(pose.Position);
                entry.Node.SetLocalTransform(transform);
                entry.Node.WorldTransform = transform;
            }

            return true;
        }

        private void ClearSelectionColliderDebug()
        {
            lock (Context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();
            }
        }

        private void ClearSelectionColliderDebugNoLock()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                _pickingExclusions.Remove(entry.Node);
            }

            if (_selectionColliderDebugRoot != null)
            {
                _pickingExclusions.Remove(_selectionColliderDebugRoot);
                _selectionColliderDebugRoot.Parent?.RemoveChild(_selectionColliderDebugRoot);

                foreach (var scene in Context.World.Scenes)
                {
                    scene.Children.Remove(_selectionColliderDebugRoot);
                }
            }

            _selectionColliderDebugRoot = null;
            _selectionColliderDebugTarget = null;
            _selectionColliderDebugEntries.Clear();
        }

        private static Vector3 CollisionSize(ManaCollisionComponent collision)
        {
            if (collision.Shape is Box box)
            {
                return new Vector3(box.Width, box.Height, box.Length);
            }

            if (collision.Shape is ConvexHull hull)
            {
                hull.ComputeBounds(Quaternion.Identity, out var min, out var max);
                return max - min;
            }

            return Vector3.Zero;
        }

        private ManaMesh CreateColliderDebugMesh(ManaCollisionComponent collision, Vector3 size)
        {
            if (collision.Shape is not ConvexHull hull)
            {
                return new ManaWireframeCage(
                    Context,
                    ColliderDebugColor,
                    Vector3.Max(size * 0.5f, new Vector3(0.025f)),
                    thickness: 0.035f);
            }

            var faces = new List<ManaLinePolyline>(hull.FaceToVertexIndicesStart.Length);
            for (var faceIndex = 0; faceIndex < hull.FaceToVertexIndicesStart.Length; faceIndex++)
            {
                hull.GetVertexIndicesForFace(faceIndex, out var vertexIndices);
                var points = new List<Vector3>(vertexIndices.Length);
                for (var vertexIndex = 0; vertexIndex < vertexIndices.Length; vertexIndex++)
                {
                    hull.GetPoint(vertexIndices[vertexIndex], out var point);
                    points.Add(point);
                }

                if (points.Count >= 3)
                {
                    faces.Add(new ManaLinePolyline(points, closed: true));
                }
            }

            return new ManaLineMesh(
                Context,
                faces,
                thickness: 0.035f,
                ColliderDebugColor,
                emissionStrength: 1.5f);
        }

        private static string CollisionOwnerName(ManaSceneNode owner)
        {
            return string.IsNullOrWhiteSpace(owner.Name) ? owner.GetType().Name : owner.Name;
        }

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

                component.OutlineEnabled = false;
            }

            foreach (var component in currentComponents)
            {
                if (component == selectedComponent || component.Owner == activeNode)
                {
                    continue;
                }

                component.OutlineEnabled = true;
                component.OutlineColor = PatchUpresInProgressOutlineColor;
                component.OutlineWidth = 4.0f;
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

            var mouse = Context.Input.Mice.FirstOrDefault();
            return _toolController.IsDrawing || mouse?.IsButtonPressed(Silk.NET.Input.MouseButton.Left) == true;
        }

#if !MANA_BROWSER  // ImGui docking layout; only caller (BaseGameMode.ImGuiDocking.cs) is itself browser-excluded
        protected override void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
            ImGuiDockNative.DockBuilderSetNodeSize(dockspaceId, dockspaceSize);

            var leftColumnRatio = dockspaceSize.X > 0.0f
                ? Math.Clamp(400.0f / dockspaceSize.X, 0.20f, 0.30f)
                : 0.24f;
            var rightColumnRatio = dockspaceSize.X > 0.0f
                ? Math.Clamp(460.0f / dockspaceSize.X, 0.28f, 0.36f)
                : 0.30f;
            var outlinerHeightRatio = dockspaceSize.Y > 0.0f
                ? Math.Clamp((dockspaceSize.Y - 280.0f) / dockspaceSize.Y, 0.45f, 0.68f)
                : 0.58f;

            uint leftColumnDockId;
            uint leftRemainderDockId;
            _ = ImGuiDockNative.DockBuilderSplitNode(
                dockspaceId,
                ImGuiDir.Left,
                leftColumnRatio,
                out leftColumnDockId,
                out leftRemainderDockId);

            _ = ImGuiDockNative.DockBuilderSplitNode(
                leftColumnDockId,
                ImGuiDir.Down,
                outlinerHeightRatio,
                out _outlinerDockId,
                out _toolDockId);

            uint centerDockId;
            uint rightDockId;
            _ = ImGuiDockNative.DockBuilderSplitNode(
                leftRemainderDockId,
                ImGuiDir.Right,
                rightColumnRatio,
                out rightDockId,
                out centerDockId);

            _ = ImGuiDockNative.DockBuilderSplitNode(
                rightDockId,
                ImGuiDir.Down,
                0.45f,
                out _utilityDockId,
                out _manaDockId);

            ImGuiDockNative.DockBuilderDockWindow(VoxelToolPaletteView.WindowTitle, _toolDockId);
            ImGuiDockNative.DockBuilderDockWindow("Outliner", _outlinerDockId);
            ImGuiDockNative.DockBuilderDockWindow(ManaWindowTitle, _manaDockId);
            ImGuiDockNative.DockBuilderDockWindow("Inspector", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Camera", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("World Perf", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Irradiance", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Dear ImGui Demo", _utilityDockId);
            BaseGameMode.UtilityDockId = _utilityDockId;
            BaseGameMode.OutlinerDockId = _outlinerDockId;
            ImGuiDockNative.DockBuilderFinish(dockspaceId);
        }
#endif

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

            UpdatePlayModeShortcuts();
            UpdateUndoRedoShortcuts();
            _trackDebugVisualization?.SetVisibility(
                _viewModel.ShowTrackBounds,
                _viewModel.ShowTrackPivots,
                _viewModel.ShowTrackSnapPoints);

            // While playing, everything below this line is the editor acting on a pointer that now belongs
            // to the game: picking, voxel placement, mesh drag-and-drop, selection outlines. Only the things
            // the world needs regardless -- streaming, and the character's own input -- carry on.
            if (_playMode is { IsPlaying: true })
            {
                _transformGizmo?.Hide();
                ClearSelectionColliderDebug();
                PumpPlanetStreaming();
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
                            meshRenderer.OutlineEnabled = isActive;
                            meshRenderer.OutlineColor = activeOutlineColor;
                            meshRenderer.OutlineWidth = 4.0f;
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
                    meshRenderer.OutlineEnabled = false;
                }
            }

            if (targetOutlinedComponents.Count > 0)
            {
                var outlineColor = selectedComponent != null ? GetSelectionOutlineColor(selectedComponent) : GetSelectionOutlineColor(null);
                foreach (var meshRenderer in targetOutlinedComponents)
                {
                    meshRenderer.OutlineEnabled = true;
                    meshRenderer.OutlineColor = outlineColor;
                    meshRenderer.OutlineWidth = 4.0f;
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
            SyncKmlAirspaceVisibility();
            ProcessPendingRegionRequest();

            PumpPlanetStreaming();

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
