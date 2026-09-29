using BepuPhysics;
using BepuPhysics.Collidables;
using Mana.Code.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Components
{
    // Created through ManaPhysics, which also registers its body and shape.
    [EditorComponent(CanAdd = false)]
    public class ManaCollisionComponent : ManaComponent
    {
        // IShape rather than IConvexShape: a triangle mesh (a grid map tile) is not convex.
        public IShape? Shape { get; set; }
        public TypedIndex ShapeIndex { get; set; }
        public BodyHandle Body { get; set; }
        public bool SyncNodeToPhysics { get; set; } = true;
        public bool ShowDebug { get; set; }

        public ManaCollisionComponent(ManaSceneNode owner) : base(owner)
        {

        }

        public static RigidPose CreateBodyPose(ManaSceneNode owner, Vector3 localOffset)
        {
            if (!Matrix4x4.Decompose(owner.WorldTransform, out _, out var orientation, out _))
            {
                orientation = Quaternion.Identity;
            }

            return new RigidPose(Vector3.Transform(localOffset, owner.WorldTransform), orientation);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var body = Context.Physics.Simulation.Bodies[Body];
            ref var pose = ref body.Pose;

            if (SyncNodeToPhysics)
            {
                var ownerPose = CreateBodyPose(Owner, Position);
                pose.Position = ownerPose.Position;
                pose.Orientation = ownerPose.Orientation;
                return;
            }

            var parentWorldPosition = Owner.Parent?.WorldTransform.Translation ?? Vector3.Zero;
            var localPosition = pose.Position - parentWorldPosition - Position;
            Owner.Position = localPosition;

            var localTransform = Matrix4x4.Identity;
            localTransform *= Matrix4x4.CreateScale(Owner.Scale);
            localTransform *= Matrix4x4.CreateFromQuaternion(pose.Orientation);
            localTransform *= Matrix4x4.CreateTranslation(localPosition);

            Owner.LocalTransform = localTransform;
            Owner.WorldTransform = Owner.Parent != null ? localTransform * Owner.Parent.WorldTransform : localTransform;
        }
    }
}
