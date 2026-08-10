using ImGuiNET;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
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
    public class VoxelEditorGameMode : BaseGameMode
    {
        private const string ManaWindowTitle = "Mana";
        private uint _toolDockId;
        private uint _outlinerDockId;
        private uint _manaDockId;
        private uint _utilityDockId;
        private readonly VoxelEditorViewModel _viewModel = new();
        private readonly VoxelEditorMainMenuView _mainMenuView = new();
        private readonly VoxelToolPaletteView _toolPaletteView = new();
        private readonly VoxelEditorPanelsView _panelsView = new();
        private readonly VoxelEditorAboutPanelView _aboutPanelView = new();
        private readonly VoxelSearchPanelView _searchPanelView = new();
        private readonly ViewportCameraGizmoView _cameraGizmoView = new();
        private readonly VoxelPlacementDepthOverlayView _placementDepthOverlayView = new();
        private readonly VoxelToolController _toolController = new();
        private readonly VoxelFileImportService _fileImportService = new();
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
        private bool _selectPointerWasDown;
        private bool _frameSelectedKeyWasDown;
        private HashSet<MeshRendererComponent> _lastOutlinedSelectionComponents = new();
        private HashSet<MeshRendererComponent> _lastStreamingHighlightedComponents = new();
        private readonly HashSet<ManaSceneNode> _pickingExclusions = new();
        private static readonly Vector4 SelectionOutlineColor = new(0.30f, 0.72f, 1.0f, 1.0f);
        private static readonly Vector4 PatchUpresInProgressOutlineColor = new(1.0f, 0.55f, 0.0f, 1.0f);
        private static readonly Vector4 PatchUpresDoneOutlineColor = new(0.25f, 0.85f, 0.25f, 1.0f);
        private static readonly Vector4 PatchUpresFailedOutlineColor = new(0.9f, 0.2f, 0.2f, 1.0f);

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

        public override bool DeferGpuUploadsOnStartup => true;

        // v3: Inspector now drawn first each frame so it lands as the first utility tab
        // (dock order follows Begin() submission order on a fresh layout, not DockBuilder call order).
        protected override int DockLayoutVersion => 3;

        public VoxelEditorGameMode(ManaGraphicsBackendKind graphicsBackend = ManaGraphicsBackendKind.OpenGL) : base(graphicsBackend)
        {

        }

        public void RunWebGpu(string? requestedBackend)
        {
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
                    webGpuContext.EnvironmentLoading.PropertyChanged += (_, _) => UpdateWebGpuLoadingProgress();
                    _ImGui = webGpuContext.CreateImGuiController(input);
                    var io = ImGuiNET.ImGui.GetIO();
                    io.ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;
                    ConfigImStyle();

                    if (GetAvailableImGuiFontPresets().Contains(ImGuiFontPreset.SegoeUIMono))
                    {
                        RequestImGuiFontPreset(ImGuiFontPreset.SegoeUIMono);
                    }
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

        public override void Load()
        {
            LoadingCoordinator.Begin();
            InitCustomNodeInspectorHooks();

            var world = Context.World;

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

                    float gridSize = 256.0f;
                    float halfGridSize = gridSize * 0.5f;
                    ManaSceneNode xzPlane, yxPlane, zyPlane, voxelsNode;

                    ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

                    lock (world.SceneLock)
                    {
                        xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize, "Grid Plane XZ");
                        yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, gridSize, 0, gridSize, "Grid Plane YX");
                        zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, gridSize, 0, gridSize, "Grid Plane ZY");

                        Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
                        Cursor.Name = "CursorBall";

                        voxelsNode = shapes.AddVoxels(Vector3.Zero);
                    }

                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, 0.1f, gridSize), new Vector3(halfGridSize, 0, halfGridSize));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, 0.1f), new Vector3(halfGridSize, halfGridSize, 0));
                    Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, gridSize, gridSize), new Vector3(0, halfGridSize, halfGridSize));

                    // Ground planes are raycast helpers for voxel placement, not meaningful selection targets.
                    _pickingExclusions.Add(xzPlane);
                    _pickingExclusions.Add(yxPlane);
                    _pickingExclusions.Add(zyPlane);
                    _pickingExclusions.Add(Cursor);

                    // 2. Load voxel model
                    Loading.Report(new ManaLoadingProgress("Loading MagicaVox model", 0.5f));
                    MagicaVoxLoader vox = new MagicaVoxLoader();
                    vox.Load();
                    var voxelNode = (ManaVoxelsNode)voxelsNode;
                    voxelNode.Name = "Startup Voxels";
                    var voxelShapes = voxelsNode.FindShapes<ManaVoxelShape>();
                    Debug.Assert(voxelShapes.Count == 1);

                    var voxelProgressInterval = Math.Max(1, vox.Voxels.Count / 100);
                    for (var voxelIndex = 0; voxelIndex < vox.Voxels.Count; voxelIndex++)
                    {
                        var voxel = vox.Voxels[voxelIndex];
                        voxelShapes[0].AddVoxel(voxel.Position + Vector3.One * 0.5f, voxel.Color);
                        if (voxelIndex == vox.Voxels.Count - 1 || voxelIndex % voxelProgressInterval == 0)
                        {
                            var importedVoxelFraction = (voxelIndex + 1) / (float)Math.Max(1, vox.Voxels.Count);
                            Loading.Report(new ManaLoadingProgress(
                                $"Building voxel model ({voxelIndex + 1:N0}/{vox.Voxels.Count:N0})",
                                0.5f + importedVoxelFraction * 0.25f));
                        }
                    }

                    voxelShapes[0].CommitData();
                    _viewModel.AttachVoxelData(voxelShapes[0], voxelNode);

                    // 3. Load Flight Helmet scene model
                    Loading.Report(new ManaLoadingProgress("Loading FlightHelmet GLTF model", 0.8f));
                    lock (world.SceneLock)
                    {
                        AddFlightHelmetReferenceModel();
                    }

                    // 4. Add a demo planet node. Water polygons load first -- patch colouring reads
                    // GeoWaterMask.Shared as the meshes generate.
                    Loading.Report(new ManaLoadingProgress("Loading water polygons", 0.88f));
                    LoadWaterMask();

                    Loading.Report(new ManaLoadingProgress("Generating planet", 0.9f));
                    lock (world.SceneLock)
                    {
                        AddPlanetDemo();
                    }

                    // 5. Probe for demo KML placemarks (real-world airspace boundaries). Deliberately only
                    // a file-existence check -- the actual parse/mesh build happens lazily the first time
                    // View > Show Airspace (KML) is switched on, so startup never pays for an overlay
                    // that's off by default (see SyncKmlAirspaceVisibility).
                    Loading.Report(new ManaLoadingProgress("Checking for KML placemarks", 0.95f));
                    _viewModel.KmlAirspaceAvailable = File.Exists(KmlDemoFilePath);

                    // 6. Add boundary line overlays (Natural Earth admin-0/admin-1 boundaries)
                    Loading.Report(new ManaLoadingProgress("Loading boundary lines", 0.98f));
                    lock (world.SceneLock)
                    {
                        AddCountryBordersDemo();
                        AddStateBordersDemo();
                    }

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

            // Built here (not in the background asset-loading task like the demo spheres above)
            // because ManaWaterPlane.GenerateMaterial() constructs its ManaWaterShader eagerly,
            // which needs a live graphics context rather than the deferred-upload path.
            _water = new ManaWaterNode(Context, new Vector2(48, 48), 128);
            _water.Position = new Vector3(64, 16, 64);
            _water.UpdateTransform();
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

        private void AddFlightHelmetReferenceModel()
        {
            var helmetPath = ResolveManaAssetPath(Path.Combine("Assets", "Models", "FlightHelmet", "glTF", "FlightHelmet.gltf"));
            try
            {
                var helmetRoot = Context.World.LoadFromGltf(helmetPath);
                helmetRoot.Name = "Flight Helmet";
                helmetRoot.Position = new Vector3(96.0f, 32.0f, 32.0f);
                helmetRoot.Rotation = new Vector3(0.0f, 35.0f.ToRadians(), 0.0f);
                helmetRoot.Scale = Vector3.One * 80.0f;
                helmetRoot.UpdateTransform();

                if (Context.World.Scenes.Count > 0)
                {
                    Context.World.Scenes[^1].Name = "Flight Helmet Scene";
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load Flight Helmet reference model: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load Flight Helmet reference model: {ex}");
            }
        }

        // Bundled baseline: NOAA ETOPO 2022 at 60 arc-second resolution, one global GeoTIFF
        // (~444MB, tracked via Git LFS — see .gitattributes), ships with the project under
        // Mana/Assets/Planet/ and is what the whole-globe patch grid loads by default.
        //
        // A separate, much finer 15 arc-second regional set (288 tiles, ~4.9GB, covering lat
        // 15N-45N/lon 90W-135W) already lives locally at PlanetDetailCacheFolder — that's *not* a
        // repo asset, it's the future on-demand streaming cache: once camera-distance patch
        // streaming exists, approaching that region should swap in those tiles for real detail
        // instead of the coarse global baseline. Not wired up yet — this method only reads the
        // bundled baseline for now.
        private const string PlanetBaselineAssetRelativePath = @"Assets\Planet\ETOPO_2022_v1_60s_N90W180_surface.tif";
        private const string PlanetDetailCacheFolder = @"C:\Users\night\Code\ManaAirSpace\Textures\HeightMaps\ETOPO2022";

        // Planet patches are cube-sphere tiles (6 faces x this many x this many), not lat/long-degree
        // tiles — see PlanetNode's doc comment for why (uniform patch size everywhere, no polar
        // wedge degeneration). 6 gives 216 total patches, comparable to (actually fewer than) the
        // 288 the old 15-degree lat/long grid produced.
        private const int PlanetPatchesPerFaceEdge = 6;

        // SceneInspectorPanelView (Mana) can't reference PlanetNode/ManaLineMesh-using nodes
        // (ManaGeoTools) directly — wrong dependency direction — so it exposes a hook that ManaVoxel
        // (which references both) fills in. One hook body, not one registration per node type, since
        // CustomNodeInspectorHook is a single delegate slot.
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
                            RevealFileInExplorer(cachedTilePaths[0]);
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
                OpenFolderInFileExplorer(TerrariumTileFetcher.CacheRoot);
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

        private static void OpenFolderInFileExplorer(string path)
        {
            Directory.CreateDirectory(path);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to open folder '{path}': {ex.Message}");
            }
        }

        // Selects filePath in its containing folder (Explorer/Finder) rather than opening it directly --
        // lets the user see sibling tiles too, and works the same whether or not there's a default image
        // viewer associated with .png.
        private static void RevealFileInExplorer(string filePath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", $"-R \"{filePath}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to reveal file '{filePath}': {ex.Message}");
            }
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

        private void AddPlanetDemo()
        {
            try
            {
                // Radius/position sized for this scene (256-unit grid), not the ManaGeoTools default
                // of 100 — placed in a clear corner, elevated well above the ground plane.
                // heightExaggeration=300 makes real terrain/bathymetry read as a visible bump at this
                // radius; true-scale relief (exaggeration=1) is nearly flat at planet scale, same as
                // it looks on the real Earth from orbit.
                // resolution=24 is only the smooth placeholder grid. Once the ETOPO baseline decodes,
                // PlanetNode raises baseline terrain patches up to its bounded baseline cap; still no
                // camera-distance LOD/streaming, so the whole globe builds and stays resident up front.
                //
                // Construction itself is instant (a smooth placeholder sphere, no GeoTIFF decode);
                // LoadHeightDataAsync below kicks off the real ~9s decode in the background and swaps
                // in real terrain/color as it completes, without blocking this load task or the
                // loading screen.
                _planet = new PlanetNode(
                    Context,
                    patchesPerFaceEdge: PlanetPatchesPerFaceEdge,
                    radius: 40.0f, resolution: 24,
                    heightExaggeration: 1.0f,
                    // Keep the ocean's startup mesh on the same baseline-resolution knob as terrain.
                    // This is a numeric match, not exact spatial density: exact matching would require
                    // a tiled ocean mesh or a much higher per-face resolution.
                    oceanResolution: PlanetNode.DefaultBaselineMaxPatchMeshResolution);
                _planet.Position = new Vector3(200.0f, 60.0f, 200.0f);
                _planet.UpdateTransform();

                Context.World.DefaultScene.Children.Add(_planet);

                var baselinePath = ResolveManaAssetPath(PlanetBaselineAssetRelativePath);
                if (File.Exists(baselinePath))
                {
                    var heightLibrary = new GeoTiffLibrary(Path.GetDirectoryName(baselinePath)!);
                    _planet.LoadHeightDataAsync(heightLibrary);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to build demo planet: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to build demo planet: {ex}");
            }
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
        // multiply per point, so this is a small slice of a frame; the whole-mesh ribbon rebuild only
        // happens once an overlay's queue drains.
        private const int DrapePointsPerFrame = 2048;

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
                if (_drapedOverlays.Count == 0)
                {
                    return;
                }

                overlays = _drapedOverlays.ToArray();
            }

            var changed = _planet.DrainChangedPatches();
            if (changed.Count > 0)
            {
                foreach (var overlay in overlays)
                {
                    overlay.QueueChanged(changed);
                }
            }

            foreach (var overlay in overlays)
            {
                if (overlay.Tick(DrapePointsPerFrame))
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
            var mouseDropWorld = GetProjectedMouseHit();
            var dropPosition = mouseDropWorld.Hit ? mouseDropWorld.Position : (Vector3?)null;
            _fileImportService.ImportDroppedFiles(files, dropPosition, Context.World, _viewModel);
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


        public const float TimestepDuration = 1 / 60f;
        protected override void DrawMainMenuBar()
        {
            _mainMenuView.Draw(_viewModel, this);
        }

        public override void Update(double deltaTime)
        {
            if (GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return;
            }

            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();
            SyncWaterVisibility();
            SyncWireframeViewOption();
            SyncNormalsViewOption();
            SyncKmlAirspaceVisibility();
            ProcessPendingRegionRequest();

            // Must run on the main thread -- finalizing a completed patch split touches the scene
            // graph (AddChild/RemoveChild), which can't safely happen from the background fetch task
            // that detects a split is ready (see PlanetNode.UpresPendingChildrenAsync's comment).
            _planet?.UpdateAutoLod(Context.World.MainCamera);
            _planet?.UpdateNearOcean(Context.World.MainCamera);
            _planet?.UpdateDetailChase();
            _planet?.ProcessPendingSplits();
            _planet?.ProcessPendingMerges();
            // Same main-thread requirement, for the same reason -- adopts a finished region mesh.
            _region?.ProcessPendingLoad();
            RedrapePlanetOverlays();
            PushPlanetStreamingStatusToWorldPerf();
            UpdateWaterRippleInteraction();
        }

        // Surfaces PlanetNode's current up-res/split/merge/fetch activity in the World Perf panel's
        // "Streaming" section (Mana.Code.Scenes.ManaWorld.DrawImGui) -- added after auto-LOD made it easy
        // to have many patches streaming in at once with no visibility into how much work was in flight.
        private void PushPlanetStreamingStatusToWorldPerf()
        {
            if (_planet == null)
            {
                return;
            }

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

            // Must run on the main thread -- finalizing a completed patch split touches the scene
            // graph (AddChild/RemoveChild), which can't safely happen from the background fetch task
            // that detects a split is ready (see PlanetNode.UpresPendingChildrenAsync's comment).
            _planet?.UpdateAutoLod(Context.World.MainCamera);
            _planet?.UpdateNearOcean(Context.World.MainCamera);
            _planet?.UpdateDetailChase();
            _planet?.ProcessPendingSplits();
            _planet?.ProcessPendingMerges();
            // Same main-thread requirement, for the same reason -- adopts a finished region mesh.
            _region?.ProcessPendingLoad();

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

            _panelsView.Draw(_viewModel, ref rayOrigin, ref rayDirection);
            ProcessMeshPickerPlacement(MouseState, sceneMin, sceneMax);

            ImGui.End();

            if (_toolDockId != 0)
            {
                ImGui.SetNextWindowDockID(_toolDockId, ImGuiCond.FirstUseEver);
            }

            _toolPaletteView.Draw(_viewModel);

            bool gizmoCapturesMouse = _cameraGizmoView.Draw(Context.World.MainCamera, sceneMin, sceneMax);
            bool imguiCapturesMouse = ImGui.GetIO().WantCaptureMouse || gizmoCapturesMouse;

            UpdateVoxelPointerInteraction(imguiCapturesMouse);
            UpdateFrameSelectedShortcut();

            _placementDepthOverlayView.Draw(Context.World.MainCamera, sceneMin, sceneMax, _viewModel);

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
        }

        private void ProcessMeshPickerPlacement(Silk.NET.Input.IMouse mouseState, Vector2 sceneMin, Vector2 sceneMax)
        {
            if (HandleMeshPickerEscapeCancel())
            {
                _panelsView.ClearDraggedMeshAssetPath();
            }

            var pendingPath = _panelsView.ConsumePendingMeshSpawnPath();
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
            var draggedPath = _panelsView.CurrentDraggedMeshAssetPath;
            if (!string.IsNullOrWhiteSpace(draggedPath) && isOverScene)
            {
                BeginMeshPickerPlacementPreview(draggedPath);
            }

            if (_meshPickerPreviewNode != null && isOverScene)
            {
                UpdateMeshPickerPlacementPreviewTransform();
            }

            if (!leftMouseDown && _meshPickerDragWasDown && !string.IsNullOrWhiteSpace(draggedPath))
            {
                if (isOverScene && string.Equals(draggedPath, _meshPickerPreviewAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    CommitMeshPickerPlacementPreview();
                }
                else
                {
                    CancelMeshPickerPlacementPreview();
                }

                _panelsView.ClearDraggedMeshAssetPath();
            }

            var placeClick = leftMouseDown && !_meshPickerPlaceClickWasDown;
            _meshPickerPlaceClickWasDown = leftMouseDown;
            if (placeClick &&
                _meshPickerPreviewNode != null &&
                string.IsNullOrWhiteSpace(draggedPath) &&
                isOverScene &&
                !ImGui.GetIO().WantCaptureMouse)
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

            var keyDown = !ImGui.GetIO().WantCaptureKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape);
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

            var mouseHit = GetProjectedMouseHit();
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
            _meshPickerPreviewAssetPath = null;
            _meshPickerPreviewNode = null;
            _meshPickerPreviewSceneStartIndex = -1;
            _meshPickerPreviewMeshStartIndex = -1;
            _meshPickerPreviewMaterialStartIndex = -1;
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
                return Context.World.LoadFromGltfMemory(memory.ToArray(), entry.Name, sidecarResolver);
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

        private static bool TryDecodeZipAssetReference(string assetReference, out string zipPath, out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;

            if (!assetReference.StartsWith(ZipAssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = assetReference.Substring(ZipAssetPrefix.Length);
            var separator = payload.IndexOf(':');
            if (separator <= 0 || separator >= payload.Length - 1)
            {
                return false;
            }

            try
            {
                zipPath = Encoding.UTF8.GetString(Convert.FromBase64String(payload[..separator]));
                entryPath = Encoding.UTF8.GetString(Convert.FromBase64String(payload[(separator + 1)..]));
                return !string.IsNullOrWhiteSpace(zipPath) && !string.IsNullOrWhiteSpace(entryPath);
            }
            catch
            {
                return false;
            }
        }

        private void ImportMeshAssetFromPicker(string filePath)
        {
            try
            {
                var mouseHit = GetProjectedMouseHit();
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
                UpdateSelectionPointerInteraction(inputCaptured, MouseState);
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

            RayHit hit = GetProjectedMouseHit();
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

            var keyDown = !ImGui.GetIO().WantCaptureKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.F);
            var pressed = keyDown && !_frameSelectedKeyWasDown;
            _frameSelectedKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            var selection = Context.World.Selection;
            Context.World.MainCamera.TryFrameSelection(selection.SelectedComponent as MeshRendererComponent, selection.SelectedNode);
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
            Context.World.Selection.SelectedNode = pickResult?.Node;
            Context.World.Selection.SelectedComponent = pickResult?.Renderer;

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

            var mouse = Context.Input.Mice.FirstOrDefault();
            return _toolController.IsDrawing || mouse?.IsButtonPressed(Silk.NET.Input.MouseButton.Left) == true;
        }

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

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
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

            var sceneChildren = Context.World.DefaultScene.Children;
            if (_viewModel.ShowWater)
            {
                if (!sceneChildren.Contains(_water))
                {
                    sceneChildren.Add(_water);
                }
            }
            else
            {
                sceneChildren.Remove(_water);
            }
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

            var waterY = water.Position.Y;
            if (MathF.Abs(ray.Direction.Y) < 0.0001f)
            {
                return;
            }

            var travel = (waterY - ray.Origin.Y) / ray.Direction.Y;
            if (travel <= 0.0f)
            {
                return;
            }

            var worldHit = ray.Origin + ray.Direction * travel;
            var localHit = worldHit - water.Position;
            var halfSize = water.Mesh.Size * 0.5f;
            if (MathF.Abs(localHit.X) > halfSize.X || MathF.Abs(localHit.Z) > halfSize.Y)
            {
                return;
            }

            water.SpawnRipple(worldHit);
        }

    }
}
