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

        /// <summary>
        /// True: sized to fit the model's visual bounds and meant to keep fitting them -- saved as "use visual
        /// bounds" and refitted when the model changes size, as it does while its parts load. False: a size of its
        /// own, loaded from a level, saved as that size. Null: made in the editor, so a save checks whether it
        /// happens to fit the model.
        /// </summary>
        public bool? FollowsVisualBounds { get; set; }

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

            if (SyncNodeToPhysics)
            {
                var ownerPose = CreateBodyPose(Owner, Position);
                if (body.Pose.Position != ownerPose.Position || body.Pose.Orientation != ownerPose.Orientation)
                {
                    // A sleeping body's bounds are never refreshed: moved while asleep (a lift), it would stay
                    // solid where it dozed off while it is drawn somewhere else, and players fall through it.
                    body.Awake = true;
                    body.Pose.Position = ownerPose.Position;
                    body.Pose.Orientation = ownerPose.Orientation;
                }
                return;
            }

            ref var pose = ref body.Pose;

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
