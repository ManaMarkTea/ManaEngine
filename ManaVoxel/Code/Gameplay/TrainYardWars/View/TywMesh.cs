using Mana.Code.Shapes;
using Mana;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>
    /// A mesh made of coloured triangles handed over by a builder: hex tiles, track, markers. The material stays
    /// white so the vertex colours are the colours.
    /// </summary>
    public sealed class TywMesh : ManaProceduralMesh
    {
        private readonly Action<TywMeshBuilder> _build;

        public TywMesh(ManaContext context, Action<TywMeshBuilder> build, bool unlit = false) : base(context)
        {
            _build = build;
            DefaultVertexColor = Vector4.One;
            GenerateData();
            if (unlit && Materials.Length > 0)
            {
                Materials[0].Unlit = true;
            }
        }

        protected override void GenerateData()
        {
            base.GenerateData();
            var builder = new TywMeshBuilder(DynamicGeometryContainer);
            _build(builder);
            MinSize = builder.Min;
            MaxSize = builder.Max;
            // CPU data only: the WebGPU backend uploads from it, and this game runs on WebGPU alone.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }
    }

    public sealed class TywMeshBuilder
    {
        private readonly ManaDynamicGeometryContainer _geometry;

        internal TywMeshBuilder(ManaDynamicGeometryContainer geometry)
        {
            _geometry = geometry;
        }

        public Vector3 Min { get; private set; } = new(float.MaxValue);
        public Vector3 Max { get; private set; } = new(float.MinValue);

        public bool IsEmpty => Min.X > Max.X;

        /// <summary>One flat triangle, wound so it faces <paramref name="outward"/>.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color, Vector3 outward)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-12f)
            {
                return;
            }

            if (Vector3.Dot(normal, outward) < 0.0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }

            normal = Vector3.Normalize(normal);
            var vertices = _geometry.AddVertexRange(Vertex(a, normal, color), Vertex(b, normal, color), Vertex(c, normal, color));
            _geometry.CreateFacesFromPoints(vertices.ToList());
            Min = Vector3.Min(Min, Vector3.Min(a, Vector3.Min(b, c)));
            Max = Vector3.Max(Max, Vector3.Max(a, Vector3.Max(b, c)));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color, Vector3 outward)
        {
            Triangle(a, b, c, color, outward);
            Triangle(a, c, d, color, outward);
        }

        /// <summary>A box of <paramref name="size"/> centred on <paramref name="center"/>, its local X along <paramref name="along"/>.</summary>
        public void Box(Vector3 center, Vector3 along, Vector3 size, Vector4 color)
        {
            along = new Vector3(along.X, 0.0f, along.Z);
            along = along.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(along);
            var side = Vector3.Cross(Vector3.UnitY, along);
            var x = along * size.X * 0.5f;
            var y = Vector3.UnitY * size.Y * 0.5f;
            var z = side * size.Z * 0.5f;
            Vector3 P(int sx, int sy, int sz) => center + x * sx + y * sy + z * sz;

            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), color, Vector3.UnitY);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color * Shade(0.6f), -Vector3.UnitY);
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), color * Shade(0.85f), along);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), color * Shade(0.85f), -along);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color * Shade(0.9f), side);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), color * Shade(0.9f), -side);
        }

        /// <summary>A hexagonal prism: pointy-top, <paramref name="radius"/> centre to corner, from <paramref name="bottom"/> to <paramref name="top"/>.</summary>
        public void HexPrism(Vector3 center, float radius, float bottom, float top, Vector4 topColor, Vector4 sideColor)
        {
            var corners = new Vector3[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = MathF.PI / 180.0f * (60.0f * i - 30.0f);
                corners[i] = new Vector3(center.X + radius * MathF.Cos(angle), 0.0f, center.Z + radius * MathF.Sin(angle));
            }

            var middle = new Vector3(center.X, top, center.Z);
            for (var i = 0; i < 6; i++)
            {
                var a = corners[i] with { Y = top };
                var b = corners[(i + 1) % 6] with { Y = top };
                Triangle(middle, a, b, topColor, Vector3.UnitY);

                var outward = Vector3.Normalize((a + b) * 0.5f - middle);
                Quad(a, b, corners[(i + 1) % 6] with { Y = bottom }, corners[i] with { Y = bottom }, sideColor, outward);
            }
        }

        /// <summary>A thin flat hexagon outline lying at <paramref name="height"/>.</summary>
        public void HexRing(Vector3 center, float radius, float width, float height, Vector4 color)
        {
            for (var i = 0; i < 6; i++)
            {
                var a0 = Corner(center, radius, i, height);
                var b0 = Corner(center, radius, i + 1, height);
                var a1 = Corner(center, radius - width, i, height);
                var b1 = Corner(center, radius - width, i + 1, height);
                Quad(a0, b0, b1, a1, color, Vector3.UnitY);
            }
        }

        public static Vector3 Corner(Vector3 center, float radius, int index, float height)
        {
            var angle = MathF.PI / 180.0f * (60.0f * index - 30.0f);
            return new Vector3(center.X + radius * MathF.Cos(angle), height, center.Z + radius * MathF.Sin(angle));
        }

        private static Vector4 Shade(float amount) => new(amount, amount, amount, 1.0f);

        private static ManaGeometryVertex Vertex(Vector3 position, Vector3 normal, Vector4 color) => new()
        {
            Position = position,
            Normal = normal,
            Color = color,
            TexCoord0 = Vector2.Zero,
            TexCoord1 = Vector2.Zero
        };
    }
}
