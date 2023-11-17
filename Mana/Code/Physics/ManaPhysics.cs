using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using Mana.Code.Components;
using Mana.Code.Nodes;

namespace Mana.Code
{
    public class ManaPhysics : ManaObject
    {
        public Simulation Simulation { get; protected set; }
        public BufferPool BufferPool { get; private set; }
        public ThreadDispatcher ThreadDispatcher { get; private set; }

        public Dictionary<BodyHandle, ManaCollisionComponent> PhysicsMap = new ();

        public ManaPhysics(ManaContext context) : base(context)
        {            
            BufferPool = new BufferPool();
            
            var targetThreadCount = int.Max(1, Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount - 1);
            ThreadDispatcher = new ThreadDispatcher(targetThreadCount);
            
            Simulation = Simulation.Create(BufferPool, new NoCollisionCallbacks(), new DemoPoseIntegratorCallbacks(new Vector3(0, -10, 0)), new SolveDescription(8, 1));

        }

        public void AddBoxBody(ManaSceneNode node, Vector3 size, Vector3 offset)
        {
            var worldPosition = node.WorldTransform.Translation;
            Box boxShape = new Box(size.X, size.Y, size.Z);
            TypedIndex boxIndex = Simulation.Shapes.Add(boxShape);
            BodyDescription boxBody = BodyDescription.CreateKinematic(new RigidPose(worldPosition + offset), new CollidableDescription(boxIndex), new BodyActivityDescription());
            BodyHandle handle = Simulation.Bodies.Add(boxBody);

            ManaCollisionComponent collisionComponent = new ManaCollisionComponent(node);
            collisionComponent.Position = offset;
            collisionComponent.Shape = boxShape;
            collisionComponent.ShapeIndex = boxIndex;
            collisionComponent.Body = handle;
            PhysicsMap[handle] = collisionComponent;
            node.Components.Add(collisionComponent);
        }

        public void Update()
        {
            var TimestepDuration = ManaTime.GlobalTime.DeltaTime;
            Simulation.Timestep(TimestepDuration.Milliseconds, ThreadDispatcher);
        }

        public void LoadSampleWorld()
        {

            const int pointCount = 16;
            var points = new QuickList<Vector3>(pointCount, BufferPool);
            var random = new Random(5);
            for (int i = 0; i < pointCount; ++i)
            {
                points.AllocateUnsafely() = new Vector3(1 * random.NextSingle(), 1 * random.NextSingle(), 1 * random.NextSingle());
            }


            var xzBox = new Box(64.0f, 0.1f, 64.0f);
            var yxBox = new Box(64.0f, 64.0f, 0.1f);
            var zyBox = new Box(0.1f, 64.0f, 64.0f);

            var xzBoxIndex = Simulation.Shapes.Add(xzBox);
            var yxBoxIndex = Simulation.Shapes.Add(yxBox);
            var zyBoxIndex = Simulation.Shapes.Add(zyBox);

            var xzBody = BodyDescription.CreateKinematic(new RigidPose(new Vector3(32, 00, 32)), new CollidableDescription(xzBoxIndex), new BodyActivityDescription());
            var yxBody = BodyDescription.CreateKinematic(new RigidPose(new Vector3(32, 32, 00)), new CollidableDescription(yxBoxIndex), new BodyActivityDescription());
            var zyBody = BodyDescription.CreateKinematic(new RigidPose(new Vector3(00, 32, 32)), new CollidableDescription(zyBoxIndex), new BodyActivityDescription());

            Simulation.Bodies.Add(xzBody);
            Simulation.Bodies.Add(yxBody);
            Simulation.Bodies.Add(zyBody);
        }

    }
}
