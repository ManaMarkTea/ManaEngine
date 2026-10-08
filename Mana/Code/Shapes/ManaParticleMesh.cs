using ManaCore.Graphics;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;

namespace Mana.Code.Shapes
{
    /// <summary>
    /// Room for a fixed number of particles, each a fan of triangles round a centre point (a disc), whose positions
    /// and colours are rewritten every frame by an emitter. The index list and the material never change.
    /// </summary>
    public sealed class ManaParticleMesh : ManaMesh
    {
        public Vector3[] Positions { get; }
        public Vector4[] Colors { get; }
        private readonly Vector3[] _normals;
        private readonly ManaMeshCpuData _first;

        public ManaParticleMesh(ManaContext context, int capacity, int verticesPerParticle, int rimPoints)
            : base(context)
        {
            Name = "Particles";
            var vertices = capacity * verticesPerParticle;
            Positions = new Vector3[vertices];
            Colors = new Vector4[vertices];
            _normals = new Vector3[vertices];
            Array.Fill(_normals, Vector3.UnitZ);
            var coordinates = new Vector2[vertices];

            var indexes = new uint[capacity * rimPoints * 3];
            var cursor = 0;
            for (var p = 0; p < capacity; p++)
            {
                var centre = (uint)(p * verticesPerParticle);
                for (var k = 0; k < rimPoints; k++)
                {
                    indexes[cursor++] = centre;
                    indexes[cursor++] = centre + 1 + (uint)k;
                    indexes[cursor++] = centre + 1 + (uint)((k + 1) % rimPoints);
                }
            }

            _first = Describe(indexes, coordinates);
            SetCpuData(_first);

            var material = new ManaMaterial(context)
            {
                Name = "Particle Material",
                Shader = new ManaPbrShader(context),
                OpacityMode = ManaOpacityMode.Blend,
                // Blended meshes cull back faces; the emitter winds every disc to face the camera.
                Unlit = true
            };
            // The vertex colours carry the look; a default albedo would tint every particle.
            material.Albedo.Value = Vector4.One;
            material.Roughness.Value = 1.0f;
            material.Metallic.Value = 0.0f;
            Materials = [material];
        }

        private ManaMeshCpuData Describe(uint[] indexes, Vector2[] coordinates) => new()
        {
            Indexes = indexes,
            Positions = Positions,
            Normals = _normals,
            Colors = Colors,
            TextureCoordinates0 = coordinates,
            TextureCoordinates1 = coordinates
        };

        /// <summary>Hands the rewritten positions and colours on to the renderer.</summary>
        public void Publish()
        {
            var previous = CpuData ?? _first;
            SetCpuData(Describe(previous.Indexes, previous.TextureCoordinates0));
#if !MANA_BROWSER
            VertexBufferObject?.UpdateDynamicData(Positions, _normals, Colors);
#endif
        }
    }
}
