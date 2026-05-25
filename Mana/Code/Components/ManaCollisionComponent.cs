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
        public IConvexShape Shape { get; set; }
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
            Owner.Position = pose.Position - parentWorldPosition - Position;
        }
    }
}
