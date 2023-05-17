using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace Mana
{
    public class MeshRendererComponent : ManaComponent
    {
        public ManaMesh Mesh { get; set; }

        private ManaMaterial? material;
        public ManaMaterial Material
        {
            get
            {
                if (dynamicMaterial != null)
                {
                    return dynamicMaterial;
                }

                return material;
            }
            set
            {
                material = value;
            }
        }


        private ManaMaterial? dynamicMaterial = null;
        public ManaMaterial DynamicMaterial
        {
            get
            {
                if (dynamicMaterial == null)
                {
                    dynamicMaterial = Material.Clone() as ManaMaterial;
                    if (dynamicMaterial == null)
                    {
                        dynamicMaterial = new ManaMaterial(Context);                        
                    }

                    dynamicMaterial.Name = Material.Name + "_Clone";
                }

                return dynamicMaterial;
            }
        }

        public MeshRendererComponent(ManaSceneNode owner, ManaMesh mesh) : base(owner)
        {            
            Mesh = mesh;

            if (mesh.Materials.Length == 0 || mesh.Materials[0] == null)
            {
                Material = ManaMaterial.DefaultMaterial;
            }
            else
            {
                Material = mesh.Materials[0];
            }
            
        }

        public override void DrawComponent()
        {
            base.DrawComponent();
            if (Mesh == null)
            {
                return;
            }

            Mesh.Draw(Owner, this);
        }
    }
}
