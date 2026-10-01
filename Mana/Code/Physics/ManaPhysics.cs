using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using Mana.Code.Components;
using Mana.Code.Nodes;
using System.Linq;

namespace Mana.Code
{
    public class ManaPhysics : ManaObject
    {
        public Simulation Simulation { get; protected set; }
        public BufferPool BufferPool { get; private set; }
        public ThreadDispatcher ThreadDispatcher { get; private set; }

        public Dictionary<BodyHandle, ManaCollisionComponent> PhysicsMap = new ();
        public List<BodyHandle> DirectBodies = new ();

        // The value handed to the pose integrator. Exposed because a character controller has to know
        // it to shape a jump arc -- Mario-style falling applies extra gravity on top of this.
        public Vector3 Gravity { get; }

        // Shared with the narrow phase so character capsules can get their own contact material.
        public ManaCharacterRegistry Characters { get; } = new();

        public ManaPhysics(ManaContext context) : base(context)
        {
            BufferPool = new BufferPool();
            Gravity = new Vector3(0, -64, 0);
            
            var targetThreadCount = int.Max(1, Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount - 1);
            ThreadDispatcher = new ThreadDispatcher(targetThreadCount);
            
            Simulation = Simulation.Create(
                BufferPool,
                new DemoNarrowPhaseCallbacks(new SpringSettings(30, 1), maximumRecoveryVelocity: 2f, frictionCoefficient: 1f, characters: Characters),
                new DemoPoseIntegratorCallbacks(Gravity, linearDamping: 0.03f, angularDamping: 0.12f),
                new SolveDescription(8, 1));

        }

        public ManaCollisionComponent AddBoxBody(ManaSceneNode node, Vector3 size, Vector3 offset)
        {
            Box boxShape = new Box(size.X, size.Y, size.Z);
            TypedIndex boxIndex = Simulation.Shapes.Add(boxShape);
            BodyDescription boxBody = BodyDescription.CreateKinematic(
                ManaCollisionComponent.CreateBodyPose(node, offset),
                new CollidableDescription(boxIndex),
                new BodyActivityDescription());
            BodyHandle handle = Simulation.Bodies.Add(boxBody);

            ManaCollisionComponent collisionComponent = new ManaCollisionComponent(node);
            collisionComponent.Position = offset;
            collisionComponent.Shape = boxShape;
            collisionComponent.ShapeIndex = boxIndex;
            collisionComponent.Body = handle;
            PhysicsMap[handle] = collisionComponent;
            lock (Context.World.SceneLock)
            {
                node.Components.Add(collisionComponent);
            }

            return collisionComponent;
        }

        public ManaCollisionComponent AddConvexHullBody(
            ManaSceneNode node,
            ReadOnlySpan<Vector3> points,
            Vector3 offset)
        {
            Matrix4x4.Decompose(node.WorldTransform, out var worldScale, out _, out _);
            var scaledOffset = offset * worldScale;
            var hullPoints = points.ToArray();
            for (var i = 0; i < hullPoints.Length; i++)
            {
                hullPoints[i] += scaledOffset;
            }

            var hullShape = new ConvexHull(hullPoints, BufferPool, out var hullCenter);
            // Hull points already carry world scale; Position is owner-local and the body pose applies scale.
            var localCenter = new Vector3(
                worldScale.X == 0 ? 0 : hullCenter.X / worldScale.X,
                worldScale.Y == 0 ? 0 : hullCenter.Y / worldScale.Y,
                worldScale.Z == 0 ? 0 : hullCenter.Z / worldScale.Z);
            var hullIndex = Simulation.Shapes.Add(hullShape);
            var hullBody = BodyDescription.CreateKinematic(
                ManaCollisionComponent.CreateBodyPose(node, localCenter),
                new CollidableDescription(hullIndex),
                new BodyActivityDescription());
            var handle = Simulation.Bodies.Add(hullBody);

            var collisionComponent = new ManaCollisionComponent(node)
            {
                Position = localCenter,
                Shape = hullShape,
                ShapeIndex = hullIndex,
                Body = handle
            };
            PhysicsMap[handle] = collisionComponent;
            lock (Context.World.SceneLock)
            {
                node.Components.Add(collisionComponent);
            }

            return collisionComponent;
        }

        /// <summary>
        /// A kinematic triangle-mesh collider, for geometry no box or hull fits -- a corridor piece, a road
        /// with a kerb. <paramref name="triangles"/> holds three vertices per triangle in the node's own
        /// space, scale already applied: the body takes the node's position and rotation only.
        /// Kinematic rather than static so <c>ManaCharacterBody.GroundNode</c> can still say what is underfoot.
        /// </summary>
        public ManaCollisionComponent AddTriangleMeshBody(ManaSceneNode node, ReadOnlySpan<Vector3> triangles)
        {
            var count = triangles.Length / 3;
            if (count == 0)
            {
                throw new ArgumentException("A triangle mesh collider needs at least one triangle.", nameof(triangles));
            }

            BufferPool.Take<Triangle>(count, out var buffer);
            for (var i = 0; i < count; i++)
            {
                // Bepu's triangles are one sided and solid where (b - a) x (c - a) points away, the opposite of
                // glTF's counter-clockwise front faces -- so b and c swap.
                buffer[i] = new Triangle(triangles[i * 3], triangles[i * 3 + 2], triangles[i * 3 + 1]);
            }

            var meshShape = new BepuPhysics.Collidables.Mesh(buffer, Vector3.One, BufferPool);
            var meshIndex = Simulation.Shapes.Add(meshShape);
            var meshBody = BodyDescription.CreateKinematic(
                ManaCollisionComponent.CreateBodyPose(node, Vector3.Zero),
                new CollidableDescription(meshIndex),
                new BodyActivityDescription());
            var handle = Simulation.Bodies.Add(meshBody);

            var collisionComponent = new ManaCollisionComponent(node)
            {
                Shape = meshShape,
                ShapeIndex = meshIndex,
                Body = handle
            };
            PhysicsMap[handle] = collisionComponent;
            lock (Context.World.SceneLock)
            {
                node.Components.Add(collisionComponent);
            }

            return collisionComponent;
        }

        public void AddDynamicBoxBody(ManaSceneNode node, Vector3 size, Vector3 offset, float mass = 1.0f, Vector3? initialLinearVelocity = null)
        {
            Box boxShape = new Box(size.X, size.Y, size.Z);
            TypedIndex boxIndex = Simulation.Shapes.Add(boxShape);

            var boxInertia = boxShape.ComputeInertia(mass);
            BodyDescription boxBody = BodyDescription.CreateDynamic(
                ManaCollisionComponent.CreateBodyPose(node, offset),
                boxInertia,
                new CollidableDescription(boxIndex),
                new BodyActivityDescription());
            BodyHandle handle = Simulation.Bodies.Add(boxBody);

            var bodyReference = Simulation.Bodies[handle];
            bodyReference.Awake = true;
            var bodyVelocity = bodyReference.Velocity;
            bodyVelocity.Linear += new Vector3(0, -0.5f, 0);
            if (initialLinearVelocity.HasValue)
            {
                bodyVelocity.Linear += initialLinearVelocity.Value;
            }
            bodyReference.Velocity = bodyVelocity;

            Simulation.Awakener.AwakenBody(handle);

            ManaCollisionComponent collisionComponent = new ManaCollisionComponent(node);
            collisionComponent.Position = offset;
            collisionComponent.Shape = boxShape;
            collisionComponent.ShapeIndex = boxIndex;
            collisionComponent.Body = handle;
            collisionComponent.SyncNodeToPhysics = false;
            PhysicsMap[handle] = collisionComponent;
            lock (Context.World.SceneLock)
            {
                node.Components.Add(collisionComponent);
            }
        }

        public void RemoveBody(BodyHandle handle)
        {
            ManaCollisionComponent? removedCollision = null;
            if (PhysicsMap.Remove(handle, out var collisionComponent))
            {
                removedCollision = collisionComponent;
                lock (Context.World.SceneLock)
                {
                    collisionComponent.Owner.Components.Remove(collisionComponent);
                }
            }

            DirectBodies.Remove(handle);

            if (Simulation.Bodies.BodyExists(handle))
            {
                Simulation.Bodies.Remove(handle);
            }

            if (removedCollision != null && removedCollision.ShapeIndex.Exists)
            {
                Simulation.Shapes.RemoveAndDispose(removedCollision.ShapeIndex, BufferPool);
            }
        }

        public BodyHandle AddDynamicBoxBodyDirect(Vector3 position, Vector3 size, float mass = 1.0f, Vector3? initialLinearVelocity = null)
        {
            Box boxShape = new Box(size.X, size.Y, size.Z);
            TypedIndex boxIndex = Simulation.Shapes.Add(boxShape);
            var boxInertia = boxShape.ComputeInertia(mass);
            BodyDescription boxBody = BodyDescription.CreateDynamic(
                new RigidPose(position),
                boxInertia,
                new CollidableDescription(boxIndex),
                new BodyActivityDescription());
            BodyHandle handle = Simulation.Bodies.Add(boxBody);

            var bodyReference = Simulation.Bodies[handle];
            bodyReference.Awake = true;
            var bodyVelocity = bodyReference.Velocity;
            bodyVelocity.Linear += new Vector3(0, -0.5f, 0);
            if (initialLinearVelocity.HasValue)
                bodyVelocity.Linear += initialLinearVelocity.Value;
            bodyReference.Velocity = bodyVelocity;

            Simulation.Awakener.AwakenBody(handle);
            DirectBodies.Add(handle);
            return handle;
        }

        public int ApplyExplosion(Vector3 center, float radius, float strength, IEnumerable<BodyHandle>? handles = null)
        {
            int affectedBodyCount = 0;
            float radiusSquared = radius * radius;

            var allHandles = handles ?? PhysicsMap.Keys.Concat(DirectBodies);
            foreach (var handle in allHandles)
            {
                if (!Simulation.Bodies.BodyExists(handle))
                {
                    continue;
                }

                var bodyReference = Simulation.Bodies[handle];
                if (bodyReference.Kinematic)
                {
                    continue;
                }

                var offset = bodyReference.Pose.Position - center;
                float distanceSquared = offset.LengthSquared();
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = MathF.Sqrt(MathF.Max(distanceSquared, 1e-6f));
                var direction = distance > 1e-5f ? offset / distance : Vector3.UnitY;
                float falloff = 1.0f - MathF.Min(distance / radius, 1.0f);

                var velocity = bodyReference.Velocity;
                velocity.Linear += direction * (strength * falloff);
                bodyReference.Velocity = velocity;
                bodyReference.Awake = true;

                affectedBodyCount++;
            }

            return affectedBodyCount;
        }

        public void Update()
        {
            var timestepDuration = ManaTime.GlobalTime.DeltaTime;
            var dtSeconds = (float)timestepDuration.TotalSeconds;

            if (!float.IsFinite(dtSeconds) || dtSeconds <= 0)
            {
                dtSeconds = 1.0f / 60.0f;
            }
            else
            {
                dtSeconds = Math.Clamp(dtSeconds, 1.0f / 240.0f, 1.0f / 30.0f);
            }

            Simulation.Timestep(dtSeconds, ThreadDispatcher);
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
