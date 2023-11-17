using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Nodes
{
    public class ManaVoxelsNode : ManaSceneNode
    {
        ManaVoxelShape shape;

        public ManaVoxelsNode(ManaContext context, Matrix4x4 localTransform) : base(context, localTransform)
        {
            shape = new ManaVoxelShape(Context);
            shape.OnVoxelAdded += Shape_OnVoxelAdded;
            Components.Add(new MeshRendererComponent(this, shape));
        }

        private void Shape_OnVoxelAdded(ManaVoxelData obj)
        {
            Context.Physics.AddBoxBody(this, Vector3.One, obj.Position);
        }
    }
}
