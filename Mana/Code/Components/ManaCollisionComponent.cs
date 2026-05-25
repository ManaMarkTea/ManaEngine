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
    public class ManaCollisionComponent : ManaComponent
    {
        public IConvexShape? Shape { get; set; }
        public TypedIndex ShapeIndex { get; set; }
        public BodyHandle Body { get; set; }
        public bool SyncNodeToPhysics { get; set; } = true;

        public ManaCollisionComponent(ManaSceneNode owner) : base(owner)
        {

        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var body = Context.Physics.Simulation.Bodies[Body];
            ref var pose = ref body.Pose;

            if (SyncNodeToPhysics)
            {
                var worldPosition = Owner.WorldTransform.Translation + Position;
                pose.Position = worldPosition;
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
