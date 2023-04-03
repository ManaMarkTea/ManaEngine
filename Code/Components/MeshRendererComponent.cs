using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class MeshRendererComponent : ManaComponent
    {
        public ManaMesh Mesh { get; set; }

        public ManaMaterial Material { get; set; }

        public MeshRendererComponent(ManaSceneNode owner, ManaMesh mesh) : base(owner)
        {            
            Mesh = mesh;
        }

        public override void DrawComponent()
        {
            base.DrawComponent();
            if (Mesh == null)
            {
                return;
            }

            Mesh.Draw(Owner);
        }
    }
}
