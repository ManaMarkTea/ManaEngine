using ImGuiNET;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code.GeoTiff;
using ManaGeoTools.Code.Kml;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Shapefile;
using System.Diagnostics;
using System.IO.Compression;

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
        private readonly ViewportCameraGizmoView _cameraGizmoView = new();
        private readonly VoxelPlacementDepthOverlayView _placementDepthOverlayView = new();
        private readonly VoxelToolController _toolController = new();
        private bool _firstFrame = true;
        private bool _selectPointerWasDown;
        private ManaSceneNode? _lastOutlinedSelectionNode;
        private readonly HashSet<ManaSceneNode> _pickingExclusions = new();
        private static readonly Vector4 SelectionOutlineColor = new(0.30f, 0.72f, 1.0f, 1.0f);

        public ManaSceneNode Cursor { get; private set; } = null!;
        private ManaSceneNode? _lightningBall;
        private ManaSceneNode? _liquidBall;
        private PlanetNode? _planet;
        private ManaSceneNode? _kmlDemo;
        private ManaSceneNode? _countryBorders;
        private ManaWaterNode? _water;
        private bool _waterRipplePointerWasDown;
        private ManaWebGpuContext? _webGpuContext;

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
                        xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize);
                        yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, gridSize, 0, gridSize);
                        zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, gridSize, 0, gridSize);

                        Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
                        Cursor.Name = "CursorBall";

/*
                        _lightningBall = shapes.AddSphere(new Vector3(10, 10, 10), Colors.White, 1.0f);
                        _lightningBall.Name = "Lightning Ball";
                        _lightningBall.Scale = new Vector3(20, 20, 20);

                        _liquidBall = shapes.AddSphere(new Vector3(34, 10, 10), Colors.White, 1.0f);
                        _liquidBall.Name = "Liquid Ball";
                        _liquidBall.Scale = new Vector3(20, 20, 20);
*/
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

                    // 4. Add a demo planet node
                    Loading.Report(new ManaLoadingProgress("Generating planet", 0.9f));
                    lock (world.SceneLock)
                    {
                        AddPlanetDemo();
                    }

                    // 5. Add demo KML placemarks (real-world airspace boundaries)
                    Loading.Report(new ManaLoadingProgress("Loading KML placemarks", 0.95f));
                    lock (world.SceneLock)
                    {
                        AddKmlDemo();
                    }

                    // 6. Add country border lines (Natural Earth admin-0 country boundaries)
                    Loading.Report(new ManaLoadingProgress("Loading country borders", 0.98f));
                    lock (world.SceneLock)
                    {
                        AddCountryBordersDemo();
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
                // resolution=24 keeps total vertex/draw-call count reasonable across all 216 patches
                // the cube-sphere grid divides the globe into — still no camera-distance LOD/
                // streaming, so the whole globe builds and stays resident up front.
                //
                // Construction itself is instant (a smooth placeholder sphere, no GeoTIFF decode);
                // LoadHeightDataAsync below kicks off the real ~9s decode in the background and swaps
                // in real terrain/color as it completes, without blocking this load task or the
                // loading screen.
                _planet = new PlanetNode(
                    Context,
                    patchesPerFaceEdge: PlanetPatchesPerFaceEdge,
                    radius: 40.0f, resolution: 24,
                    heightExaggeration: 20.0f,
                    // Higher than the terrain patch resolution -- waves need enough vertex density to
                    // resolve whatever wave frequency is dialed in without aliasing, and the ocean is
                    // one un-tiled shell covering the whole globe rather than one of many small
                    // patches, so it needs more vertices to match even a similar visual density.
                    oceanResolution: 96);
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
        // any machine without it. About 89% of its LinearRings are degenerate — two distinct points
        // repeated to look like a closed 5-point ring, a data-quality artifact in the source file
        // itself (the header even carries an "output may contain errors" disclaimer), not a
        // triangulator bug: there's no triangle to make from a zero-area line. Those are silently
        // skipped by KmlLinearRingMesh/PolygonTriangulator, same as any other degenerate ring; the
        // remaining ~1,850 real polygons plus ~1,287 point placemarks still load and render fine.
        private const string KmlDemoFilePath = @"C:\Users\night\Code\ManaAirSpace\AirSpaceModels\UnitedStates.xml";

        private void AddKmlDemo()
        {
            try
            {
                if (!File.Exists(KmlDemoFilePath))
                {
                    return;
                }

                // Same "shrink ratio" reasoning as the planet, and the same radius, so the two read
                // as the same-scale world: real Earth radius/altitude scaled down to fit this scene.
                const float radius = 40.0f;
                var altitudeScale = radius / PlanetLoader.EarthRadiusMeters;
                var kml = new Kml(KmlDemoFilePath, radius, altitudeScale);

                // The full file has 2,574 placemarks, but ~89% of its rings are degenerate (two
                // distinct points repeated to look like a closed ring — see KmlLinearRing.BuildMesh)
                // and no longer produce a mesh node at all, so this is really ~1,848 real polygons —
                // no cap needed. Builds in well under a second; still one draw call per mesh with no
                // culling/LOD/batching, so revisit if that many draw calls ever becomes the
                // bottleneck (the planet got a background-build treatment for its own scale problem;
                // this hasn't needed it yet).
                _kmlDemo = KmlSceneBuilder.BuildPlacemarks(Context, kml);
                _kmlDemo.Name = "US Airspace (KML)";

                // Parented under the planet (not the scene root) so it moves with it — its geometry
                // is already built on the same radius/origin convention as the planet (Kml.EarthRadius
                // == the planet's own radius above), so it needs to sit at the planet's local origin,
                // not an arbitrary world-space offset.
                if (_planet != null)
                {
                    _planet.AddChild(_kmlDemo);
                    _kmlDemo.Position = Vector3.Zero;
                    _kmlDemo.UpdateTransform();
                }
                else
                {
                    _kmlDemo.Position = new Vector3(280.0f, 60.0f, 200.0f);
                    _kmlDemo.UpdateTransform();
                    Context.World.DefaultScene.Children.Add(_kmlDemo);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load demo KML: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load demo KML: {ex}");
            }
        }

        // Natural Earth's admin-0 country boundaries, 50m resolution (~100K raw points across 242
        // countries/territories before decimation -- see CountryBorderLines). A 10m version sits
        // alongside it in the same folder for a future zoom-dependent LOD pass; not wired up yet.
        // Not a repo asset (sourced externally, ~800KB), same treatment as KmlDemoFilePath -- degrades
        // to a no-op via the File.Exists guard on any machine without it.
        private const string CountryBordersZipPath = @"C:\Users\night\Code\ManaRenderer\Data\ne_50m_admin_0_countries.zip";
        private const string CountryBordersShpEntryName = "ne_50m_admin_0_countries.shp";

        private void AddCountryBordersDemo()
        {
            try
            {
                if (_planet == null || !File.Exists(CountryBordersZipPath))
                {
                    return;
                }

                var rings = new List<IReadOnlyList<(double Longitude, double Latitude)>>();
                using (var archive = ZipFile.OpenRead(CountryBordersZipPath))
                {
                    var shpEntry = archive.GetEntry(CountryBordersShpEntryName)
                        ?? throw new InvalidOperationException($"'{CountryBordersShpEntryName}' not found in {CountryBordersZipPath}.");
                    using var shpStream = shpEntry.Open();
                    foreach (var ring in ShapefilePolygonReader.ReadRings(shpStream))
                    {
                        rings.Add(ring);
                    }
                }

                // Thin square-cross-section lines (see ManaLineMesh's doc comment for why square
                // cross-sections specifically) sitting just above the terrain radius (avoids
                // z-fighting), warm amber and emissive so they read on the planet's night side too.
                // All tunable if the look needs adjusting once visible.
                const float thickness = 0.15f;
                const float radiusOffset = 0.3f;
                const float minSegmentDegrees = 0.15f;
                var color = new Vector4(1.0f, 0.75f, 0.3f, 1.0f);
                const float emissionStrength = 2.5f;
                var mesh = CountryBorderLines.Build(Context, rings, _planet.Radius + radiusOffset, thickness, minSegmentDegrees, color, emissionStrength);
                _countryBorders = new ManaMeshSceneNode(Context, mesh) { Name = "Country Borders" };

                // Parented under the planet so it moves/rotates with it, same reasoning as the KML
                // overlay above.
                _planet.AddChild(_countryBorders);
                _countryBorders.Position = Vector3.Zero;
                _countryBorders.UpdateTransform();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load country borders: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load country borders: {ex}");
            }
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

        Vector3 startImportPos = new Vector3(30.0f, 0, 20);
        Vector3 lastImportPos = new Vector3(30.0f, 0, 20);
        private void Window_FileDrop(string[] files)
        {
            var mouseDropWorld = GetProjectedMouseHit();
            if (mouseDropWorld.Position.LengthSquared() != 0)
            {
                lastImportPos = mouseDropWorld.Position;
            }

            foreach (var file in files)
            {
                if (Path.GetExtension(file).Equals(".vox", StringComparison.OrdinalIgnoreCase))
                {
                    var voxLoader = new MagicaVoxLoader();
                    voxLoader.Load(file);

                    var importedVoxels = voxLoader.Voxels
                        .Select(voxelData => new ManaVoxelData
                        {
                            Position = voxelData.Position + Vector3.One * 0.5f,
                            Color = voxelData.Color
                        })
                        .ToArray();

                    _viewModel.CreateVoxelLayer(
                        Path.GetFileNameWithoutExtension(file),
                        "Import",
                        lastImportPos + _viewModel.ImportPositionOffset,
                        importedVoxels);

                    lastImportPos.X += 2.0f;
                    continue;
                }

                var fileWorld = Context.World;
                var model = fileWorld.LoadFromGltf(file);
                model.Position = lastImportPos + _viewModel.ImportPositionOffset;
                model.Rotation = new Vector3(
                    _viewModel.ImportRotationDegrees.X.ToRadians(),
                    _viewModel.ImportRotationDegrees.Y.ToRadians(),
                    _viewModel.ImportRotationDegrees.Z.ToRadians());
                model.Scale = new Vector3(
                    MathF.Max(0.01f, _viewModel.ImportScale.X),
                    MathF.Max(0.01f, _viewModel.ImportScale.Y),
                    MathF.Max(0.01f, _viewModel.ImportScale.Z));
                lastImportPos.X += 2.0f;

                if (lastImportPos.X > startImportPos.X + 30)
                {
                    lastImportPos.X = startImportPos.X;
                    lastImportPos.Z += 2.0f;
                }                
            }
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
            UpdateWaterRippleInteraction();
        }

        protected override void DrawImGui(double deltaTime)
        {
            PushWebGpuFrameStats();

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
            var selectedNode = Context.World.Selection.SelectedNode;
            if (_lastOutlinedSelectionNode != null && _lastOutlinedSelectionNode != selectedNode && _lastOutlinedSelectionNode != activeNode)
            {
                foreach (var meshRenderer in _lastOutlinedSelectionNode.Components.OfType<MeshRendererComponent>())
                {
                    meshRenderer.OutlineEnabled = false;
                }
            }

            if (selectedNode != null && selectedNode != activeNode)
            {
                foreach (var meshRenderer in selectedNode.Components.OfType<MeshRendererComponent>())
                {
                    meshRenderer.OutlineEnabled = true;
                    meshRenderer.OutlineColor = SelectionOutlineColor;
                    meshRenderer.OutlineWidth = 4.0f;
                }
            }

            _lastOutlinedSelectionNode = selectedNode;

            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();
            SyncWaterVisibility();

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

            ImGui.End();

            if (_toolDockId != 0)
            {
                ImGui.SetNextWindowDockID(_toolDockId, ImGuiCond.FirstUseEver);
            }

            _toolPaletteView.Draw(_viewModel);

            bool gizmoCapturesMouse = _cameraGizmoView.Draw(Context.World.MainCamera, sceneMin, sceneMax);
            bool imguiCapturesMouse = ImGui.GetIO().WantCaptureMouse || gizmoCapturesMouse;

            UpdateVoxelPointerInteraction(imguiCapturesMouse);

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

            Context.World.Selection.SelectedNode = ManaScenePicker.Pick(Context.World.Scenes, ray.Origin, ray.Direction, _pickingExclusions);
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
