using Mana;
using Mana.Code;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// Room for a fixed number of aircraft markers -- a plane seen from above, nose along the heading -- rewritten
    /// every frame. Opaque meshes are drawn without culling, so one side shows from above and below. Unused slots
    /// collapse to a point. Unlit, so the night side's traffic still reads.
    /// </summary>
    public sealed class FlightTrafficMesh : ManaMesh
    {
        // The planform as (forward, right) in marker lengths: fuselage, swept wings, tailplane.
        private static readonly Vector2[] Outline =
        [
            new(1.0f, 0.0f),
            new(0.7f, 0.085f), new(0.7f, -0.085f),
            new(0.25f, 0.085f), new(0.25f, -0.085f),
            new(-0.22f, 0.95f), new(-0.22f, -0.95f),
            new(-0.36f, 0.95f), new(-0.36f, -0.95f),
            new(-0.18f, 0.07f), new(-0.18f, -0.07f),
            new(-0.62f, 0.055f), new(-0.62f, -0.055f),
            new(-0.86f, 0.38f), new(-0.86f, -0.38f),
            new(-0.97f, 0.38f), new(-0.97f, -0.38f),
            new(-1.0f, 0.0f)
        ];

        private static readonly uint[] Triangles =
        [
            0, 1, 2, 1, 3, 4, 1, 4, 2, 3, 9, 10, 3, 10, 4, 9, 11, 12, 9, 12, 10, 11, 17, 12,
            3, 5, 7, 3, 7, 9, 4, 8, 6, 4, 10, 8,
            11, 13, 15, 11, 15, 17, 12, 16, 14, 12, 17, 16
        ];

        public static int VerticesPerMarker => Outline.Length;

        // Published in blocks of this many markers: only what is on screen goes up, and the buffer changes size seldom.
        private const int Block = 512;

        public int Capacity { get; }
        public Vector3[] Positions { get; }
        public Vector4[] Colors { get; }

        // The block-sized copies handed to the renderer; remade only when the count crosses a block.
        private Vector3[] _shownPositions = [];
        private Vector4[] _shownColors = [];
        private Vector3[] _shownNormals = [];
        private Vector2[] _shownCoordinates = [];
        private uint[] _shownIndexes = [];

        public FlightTrafficMesh(ManaContext context, int capacity, float boundsRadius) : base(context)
        {
            Name = "Flight Traffic";
            Capacity = capacity;
            var vertices = capacity * VerticesPerMarker;
            Positions = new Vector3[vertices];
            Colors = new Vector4[vertices];

            SetSize(-boundsRadius, boundsRadius);
            Publish(0);

            var material = new ManaMaterial(context)
            {
                Name = "Flight Traffic Material",
                Shader = new ManaPbrShader(context),
                Unlit = true
            };
            material.Albedo.Value = Vector4.One;
            material.Roughness.Value = 1.0f;
            material.Metallic.Value = 0.0f;
            Materials = [material];
        }

        /// <summary>Writes marker <paramref name="slot"/>: centred on <paramref name="center"/>, nose along <paramref name="forward"/>, lying across <paramref name="up"/>.</summary>
        public void SetMarker(int slot, Vector3 center, Vector3 forward, Vector3 up, float size, Vector4 color)
        {
            var right = Vector3.Cross(forward, up) * size;
            var ahead = forward * size;
            var v = slot * VerticesPerMarker;
            for (var k = 0; k < Outline.Length; k++)
            {
                Positions[v + k] = center + ahead * Outline[k].X + right * Outline[k].Y;
                Colors[v + k] = color;
            }
        }

        /// <summary>Hands the first <paramref name="count"/> markers on to the renderer; the rest of their block folds to nothing.</summary>
        public void Publish(int count)
        {
            var markers = Math.Min(Capacity, Math.Max(Block, (count + Block - 1) / Block * Block));
            var vertices = markers * VerticesPerMarker;
            if (_shownPositions.Length != vertices)
            {
                Resize(markers);
            }

            var written = Math.Min(count, markers) * VerticesPerMarker;
            Array.Copy(Positions, _shownPositions, written);
            Array.Copy(Colors, _shownColors, written);
            Array.Clear(_shownPositions, written, vertices - written);
            SetCpuData(new ManaMeshCpuData
            {
                Indexes = _shownIndexes,
                Positions = _shownPositions,
                // Unlit: normals and coordinates are never read, so they stay as made.
                Normals = _shownNormals,
                Colors = _shownColors,
                TextureCoordinates0 = _shownCoordinates,
                TextureCoordinates1 = _shownCoordinates,
                Unshared = true
            });
        }

        private void Resize(int markers)
        {
            var vertices = markers * VerticesPerMarker;
            _shownPositions = new Vector3[vertices];
            _shownColors = new Vector4[vertices];
            _shownNormals = new Vector3[vertices];
            Array.Fill(_shownNormals, Vector3.UnitY);
            _shownCoordinates = new Vector2[vertices];
            _shownIndexes = new uint[markers * Triangles.Length];
            for (var marker = 0; marker < markers; marker++)
            {
                var v = (uint)(marker * VerticesPerMarker);
                var i = marker * Triangles.Length;
                for (var k = 0; k < Triangles.Length; k++)
                {
                    _shownIndexes[i + k] = v + Triangles[k];
                }
            }
        }
    }
}
