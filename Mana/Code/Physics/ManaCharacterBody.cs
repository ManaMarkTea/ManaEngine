using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using System.Runtime.CompilerServices;

namespace Mana.Code
{
    // Closest-hit ray probe that ignores one body. Used for the character's grounded test, where the
    // ray starts inside its own capsule and would otherwise self-hit every frame, and for the spring
    // arm, which must not collide with the character it is following.
    public struct ExcludeBodyRayHandler : IRayHitHandler
    {
        private readonly int excludedBodyHandle;

        public bool Hit;
        public float Distance;
        public Vector3 Normal;
        public CollidableReference Collidable;

        public ExcludeBodyRayHandler(BodyHandle excluded)
        {
            excludedBodyHandle = excluded.Value;
            Hit = false;
            Distance = float.MaxValue;
            Normal = Vector3.UnitY;
            Collidable = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable)
        {
            // BodyHandle is only meaningful for non-static collidables; reading it on a static is
            // undefined, hence the mobility check first.
            return collidable.Mobility == CollidableMobility.Static || collidable.BodyHandle.Value != excludedBodyHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return AllowTest(collidable);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t > maximumT || (Hit && t >= Distance))
            {
                return;
            }

            Hit = true;
            Distance = t;
            Normal = normal;
            Collidable = collidable;
        }
    }

    // A dynamic Bepu capsule with its rotation locked, driven by setting velocity every frame. This is
    // the standard shape of a physics character controller: real collision response against walls and
    // platforms from the solver, but full authority over movement and jump arcs, which a purely
    // force-driven body would not give.
    //
    // The capsule never sleeps (negative sleep threshold). A character standing still would otherwise
    // be deactivated by the solver and stop responding to the next input.
    public sealed class ManaCharacterBody : ManaObject, IDisposable
    {
        // How far below the capsule to keep probing for ground, and how steep a surface can be before
        // it stops counting as a floor. The probe has to exceed the contact speculative margin or the
        // character flickers between grounded and airborne while resting.
        private const float GroundProbeDistance = 0.25f;
        private const float MinimumGroundNormalY = 0.5f;

        private bool disposed;

        public BodyHandle Handle { get; private set; }
        public float Radius { get; }
        public float Height { get; }
        public float HalfHeight => Height * 0.5f;

        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;
        public Mana.Code.Nodes.ManaSceneNode? GroundNode { get; private set; }

        // Distance from the capsule's bottom to whatever is under it, or float.MaxValue when the probe
        // finds nothing. Useful for landing prediction and for debugging a stuck character.
        public float GroundDistance { get; private set; } = float.MaxValue;

        private ManaCharacterBody(ManaContext context, BodyHandle handle, float radius, float height) : base(context)
        {
            Handle = handle;
            Radius = radius;
            Height = height;
            Name = "Character Body";
        }

        public static ManaCharacterBody Create(ManaContext context, Vector3 footPosition, float radius, float height, float mass = 1.0f)
        {
            // Bepu's Capsule length is the cylindrical section only, so the caps have to be subtracted
            // out of the requested total height.
            var cylinderLength = MathF.Max(height - (radius * 2.0f), 0.01f);
            var capsule = new Capsule(radius, cylinderLength);
            var shapeIndex = context.Physics.Simulation.Shapes.Add(capsule);

            var inertia = capsule.ComputeInertia(mass);

            // Zeroing the inverse inertia tensor gives infinite rotational inertia: the capsule keeps
            // its mass for collision response but cannot be tipped over or spun by a contact, which is
            // what keeps a character upright without a constraint.
            inertia.InverseInertiaTensor = default;

            var pose = new RigidPose(footPosition + new Vector3(0.0f, height * 0.5f, 0.0f));
            var description = BodyDescription.CreateDynamic(
                pose,
                inertia,
                new CollidableDescription(shapeIndex),
                new BodyActivityDescription(-1.0f));

            var handle = context.Physics.Simulation.Bodies.Add(description);
            context.Physics.Simulation.Awakener.AwakenBody(handle);

            // Tells the narrow phase this is a character, so its wall contacts get zero friction.
            context.Physics.Characters.Register(handle);

            return new ManaCharacterBody(context, handle, radius, height);
        }

        public bool Exists => !disposed && Context.Physics.Simulation.Bodies.BodyExists(Handle);

        public Vector3 FootPosition => Exists
            ? Context.Physics.Simulation.Bodies[Handle].Pose.Position - new Vector3(0.0f, HalfHeight, 0.0f)
            : Vector3.Zero;

        public Vector3 Velocity
        {
            get => Exists ? Context.Physics.Simulation.Bodies[Handle].Velocity.Linear : Vector3.Zero;
            set
            {
                if (!Exists)
                {
                    return;
                }

                var body = Context.Physics.Simulation.Bodies[Handle];
                var velocity = body.Velocity;
                velocity.Linear = value;
                body.Velocity = velocity;
                body.Awake = true;

                // Walking, when driven sideways: its contacts stop gripping (see DemoNarrowPhaseCallbacks).
                Context.Physics.Characters.SetMoving(Handle, value.X * value.X + value.Z * value.Z > 1e-4f);
            }
        }

        /// <summary>
        /// Moves the capsule without touching its velocity -- what riding a moving platform needs, where
        /// <see cref="Teleport"/> would also stop a jump or a fall dead.
        /// </summary>
        public void Shift(Vector3 delta)
        {
            if (!Exists)
            {
                return;
            }

            var body = Context.Physics.Simulation.Bodies[Handle];
            var pose = body.Pose;
            pose.Position += delta;
            body.Pose = pose;
            body.Awake = true;
        }

        public void Teleport(Vector3 footPosition)
        {
            if (!Exists)
            {
                return;
            }

            var body = Context.Physics.Simulation.Bodies[Handle];
            var pose = body.Pose;
            pose.Position = footPosition + new Vector3(0.0f, HalfHeight, 0.0f);
            pose.Orientation = Quaternion.Identity;
            body.Pose = pose;

            var velocity = body.Velocity;
            velocity.Linear = Vector3.Zero;
            velocity.Angular = Vector3.Zero;
            body.Velocity = velocity;
            body.Awake = true;
        }

        // Re-probes the ground. Call once per frame after the simulation has stepped and before the
        // movement code reads IsGrounded.
        public void RefreshGroundState()
        {
            IsGrounded = false;
            GroundNode = null;
            GroundNormal = Vector3.UnitY;
            GroundDistance = float.MaxValue;

            if (!Exists)
            {
                return;
            }

            var center = Context.Physics.Simulation.Bodies[Handle].Pose.Position;
            var maximumT = HalfHeight + GroundProbeDistance;

            var handler = new ExcludeBodyRayHandler(Handle);
            Context.Physics.Simulation.RayCast(center, -Vector3.UnitY, maximumT, Context.Physics.BufferPool, ref handler, 0);

            if (!handler.Hit)
            {
                return;
            }

            GroundDistance = MathF.Max(handler.Distance - HalfHeight, 0.0f);

            // A wall's normal points sideways; treating one as ground would let a character jump
            // indefinitely by hugging it.
            if (handler.Normal.Y < MinimumGroundNormalY)
            {
                return;
            }

            IsGrounded = GroundDistance <= GroundProbeDistance;
            GroundNormal = handler.Normal;
            if (IsGrounded && handler.Collidable.Mobility != CollidableMobility.Static
                && Context.Physics.PhysicsMap.TryGetValue(handler.Collidable.BodyHandle, out var collider))
            {
                GroundNode = collider.Owner;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Context.Physics.Characters.Unregister(Handle);
            if (Context.Physics.Simulation.Bodies.BodyExists(Handle))
            {
                Context.Physics.Simulation.Bodies.Remove(Handle);
            }
        }
    }
}
