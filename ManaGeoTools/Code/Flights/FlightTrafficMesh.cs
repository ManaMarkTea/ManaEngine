using Mana;
using Mana.Code;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// Room for a fixed number of aircraft markers -- a dart pointing along the heading, top and underside each
    /// their own pair of triangles so one shows whichever side is culled -- rewritten every frame. Unused slots
    /// collapse to a point. Unlit, so the night side's traffic still reads.
    /// </summary>
    public sealed class FlightTrafficMesh : ManaMesh
    {
        public const int VerticesPerMarker = 8;

        public int Capacity { get; }
        public Vector3[] Positions { get; }
        public Vector4[] Colors { get; }
        private readonly Vector3[] _normals;
        private readonly uint[] _indexes;
        private readonly Vector2[] _coordinates;

        public FlightTrafficMesh(ManaContext context, int capacity, float boundsRadius) : base(context)
        {
            Name = "Flight Traffic";
            Capacity = capacity;
            var vertices = capacity * VerticesPerMarker;
            Positions = new Vector3[vertices];
            Colors = new Vector4[vertices];
            _normals = new Vector3[vertices];
            _coordinates = new Vector2[vertices];
            _indexes = new uint[capacity * 12];
            // Nose, right wing, tail notch / nose, tail notch, left wing -- once each way round.
            uint[] dart = [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6];
            for (var marker = 0; marker < capacity; marker++)
            {
                var v = (uint)(marker * VerticesPerMarker);
                var i = marker * 12;
                for (var k = 0; k < dart.Length; k++)
                {
                    _indexes[i + k] = v + dart[k];
                }
            }

            SetSize(-boundsRadius, boundsRadius);
            Publish();

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
            var right = Vector3.Cross(forward, up);
            var nose = center + forward * size;
            var rightWing = center - forward * (0.6f * size) + right * (0.75f * size);
            var tail = center - forward * (0.25f * size);
            var leftWing = center - forward * (0.6f * size) - right * (0.75f * size);
            var v = slot * VerticesPerMarker;
            Positions[v] = nose;
            Positions[v + 1] = rightWing;
            Positions[v + 2] = tail;
            Positions[v + 3] = leftWing;
            Positions[v + 4] = nose;
            Positions[v + 5] = rightWing;
            Positions[v + 6] = tail;
            Positions[v + 7] = leftWing;
            var underside = color * new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
            for (var k = 0; k < 4; k++)
            {
                Colors[v + k] = color;
                _normals[v + k] = up;
                Colors[v + 4 + k] = underside;
                _normals[v + 4 + k] = -up;
            }
        }

        /// <summary>Folds slots from <paramref name="from"/> on into nothing.</summary>
        public void ClearFrom(int from)
        {
            var start = Math.Max(0, from) * VerticesPerMarker;
            if (start < Positions.Length)
            {
                Array.Clear(Positions, start, Positions.Length - start);
            }
        }

        /// <summary>Hands the rewritten markers on to the renderer.</summary>
        public void Publish()
        {
            SetCpuData(new ManaMeshCpuData
            {
                Indexes = _indexes,
                Positions = Positions,
                Normals = _normals,
                Colors = Colors,
                TextureCoordinates0 = _coordinates,
                TextureCoordinates1 = _coordinates
            });
        }
    }
}
