using ImGuiNET;
using Mana.Code.Nodes;
using ManaVoxel.Code.Loaders;
using System.Diagnostics;

namespace Mana.Code.GameModes
{
    public class VoxelEditorGameMode : BaseGameMode
    {
        TimeSpan[]? fpsData;

        public ManaSceneNode Cursor { get; private set; } = null!;

        public VoxelEditorGameMode()
        {

        }

        ManaVoxelShape? voxel;
        ManaVoxelsNode? voxelNode;
        int initialVoxelCount;
        int bakedPhysicsVoxelCount;
        Vector4 paintVoxelColor = Colors.Magenta;
        Vector3 importPositionOffset = Vector3.Zero;
        Vector3 importRotationDegrees = Vector3.Zero;
        Vector3 importScale = Vector3.One;

        public override void Load()
        {
            base.Load();

            Context.Window.FileDrop += Window_FileDrop;

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
            voxelNode = (ManaVoxelsNode)voxelsNode;
            var voxelShapes = voxelsNode.FindShapes<ManaVoxelShape>();
            Debug.Assert(voxelShapes.Count == 1);

            foreach (var voxel in vox.Voxels)
            {
                voxelShapes[0].AddVoxel(voxel.Position + Vector3.One * 0.5f, voxel.Color);
            }

            voxelShapes[0].CommitData();
            voxel = voxelShapes[0];
            initialVoxelCount = voxel.VoxelCount;
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
                    if (voxelNode != null && voxelNode.IsBakedToDynamicPhysics)
                    {
                        // Ignore voxel imports once baked into dynamic physics mode.
                        continue;
                    }

                    var voxLoader = new MagicaVoxLoader();
                    voxLoader.Load(file);

                    if (voxel == null)
                    {
                        continue;
                    }

                    foreach (var voxelData in voxLoader.Voxels)
                    {
                        var voxelPosition = voxelData.Position + Vector3.One * 0.5f;
                        voxelPosition += lastImportPos + importPositionOffset;
                        voxel.AddVoxel(voxelPosition, voxelData.Color);
                    }

                    voxel.CommitData();
                    lastImportPos.X += 2.0f;
                    continue;
                }

                var fileWorld = Context.World;
                var model = fileWorld.LoadFromGltf(file);
                model.Position = lastImportPos + importPositionOffset;
                model.Rotation = new Vector3(
                    importRotationDegrees.X.ToRadians(),
                    importRotationDegrees.Y.ToRadians(),
                    importRotationDegrees.Z.ToRadians());
                model.Scale = new Vector3(
                    MathF.Max(0.01f, importScale.X),
                    MathF.Max(0.01f, importScale.Y),
                    MathF.Max(0.01f, importScale.Z));
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
        public override void Update(double deltaTime)
        {
            ImGui.SetNextWindowPos(new Vector2(this._Window.Size.X - 200, 15));
            ImGui.Begin("Mana");
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");
            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.Text($"Voxels: {voxel?.VoxelCount ?? 0}");
            ImGui.Text($"Created This Session: {Math.Max(0, (voxel?.VoxelCount ?? 0) - initialVoxelCount)}");
            ImGui.Text($"Pending Voxels: {voxel?.PendingVoxelCount ?? 0}");
            ImGui.Text($"Duplicate Skips: {voxel?.DuplicateVoxelRejectCount ?? 0}");
            ImGui.Text($"Physics Baked Voxels: {bakedPhysicsVoxelCount}");
            ImGui.ColorEdit4("Voxel Paint", ref paintVoxelColor);
            ImGui.InputFloat3("Import Pos Offset", ref importPositionOffset);
            ImGui.InputFloat3("Import Rot (deg)", ref importRotationDegrees);
            ImGui.InputFloat3("Import Scale", ref importScale);

            bool canBakeToPhysics = voxelNode != null && !voxelNode.IsBakedToDynamicPhysics;
            if (!canBakeToPhysics)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Bake Voxels To Physics (Fall)"))
            {
                if (voxelNode != null)
                {
                    bakedPhysicsVoxelCount = voxelNode.BakeVoxelsToDynamicPhysics();
                }
            }

            if (!canBakeToPhysics)
            {
                ImGui.EndDisabled();
            }

            if (fpsData == null)
            {
                fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
            }

            float[] data = ManaTime.GlobalTime.OrderedHistory(ref fpsData).Select((i) => (float)i.TotalMilliseconds).ToArray<float>();
            ImGui.PlotLines("FPS", ref data[0], data.Length, 0, "fps", 0, 100, new Vector2(150, 90));            
      
            var MouseState = Context.Input.Mice.First();

            var mouseStart = new Vector3(MouseState.Position, 0.0f);
            var mouseEnd = new Vector3(MouseState.Position, 1.0f);
            
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var rayOrigin = ray.Origin;
            var rayDirection = ray.Direction;

            bool imguiCapturesMouse = ImGui.GetIO().WantCaptureMouse;

            if (!imguiCapturesMouse)
            {
                RayHit hit = GetProjectedMouseHit();
                if (hit.Hit)
                {
                    var pos = hit.Position;
                    pos.X = MathF.Floor(pos.X);
                    pos.Y = MathF.Floor(pos.Y);
                    pos.Z = MathF.Floor(pos.Z);

                    pos.X += 0.5f;
                    pos.Y += 0.5f;
                    pos.Z += 0.5f;

                    Cursor.Position = pos;

                    if (MouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left))
                    {
                        if (voxelNode != null && voxelNode.IsBakedToDynamicPhysics)
                        {
                            Context.Physics.ApplyExplosion(pos, radius: 6.0f, strength: 6.0f);
                        }
                        else
                        {
                            // Place voxels only when the scene has mouse focus, not while dragging ImGui windows.
                            if (voxel != null)
                            {
                                voxel.AddVoxel(pos, paintVoxelColor);
                                voxel.CommitData();
                            }
                        }
                    }
                }
            }

            ImGui.InputFloat3("RAY", ref rayOrigin);
            ImGui.InputFloat3("DIR", ref rayDirection);


            ImGui.ShowDemoWindow();
            
            ImGui.End();
        }

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
        }

    }
}
