using ImGuiNET;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    public class VoxelEditorGameMode : BaseGameMode
    {
        private const string ManaWindowTitle = "Mana";
        private uint _toolDockId;
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

        public VoxelEditorGameMode()
        {

        }

        public override void Load()
        {
            base.Load();

            Context.Window.FileDrop += Window_FileDrop;
            _viewModel.AttachScene(Context, Context.World.DefaultScene);

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
            if (Cursor.Components[0] is MeshRendererComponent renderer)
            {
                renderer.Material.Shader = new ManaLiquidShader(Context);
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
            var MouseState = Context.Input.Mice.First();

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
        }

        protected override void DrawImGui(double deltaTime)
        {
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
            bool leftMouseDown = MouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            var toolSettings = _viewModel.CreateToolSettings();

            if (imguiCapturesMouse)
            {
                _toolController.Cancel();
                _viewModel.ClearToolPreview();
                _viewModel.ClearHoveredVoxel();
            }
            else
            {
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
                }
                else
                {
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
            }

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

        protected override void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
            ImGuiDockNative.DockBuilderSetNodeSize(dockspaceId, dockspaceSize);

            uint leftRemainderDockId;
            _ = ImGuiDockNative.DockBuilderSplitNode(
                dockspaceId,
                ImGuiDir.Left,
                0.07f,
                out _toolDockId,
                out leftRemainderDockId);

            uint centerDockId;
            uint rightDockId;
            _ = ImGuiDockNative.DockBuilderSplitNode(
                leftRemainderDockId,
                ImGuiDir.Right,
                0.28f,
                out rightDockId,
                out centerDockId);

            _ = ImGuiDockNative.DockBuilderSplitNode(
                rightDockId,
                ImGuiDir.Down,
                0.45f,
                out _utilityDockId,
                out _manaDockId);

            ImGuiDockNative.DockBuilderDockWindow(VoxelToolPaletteView.WindowTitle, _toolDockId);
            ImGuiDockNative.DockBuilderDockWindow(ManaWindowTitle, _manaDockId);
            ImGuiDockNative.DockBuilderDockWindow("Camera", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("World Perf", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Irradiance", _utilityDockId);
            ImGuiDockNative.DockBuilderDockWindow("Dear ImGui Demo", _utilityDockId);
            BaseGameMode.UtilityDockId = _utilityDockId;
            ImGuiDockNative.DockBuilderFinish(dockspaceId);
        }

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
        }

    }
}
