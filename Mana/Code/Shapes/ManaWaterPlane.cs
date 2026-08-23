using Mana.Code.Materials;

namespace Mana.Code.Shapes
{
    // A subdivided XZ grid (unlike ManaQuad, which is a single 4-vertex quad) so the water
    // shader has enough vertices to displace geometrically for waves/ripples rather than
    // only shading a flat surface.
    public class ManaWaterPlane : ManaProceduralMesh
    {
        public Vector2 Size { get; }
        public int Resolution { get; }

        public ManaWaterPlane(ManaContext context, Vector4 color, Vector2 size, int resolution = 48) : base(context)
        {
            Size = size;
            Resolution = Math.Max(1, resolution);
            DefaultVertexColor = color;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context);
            material.Name = "Water Material";
            material.Shader = new ManaWaterShader(Context);
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var halfSize = Size * 0.5f;
            MinSize = new Vector3(-halfSize.X, 0.0f, -halfSize.Y);
            MaxSize = new Vector3(halfSize.X, 0.0f, halfSize.Y);

            var verts = new ManaGeometryVertex[Resolution + 1, Resolution + 1];
            for (int z = 0; z <= Resolution; z++)
            {
                var v = (float)z / Resolution;
                for (int x = 0; x <= Resolution; x++)
                {
                    var u = (float)x / Resolution;
                    var position = new Vector3(-halfSize.X + u * Size.X, 0.0f, -halfSize.Y + v * Size.Y);
                    var vert = DynamicGeometryContainer.AddPoint(position, Vector3.UnitY);
                    vert.TexCoord0 = new Vector2(u, v);
                    vert.Color = DefaultVertexColor;
                    verts[x, z] = vert;
                }
            }

            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    var a = verts[x, z];
                    var b = verts[x + 1, z];
                    var c = verts[x + 1, z + 1];
                    var d = verts[x, z + 1];
                    DynamicGeometryContainer.CreateFacesFromQuad(a, b, c, d);
                }
            }

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
#if !MANA_BROWSER
                VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
#endif
            }
        }
    }
}
