namespace Mana.Code.Shapes
{
    /// <summary>
    /// One mesh of many coloured shapes -- boxes, rocks, hexes, loose triangles -- handed over by a builder. The renderer
    /// pays per mesh, not per triangle, so a whole stretch of ground, track or ore goes up as one. The material stays
    /// white so the vertex colours are the colours.
    /// </summary>
    public sealed class ManaShapeMesh : ManaProceduralMesh
    {
        private readonly Action<ManaShapeMeshBuilder> _build;

        /// <param name="context">The context the mesh draws in.</param>
        /// <param name="build">Adds the shapes, each time the mesh is (re)generated.</param>
        /// <param name="unlit">Shown as drawn, not lit (markers, glowing parts).</param>
        /// <param name="glow">How much the mesh shines with its own colours (0 none): enough to read at dusk, not to light anything.</param>
        /// <param name="blend">Vertex alpha fades it into what is under it.</param>
        public ManaShapeMesh(ManaContext context, Action<ManaShapeMeshBuilder> build, bool unlit = false, float glow = 0.0f, bool blend = false) : base(context)
        {
            _build = build;
            DefaultVertexColor = Vector4.One;
            GenerateData();
            if (Materials.Length == 0)
            {
                return;
            }
            if (unlit)
            {
                Materials[0].Unlit = true;
            }
            if (blend)
            {
                Materials[0].OpacityMode = ManaCore.Graphics.ManaOpacityMode.Blend;
            }
            if (glow > 0.0f)
            {
                Materials[0].Emission.Value = Vector4.One;
                Materials[0].EmissionStrength = glow;
            }
        }

        /// <summary>Nothing was drawn: not worth a node.</summary>
        public bool IsEmpty { get; private set; }

        protected override void GenerateData()
        {
            base.GenerateData();
            var builder = new ManaShapeMeshBuilder(DynamicGeometryContainer);
            _build(builder);
            IsEmpty = builder.IsEmpty;
            // An empty mesh still needs bounds and a triangle to be a valid one; this one is out of sight.
            if (builder.IsEmpty)
            {
                builder.Triangle(new Vector3(0, -50, 0), new Vector3(0.01f, -50, 0), new Vector3(0, -50, 0.01f), Vector4.Zero, Vector3.UnitY);
            }
            MinSize = builder.Min;
            MaxSize = builder.Max;
            // CPU data only: the WebGPU backend uploads from it.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }
    }

    public sealed class ManaShapeMeshBuilder
    {
        private readonly ManaDynamicGeometryContainer _geometry;

        internal ManaShapeMeshBuilder(ManaDynamicGeometryContainer geometry)
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
            if (normal.LengthSquared() < 1e-14f)
            {
                return;
            }
            if (Vector3.Dot(normal, outward) < 0)
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

        /// <summary>A block sitting on the ground, square to the axes: its sides a shade darker than its top so edges read, and no bottom.</summary>
        public void Box(Vector3 center, Vector3 size, Vector4 color, bool sides = true)
        {
            var h = size * 0.5f;
            Vector3 P(int x, int y, int z) => center + new Vector3(h.X * x, h.Y * y, h.Z * z);
            var side = new Vector4(color.X * 0.85f, color.Y * 0.85f, color.Z * 0.85f, color.W);
            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), color, Vector3.UnitY);
            if (!sides)
            {
                return;
            }
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), side, Vector3.UnitX);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), side, -Vector3.UnitX);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), side, Vector3.UnitZ);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), side, -Vector3.UnitZ);
        }

        /// <summary>
        /// A closed box of <paramref name="size"/> centred on <paramref name="center"/>, its local X along
        /// <paramref name="along"/>; each face its own shade (top brightest, bottom darkest), lit or not.
        /// </summary>
        public void Box(Vector3 center, Vector3 along, Vector3 size, Vector4 color)
        {
            along = new Vector3(along.X, 0.0f, along.Z);
            if (along.LengthSquared() < 1e-8f)
            {
                along = Vector3.UnitX;
            }
            along = Vector3.Normalize(along);
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

        /// <summary>A faceted lump: an octahedron squashed and skewed by <paramref name="seed"/>.</summary>
        public void Rock(Vector3 center, float size, Vector4 color, float seed)
        {
            var skew = new Vector3(MathF.Sin(seed * 12.9f), 0, MathF.Cos(seed * 7.3f)) * size * 0.15f;
            var top = center + new Vector3(0, size * 0.75f, 0) + skew;
            var bottom = center - new Vector3(0, size * 0.1f, 0);
            var turn = seed * 6.28f;
            var ring = new Vector3[5];
            for (var i = 0; i < ring.Length; i++)
            {
                var angle = turn + i * MathF.Tau / ring.Length;
                var reach = size * (0.42f + 0.18f * MathF.Abs(MathF.Sin(seed * 31 + i * 1.7f)));
                ring[i] = center + new Vector3(MathF.Cos(angle) * reach, size * 0.22f, MathF.Sin(angle) * reach);
            }
            var dark = new Vector4(color.X * 0.75f, color.Y * 0.75f, color.Z * 0.75f, 1);
            // Every other facet a touch darker, so the lump reads as faceted.
            var facet = color * 0.92f + new Vector4(0, 0, 0, 0.08f);
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var middle = (a + b) * 0.5f - center;
                var shade = facet;
                if (i % 2 == 0)
                {
                    shade = color;
                }
                Triangle(top, a, b, shade, middle + Vector3.UnitY * size * 0.5f);
                Triangle(bottom, a, b, dark, middle - Vector3.UnitY * size * 0.2f);
            }
        }

        /// <summary>A hexagonal prism: pointy-top, <paramref name="radius"/> centre to corner, from <paramref name="bottom"/> to <paramref name="top"/>.</summary>
        public void HexPrism(Vector3 center, float radius, float bottom, float top, Vector4 topColor, Vector4 sideColor)
        {
            var middle = new Vector3(center.X, top, center.Z);
            for (var i = 0; i < 6; i++)
            {
                var a = Corner(center, radius, i, top);
                var b = Corner(center, radius, i + 1, top);
                Triangle(middle, a, b, topColor, Vector3.UnitY);
                var outward = Vector3.Normalize((a + b) * 0.5f - middle);
                Quad(a, b, b with { Y = bottom }, a with { Y = bottom }, sideColor, outward);
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

        /// <summary>One side of a flat hexagon's outline, between corner <paramref name="index"/> and the next.</summary>
        public void HexEdge(Vector3 center, float radius, float width, int index, float height, Vector4 color)
        {
            // Centred on the side and a half-width longer at each end, so lines of hexes that meet at a joint overlap
            // there instead of leaving a notch; use an opaque colour, or the overlap shows.
            var a = Corner(center, radius, index, height);
            var b = Corner(center, radius, index + 1, height);
            var along = Vector3.Normalize(b - a) * (width * 0.5f);
            var across = Vector3.Cross(Vector3.UnitY, Vector3.Normalize(b - a)) * (width * 0.5f);
            a -= along;
            b += along;
            Quad(a - across, b - across, b + across, a + across, color, Vector3.UnitY);
        }

        /// <summary>A flat hexagon at <paramref name="height"/>.</summary>
        public void HexFill(Vector3 center, float radius, float height, Vector4 color)
        {
            for (var i = 0; i < 6; i++)
            {
                HexWedge(center, radius, height, i, color);
            }
        }

        /// <summary>One sixth of a flat hexagon, between corner <paramref name="index"/> and the next.</summary>
        public void HexWedge(Vector3 center, float radius, float height, int index, Vector4 color) =>
            Triangle(center with { Y = height }, Corner(center, radius, index, height), Corner(center, radius, index + 1, height), color, Vector3.UnitY);

        /// <summary>Corner <paramref name="index"/> of a pointy-top hexagon, <paramref name="radius"/> from its centre, at <paramref name="height"/>.</summary>
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
