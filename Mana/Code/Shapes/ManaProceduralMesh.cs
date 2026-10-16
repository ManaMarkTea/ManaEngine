using Mana.Code.ManaShaders;
using Mana.Code.Materials;

namespace Mana.Code.Shapes
{
    public class ManaProceduralMesh : ManaMesh
    {
        protected struct TriangleIndices
        {
            public int v1;
            public int v2;
            public int v3;

            public TriangleIndices(int v1, int v2, int v3)
            {
                this.v1 = v1;
                this.v2 = v2;
                this.v3 = v3;
            }
        }

        public Vector4 DefaultVertexColor { get; set; }

        public ManaProceduralMesh(ManaContext context) : base(context)
        {            
        }

        public virtual ManaMaterial GenerateMaterial()
        {
            var foundMaterial = new ManaMaterial(Context);
            foundMaterial.Name = $"{GetShapeDisplayName()} Material";
            foundMaterial.Albedo.Value = DefaultVertexColor;

            // Says what it already is. A material with no shader renders as PBR anyway -- the backend
            // falls back to it by name -- but the material claimed to have none, so an inspector had no
            // reason to offer the PBR channels and there was nowhere to assign a texture on a plain cube.
            foundMaterial.Shader = new ManaPbrShader(Context);

            return foundMaterial;
        }

        protected string GetShapeDisplayName()
        {
            var name = GetType().Name;
            return name.StartsWith("Mana", StringComparison.Ordinal)
                ? name.Substring("Mana".Length)
                : name;
        }

        public void RebuildGeometry()
        {
            GenerateData();
        }

        protected virtual void GenerateData() 
        {
            DynamicGeometryContainer = new ManaDynamicGeometryContainer();

            if (Materials.Length == 0)
            {
                Materials = new ManaMaterial[] { GenerateMaterial() };
            }
        }

    }
}
