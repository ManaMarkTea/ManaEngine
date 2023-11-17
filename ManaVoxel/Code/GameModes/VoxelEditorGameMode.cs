using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using ImGuiNET;
using Mana.Code.Nodes;
using ManaVoxel.Code;
using ManaVoxel.Code.Loaders;
using Silk.NET.SDL;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Mana.Code.GameModes
{
    public class VoxelEditorGameMode : BaseGameMode
    {
        TimeSpan[] fpsData;

        public ManaSceneNode Cursor { get; private set; }

        public VoxelEditorGameMode()
        {

        }

        ManaVoxelShape voxel;

        public override void Load()
        {
            base.Load();

            Context.Window.FileDrop += Window_FileDrop;

            ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

            var xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, 64, 0, 64);
            var yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, 64, 0, 64);
            var zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, 64, 0, 64);
            
            Context.Physics.AddBoxBody(xzPlane, new Vector3(64.0f, 0.1f, 64.0f), new Vector3(32, 0, 32));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(64.0f, 64.0f, 0.1f), new Vector3(32, 32, 0));
            Context.Physics.AddBoxBody(xzPlane, new Vector3(0.1f, 64.0f, 64.0f), new Vector3(0, 32, 32));


            Cursor = shapes.AddSphere(new Vector3(64, 3, 64), Colors.White, 1.0f);
            if (Cursor.Components[0] is MeshRendererComponent renderer)
            {
                renderer.Material.Shader = new ManaLiquidShader(Context);
            }


            MagicaVoxLoader vox = new MagicaVoxLoader();
            vox.Load();

            var voxelsNode = shapes.AddVoxels(Vector3.Zero);
            var voxelShapes = voxelsNode.FindShapes<ManaVoxelShape>();
            Debug.Assert(voxelShapes.Count == 1);

            foreach (var voxel in vox.Voxels)
            {
                voxelShapes[0].AddVoxel(voxel.Position + Vector3.One * 0.5f, voxel.Color);
            }

            voxelShapes[0].CommitData();
            voxel = voxelShapes[0];
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
                var fileWorld = Context.World;
                var model = fileWorld.LoadFromGltf(file);
                model.Position = lastImportPos;
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
            Context.Physics.Simulation.RayCast(ray.Origin, ray.Direction, 10000.0f, ref hitHandler);

            RayHit hit = new RayHit();
            if (hitHandler.ClosestHit.HasValue)
            {
                hit = hitHandler.ClosestHit.Value;
                var pos = hit.Position;
                pos *= 1;
                pos.X = MathF.Round(pos.X);
                pos.Y = MathF.Round(pos.Y);
                pos.Z = MathF.Round(pos.Z);
                pos *= 1f;
                hit.Position = pos;
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

            RayHit hit = GetProjectedMouseHit();
            if (hit.Hit)
            {
                var pos = hit.Position;
                pos *= 1;
                pos.X = MathF.Round(pos.X);
                pos.Y = MathF.Round(pos.Y);
                pos.Z = MathF.Round(pos.Z);
                pos *= 1f;

                Cursor.Position = pos;

                if (MouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left))
                {
                    
                    // Too slow, needs to be on press
                    //voxel.AddVoxel(pos, Colors.Magenta);
                    //voxel.CommitData();
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
