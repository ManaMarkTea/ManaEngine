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

        public ManaCollisionComponent(ManaSceneNode owner) : base(owner)
        {

        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var pos = Owner.WorldTransform.Translation + Position;

            var body = Context.Physics.Simulation.Bodies[Body];
            ref var pose = ref body.Pose;
            pose.Position = pos;
        }
    }
}
