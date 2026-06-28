namespace Mana.Code.Shapes
{
    public class ManaIcosahedron : ManaProceduralMesh
    {
        public float Radius { get; set; }

        public ManaIcosahedron(ManaContext context, Vector4 color, float radius = 0.5f) : base(context)
        {
            Radius = radius;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var t = (1.0f + MathF.Sqrt(5.0f)) * 0.5f;
            var vertices = new[]
            {
                new Vector3(-1.0f, t, 0.0f),
                new Vector3(1.0f, t, 0.0f),
                new Vector3(-1.0f, -t, 0.0f),
                new Vector3(1.0f, -t, 0.0f),
                new Vector3(0.0f, -1.0f, t),
                new Vector3(0.0f, 1.0f, t),
                new Vector3(0.0f, -1.0f, -t),
                new Vector3(0.0f, 1.0f, -t),
                new Vector3(t, 0.0f, -1.0f),
                new Vector3(t, 0.0f, 1.0f),
                new Vector3(-t, 0.0f, -1.0f),
                new Vector3(-t, 0.0f, 1.0f),
            };

            for (int i = 0; i < vertices.Length; ++i)
            {
                vertices[i] = vertices[i].Normalized() * Radius;
            }

            var faces = new[]
            {
                new TriangleIndices(0, 11, 5),
                new TriangleIndices(0, 5, 1),
                new TriangleIndices(0, 1, 7),
                new TriangleIndices(0, 7, 10),
                new TriangleIndices(0, 10, 11),
                new TriangleIndices(1, 5, 9),
                new TriangleIndices(5, 11, 4),
                new TriangleIndices(11, 10, 2),
                new TriangleIndices(10, 7, 6),
                new TriangleIndices(7, 1, 8),
                new TriangleIndices(3, 9, 4),
                new TriangleIndices(3, 4, 2),
                new TriangleIndices(3, 2, 6),
                new TriangleIndices(3, 6, 8),
                new TriangleIndices(3, 8, 9),
                new TriangleIndices(4, 9, 5),
                new TriangleIndices(2, 4, 11),
                new TriangleIndices(6, 2, 10),
                new TriangleIndices(8, 6, 7),
                new TriangleIndices(9, 8, 1),
            };

            foreach (var face in faces)
            {
                AddFace(vertices[face.v1], vertices[face.v2], vertices[face.v3]);
            }

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
                VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
            }
        }

        private void AddFace(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var center = (a + b + c) / 3.0f;
            if (Vector3.Dot(normal, center) < 0.0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }

            var vertices = DynamicGeometryContainer.AddVertexRange(
                CreateVertex(a, normal, new Vector2(0.0f, 0.0f)),
                CreateVertex(b, normal, new Vector2(1.0f, 0.0f)),
                CreateVertex(c, normal, new Vector2(0.5f, 1.0f)));

            DynamicGeometryContainer.CreateFacesFromPoints(vertices.ToList());
        }

        private ManaGeometryVertex CreateVertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            return new ManaGeometryVertex
            {
                Position = position,
                Normal = normal,
                Color = DefaultVertexColor,
                TexCoord0 = texCoord,
                TexCoord1 = texCoord
            };
        }
    }
}
