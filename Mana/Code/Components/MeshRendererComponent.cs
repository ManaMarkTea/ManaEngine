using System.Runtime.CompilerServices;
using Mana.Code.Nodes;

namespace Mana
{
    public class MeshRendererComponent : ManaComponent
    {
        public ManaMesh Mesh { get; set; }
        public bool DrawAfterScene { get; set; }
        public bool OutlineEnabled { get; set; }
        public Vector4 OutlineColor { get; set; } = new(1.0f, 0.82f, 0.18f, 1.0f);
        public float OutlineWidth { get; set; } = 2.0f;

        // Owner/this hash codes are stable for the object's lifetime, so this key -- previously
        // re-formatted with string interpolation on every ManaRenderMeshInstance built for this
        // renderer, every frame -- only needs computing once. See ManaSceneNode.CollectRenderMeshInstances.
        private string? _instanceKey;
        public string InstanceKey => _instanceKey ??= $"{RuntimeHelpers.GetHashCode(Owner):x8}:{RuntimeHelpers.GetHashCode(this):x8}";

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

            if (DrawAfterScene)
            {
                Context.World.PostDrawFutures.Add(() => Mesh.Draw(Owner, this));
                return;
            }

            Mesh.Draw(Owner, this);
        }
    }
}
