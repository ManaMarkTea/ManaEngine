using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using ImGuiNET;
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

        public Simulation Simulation { get; protected set; }
        public BufferPool BufferPool { get; private set; }        
        public ThreadDispatcher ThreadDispatcher { get; private set; }

        public ManaSceneNode Cursor { get; private set; }

        public VoxelEditorGameMode()
        {

        }

        public override void Load()
        {
            base.Load();

            BufferPool = new BufferPool();
            var targetThreadCount = int.Max(1, Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount - 1);
            ThreadDispatcher = new ThreadDispatcher(targetThreadCount);

            Simulation = Simulation.Create(BufferPool, new NoCollisionCallbacks(), new DemoPoseIntegratorCallbacks(new Vector3(0, -10, 0)), new SolveDescription(8, 1));

            var sphere = new Sphere(10);            
            var capsule = new Capsule(0, 0.5f);
            var box = new Box(0.5f, 1.5f, 1f);
            var cylinder = new Cylinder(0.5f, 1);

            

            const int pointCount = 16;
            var points = new QuickList<Vector3>(pointCount, BufferPool);
            var random = new Random(5);
            for (int i = 0; i < pointCount; ++i)
            {
                points.AllocateUnsafely() = new Vector3(1 * random.NextSingle(), 1 * random.NextSingle(), 1 * random.NextSingle());
            }
            var hullShape = new ConvexHull(points, BufferPool, out _);
            var sphereIndex = Simulation.Shapes.Add(sphere);
            var capsuleIndex = Simulation.Shapes.Add(capsule);
            var boxIndex = Simulation.Shapes.Add(box);
            var cylinderIndex = Simulation.Shapes.Add(cylinder);
            var hullIndex = Simulation.Shapes.Add(hullShape);


            Simulation.Statics.Add(new StaticDescription(new Vector3(0,0,0), Quaternion.Identity, sphereIndex, ContinuousDetection.Continuous()));


            Context.Window.FileDrop += Window_FileDrop;

            ManaShapeFactory shapes = new ManaShapeFactory(Context, Context.World.DefaultScene);

            var xzPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.XZ, 0, 64, 0, 64);
            var yxPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.YX, 0, 64, 0, 64);
            var zyPlane = shapes.AddQuad(Vector3.Zero, Colors.White, ManaQuadPlanes.ZY, 0, 64, 0, 64);

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
            
        }

        Vector3 startImportPos = new Vector3(30.0f, 0, 20);
        Vector3 lastImportPos = new Vector3(30.0f, 0, 20);
        private void Window_FileDrop(string[] files)
        {
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



            BufferPool.Take(1, out Buffer<RayHit> hits);
            hits[0].T = float.MaxValue;
            hits[0].Hit = false;
        
            var hitHandler = new HitHandler() { Hits = hits };
            Simulation.RayCast(ray.Origin, ray.Direction, 10000.0f, ref hitHandler);

            if (hitHandler.IntersectionCount > 0)
            {
                Cursor.Position = hits[0].Position;
            }

            ImGui.InputFloat3("RAY", ref rayOrigin);
            ImGui.InputFloat3("DIR", ref rayDirection);

            
            ImGui.End();


            Simulation.Timestep(TimestepDuration, ThreadDispatcher);

        }

        public override void Render(double deltaTime)
        {
            base.Render(deltaTime);
        }

    }
}
