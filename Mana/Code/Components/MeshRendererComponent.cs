using Mana.Code.Nodes;

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

                return material ?? ManaMaterial.GetDefaultMaterial(Context);
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

                    dynamicMaterial.Name = BuildDynamicMaterialName(Material.Name);
                }

                return dynamicMaterial;
            }
        }

        private string BuildDynamicMaterialName(string baseMaterialName)
        {
            var ownerName = string.IsNullOrWhiteSpace(Owner.Name)
                ? Owner.GetType().Name
                : Owner.Name;

            if (string.IsNullOrWhiteSpace(baseMaterialName) ||
                baseMaterialName == "PBR Material" ||
                baseMaterialName == "Default PBR Material")
            {
                return $"{ownerName} Material";
            }

            if (baseMaterialName.Contains(ownerName, StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMaterialName} Instance";
            }

            return $"{ownerName} {baseMaterialName}";
        }

        public MeshRendererComponent(ManaSceneNode owner, ManaMesh mesh) : base(owner)
        {
            Mesh = mesh;

            if (mesh.Materials.Length == 0 || mesh.Materials[0] == null)
            {
                Material = ManaMaterial.GetDefaultMaterial(owner.Context);
            }
            else
            {
                Material = mesh.Materials[0];
            }

        }

        public override void DrawComponent()
        {
            base.DrawComponent();
            if (Mesh == null || Mesh.VertexBufferObject == null || Mesh.VertexBufferObject.indexes.Length == 0)
            {
                return;
            }

            Mesh.Draw(Owner, this);
        }
    }
}
