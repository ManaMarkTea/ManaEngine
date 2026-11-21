using Mana;
using Mana.Code;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Water areas laid on a planet: triangles over a cell's draped points, drawn by planet_water.wgsl. Each corner
    /// carries its distance to the shore (colour r, metres) and its vector tile coordinate (uv0).
    /// Reads the point arrays it is given every
    /// rebuild, so laying them on the ground again is a rebuild.
    /// </summary>
    internal sealed class PlanetWaterMesh : ManaProceduralMesh
    {
        private readonly Vector3[][] _pieces;
        private readonly uint[][] _triangles;
        private readonly float[][] _shore;
        private readonly Vector2[][] _coordinates;
        private readonly Vector2 _tileIndex;

        public PlanetWaterMesh(ManaContext context, Vector3[][] pieces, uint[][] triangles, float[][] shore,
            Vector2[][] coordinates, Vector2 tileIndex) : base(context)
        {
            _pieces = pieces;
            _triangles = triangles;
            _shore = shore;
            _coordinates = coordinates;
            _tileIndex = tileIndex;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context)
            {
                Name = "Planet Water Material",
                Shader = new ManaPlanetWaterShader(Context)
            };
            material.EnsureShaderMetadataDefaults();
            material.SetShaderUniform("tileIndex", _tileIndex);
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();
            var vertexCount = 0;
            var indexCount = 0;
            for (var i = 0; i < _pieces.Length; i++)
            {
                vertexCount += _pieces[i].Length;
                indexCount += _triangles[i].Length;
            }

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var colours = new Vector4[vertexCount];
            var uvs = new Vector2[vertexCount];
            var indexes = new uint[indexCount];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            var vertex = 0;
            var index = 0;
            for (var i = 0; i < _pieces.Length; i++)
            {
                var start = (uint)vertex;
                for (var p = 0; p < _pieces[i].Length; p++)
                {
                    var point = _pieces[i][p];
                    positions[vertex] = point;
                    normals[vertex] = Vector3.Normalize(point);
                    uvs[vertex] = _coordinates[i][p];
                    colours[vertex] = new Vector4(_shore[i][p], 0.0f, 0.0f, 1.0f);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    vertex++;
                }
                foreach (var corner in _triangles[i])
                {
                    indexes[index++] = start + corner;
                }
            }

            MinSize = Vector3.Zero;
            MaxSize = Vector3.Zero;
            if (vertexCount > 0)
            {
                MinSize = min;
                MaxSize = max;
            }
            SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = colours,
                TextureCoordinates0 = uvs,
                TextureCoordinates1 = new Vector2[vertexCount],
                Unshared = true
            });
        }
    }
}
