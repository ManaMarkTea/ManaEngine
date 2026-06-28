using ImGuiNET;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using System.Diagnostics;

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
        private readonly ViewportCameraGizmoView _cameraGizmoView = new();
        private readonly VoxelPlacementDepthOverlayView _placementDepthOverlayView = new();
        private readonly VoxelToolController _toolController = new();

        public ManaSceneNode Cursor { get; private set; } = null!;
        private ManaSceneNode? _lightningBall;
        private ManaSceneNode? _liquidBall;

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
                deltaTime =>
                {
                    ProcessPendingFontPreset();
                    _ImGui?.Update((float)deltaTime);
                    DrawMainMenuBar();
                    DrawDockSpace();
                    DrawImGui(deltaTime);
                    Context.World.DrawImGui();
                },
                (webGpuContext, input) =>
                {
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

        protected override void LoadContent()
        {
            Context.Window.FileDrop += Window_FileDrop;
            _viewModel.AttachScene(Context, Context.World.DefaultScene);
            Context.World.MainCamera.ShouldCaptureMouseInput = ShouldCaptureEditorMouseInput;

            ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

            float gridSize = 256.0f;
            float halfGridSize = gridSize * 0.5f;

            var xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, gridSize, 0, gridSize);
            var yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, gridSize, 0, gridSize);
            var zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, gridSize, 0, gridSize);
            
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, 0.1f, gridSize), new Vector3(halfGridSize, 0, halfGridSize));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(gridSize, gridSize, 0.1f), new Vector3(halfGridSize, halfGridSize, 0));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, gridSize, gridSize), new Vector3(0, halfGridSize, halfGridSize));


            Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
            Cursor.Name = "CursorBall";
            
            if (Cursor.Components[0] is MeshRendererComponent renderer)
            {
                var cursorMaterial = renderer.DynamicMaterial;
                cursorMaterial.Name = "Cursor Material";
                cursorMaterial.Shader = new ManaLiquidShader(Context);
            }

            _lightningBall = shapes.AddSphere(new Vector3(10, 10, 10), Colors.White, 1.0f);
            _lightningBall.Name = "Lightning Ball";
            _lightningBall.Scale = new Vector3(20, 20, 20);
            if (_lightningBall.Components[0] is MeshRendererComponent lightningRenderer)
            {
                var lightningMaterial = lightningRenderer.DynamicMaterial;
                lightningMaterial.Name = "Lightning Ball Material";
                lightningMaterial.Shader = new ManaLightningBallShader(Context);
                lightningRenderer.DrawAfterScene = true;
            }

            _liquidBall = shapes.AddSphere(new Vector3(34, 10, 10), Colors.White, 1.0f);
            _liquidBall.Name = "Liquid Ball";
            _liquidBall.Scale = new Vector3(20, 20, 20);
            if (_liquidBall.Components[0] is MeshRendererComponent liquidRenderer)
            {
                var liquidMaterial = liquidRenderer.DynamicMaterial;
                liquidMaterial.Name = "Liquid Ball Material";
                liquidMaterial.Shader = new ManaLiquidBallShader(Context);
                liquidMaterial.SetShaderUniform("liquidFillLevel", -0.14f);
                liquidMaterial.SetShaderUniform("liquidWaveStrength", 1.0f);
                liquidMaterial.SetShaderUniform("liquidWaterOpacity", 1.0f);
                liquidMaterial.SetShaderUniform("liquidSurfaceStrength", 1.0f);
                liquidMaterial.SetShaderUniform("liquidPoolContourStrength", 1.0f);
                liquidMaterial.SetShaderUniform("glassEdgeStrength", 1.0f);
                liquidMaterial.SetShaderUniform("glassAirAlpha", 1.0f);
                liquidRenderer.DrawAfterScene = true;
            }


            MagicaVoxLoader vox = new MagicaVoxLoader();
            vox.Load();

            var voxelsNode = shapes.AddVoxels(Vector3.Zero);
            var voxelNode = (ManaVoxelsNode)voxelsNode;
            voxelNode.Name = "Startup Voxels";
            var voxelShapes = voxelsNode.FindShapes<ManaVoxelShape>();
            Debug.Assert(voxelShapes.Count == 1);

            foreach (var voxel in vox.Voxels)
            {
                voxelShapes[0].AddVoxel(voxel.Position + Vector3.One * 0.5f, voxel.Color);
            }

            voxelShapes[0].CommitData();
            _viewModel.AttachVoxelData(voxelShapes[0], voxelNode);

            AddFlightHelmetReferenceModel();
        }

        private void AddFlightHelmetReferenceModel()
        {
            var helmetPath = ResolveManaAssetPath(Path.Combine("Assets", "Models", "FlightHelmet", "glTF", "FlightHelmet.gltf"));
            try
            {
                var helmetRoot = Context.World.LoadFromGltf(helmetPath);
                helmetRoot.Name = "Flight Helmet";
                helmetRoot.Position = new Vector3(96.0f, 6.0f, 32.0f);
                helmetRoot.Rotation = new Vector3(0.0f, 35.0f.ToRadians(), 0.0f);
                helmetRoot.Scale = Vector3.One * 8.0f;
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
            Context.World.PostUpdateFutures.Add(() => UpdateVoxelPointerInteraction(inputCaptured: false));
        }

        protected override void DrawImGui(double deltaTime)
        {
            SyncLightningBallVisibility();
            SyncLiquidBallVisibility();

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
        }

        private void UpdateVoxelPointerInteraction(bool inputCaptured)
        {
            var MouseState = Context.Input.Mice.FirstOrDefault();
            if (MouseState == null)
            {
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
            ImGuiDockNative.DockBuilderDockWindow("Camera", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Inspector", _utilityDockId);
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

    }
}
