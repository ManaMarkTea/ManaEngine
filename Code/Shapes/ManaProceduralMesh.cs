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
            Materials = new ManaMaterial[] { GenerateMaterial() };
        }

        public virtual ManaMaterial GenerateMaterial()
        {
            var foundMaterial = new ManaMaterial(Context);
            foundMaterial.Albedo.Value = DefaultVertexColor;
            return foundMaterial;
        }

        protected virtual void GenerateData() { }

    }
}
