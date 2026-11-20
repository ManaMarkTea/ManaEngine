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
    /// carries its distance to the shore (colour r, metres) and its place east and north of the mesh's first point
    /// (uv0, metres), small numbers the ripples keep their precision in. Reads the point arrays it is given every
    /// rebuild, so laying them on the ground again is a rebuild.
    /// </summary>
    internal sealed class PlanetWaterMesh : ManaProceduralMesh
    {
        private readonly Vector3[][] _pieces;
        private readonly uint[][] _triangles;
        private readonly float[][] _shore;
        private readonly float _metresPerUnit;

        public PlanetWaterMesh(ManaContext context, Vector3[][] pieces, uint[][] triangles, float[][] shore, float metresPerUnit) : base(context)
        {
            _pieces = pieces;
            _triangles = triangles;
            _shore = shore;
            _metresPerUnit = metresPerUnit;
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

            // The east and north at the first point, for every corner's (east, north) metres from it.
            var origin = Vector3.Zero;
            var east = Vector3.UnitX;
            var north = Vector3.UnitZ;
            if (vertexCount > 0)
            {
                foreach (var piece in _pieces)
                {
                    if (piece.Length > 0)
                    {
                        origin = piece[0];
                        break;
                    }
                }
                var up = Vector3.Normalize(origin);
                var across = Vector3.Cross(Vector3.UnitY, up);
                if (across.LengthSquared() > 1e-8f)
                {
                    east = Vector3.Normalize(across);
                    north = Vector3.Cross(up, east);
                }
            }

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
                    var offset = point - origin;
                    uvs[vertex] = new Vector2(Vector3.Dot(offset, east), Vector3.Dot(offset, north)) * _metresPerUnit;
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
