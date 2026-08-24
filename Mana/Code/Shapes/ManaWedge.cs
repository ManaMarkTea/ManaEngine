namespace Mana.Code.Shapes
{
    /// <summary>
    /// A right-triangular prism that is high on local -Z and slopes down toward local +Z.
    /// <see cref="Size"/> is the full visual bounding size.
    /// </summary>
    public sealed class ManaWedge : ManaProceduralMesh
    {
        public Vector3 Size { get; }

        public ManaWedge(ManaContext context, Vector4 color, Vector3 size) : base(context)
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

            var backLeftBottom = new Vector3(-half.X, -half.Y, -half.Z);
            var backRightBottom = new Vector3(half.X, -half.Y, -half.Z);
            var frontRightBottom = new Vector3(half.X, -half.Y, half.Z);
            var frontLeftBottom = new Vector3(-half.X, -half.Y, half.Z);
            var backLeftTop = new Vector3(-half.X, half.Y, -half.Z);
            var backRightTop = new Vector3(half.X, half.Y, -half.Z);
            var interior = new Vector3(0.0f, -half.Y / 3.0f, -half.Z / 3.0f);

            AddFace(backLeftBottom, frontLeftBottom, frontRightBottom, interior);
            AddFace(backLeftBottom, frontRightBottom, backRightBottom, interior);

            AddFace(backLeftBottom, backRightBottom, backRightTop, interior);
            AddFace(backLeftBottom, backRightTop, backLeftTop, interior);

            AddFace(backLeftBottom, backLeftTop, frontLeftBottom, interior);
            AddFace(backRightBottom, frontRightBottom, backRightTop, interior);

            AddFace(backLeftTop, backRightTop, frontRightBottom, interior);
            AddFace(backLeftTop, frontRightBottom, frontLeftBottom, interior);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
#if !MANA_BROWSER
                VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
#endif
            }
        }

        private void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 interior)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var center = (a + b + c) / 3.0f;
            if (Vector3.Dot(normal, center - interior) < 0.0f)
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
