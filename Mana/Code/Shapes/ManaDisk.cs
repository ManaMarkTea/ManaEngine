namespace Mana.Code.Shapes
{
    /// <summary>A thin elliptical cylinder whose <see cref="Size"/> is its full visual bounding size.</summary>
    public sealed class ManaDisk : ManaProceduralMesh
    {
        private const int SegmentCount = 32;

        public Vector3 Size { get; }

        public ManaDisk(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            Size = Vector3.Max(size, new Vector3(0.001f));
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var half = Size * 0.5f;
            MinSize = -half;
            MaxSize = half;

            var topCenter = new Vector3(0.0f, half.Y, 0.0f);
            var bottomCenter = new Vector3(0.0f, -half.Y, 0.0f);
            for (var segment = 0; segment < SegmentCount; segment++)
            {
                var angleA = MathF.Tau * segment / SegmentCount;
                var angleB = MathF.Tau * (segment + 1) / SegmentCount;
                var topA = new Vector3(MathF.Cos(angleA) * half.X, half.Y, MathF.Sin(angleA) * half.Z);
                var topB = new Vector3(MathF.Cos(angleB) * half.X, half.Y, MathF.Sin(angleB) * half.Z);
                var bottomA = new Vector3(topA.X, -half.Y, topA.Z);
                var bottomB = new Vector3(topB.X, -half.Y, topB.Z);

                AddFace(topCenter, topA, topB);
                AddFace(bottomCenter, bottomB, bottomA);
                AddFace(topA, bottomA, bottomB);
                AddFace(topA, bottomB, topB);
            }

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
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

        private ManaGeometryVertex CreateVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            return new ManaGeometryVertex
            {
                Position = position,
                Normal = normal,
                Color = DefaultVertexColor,
                TexCoord0 = uv,
                TexCoord1 = uv
            };
        }
    }
}
