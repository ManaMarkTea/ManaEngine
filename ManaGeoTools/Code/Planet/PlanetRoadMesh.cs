using Mana;
using Mana.Code;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A cell's roads of one class as ribbons drawn by planet_road.wgsl: two corners at each point of the centre line, the
    /// shader widening them along the way across the road (vertex colour) to the road's own width or its minimum on
    /// screen, whichever is wider. uv0 is the side (-1, +1) and the metres along the road, for the lane dashes. Reads the
    /// point arrays it is given every rebuild, so laying them on the ground again is a rebuild.
    /// </summary>
    internal sealed class PlanetRoadMesh : ManaProceduralMesh
    {
        private readonly Vector3[][] _lines;
        private readonly float _metresPerUnit;

        public override ManaPosition? RenderOrigin { get; }

        public PlanetRoadMesh(ManaContext context, Vector3[][] lines, float metresPerUnit, ManaPosition? origin) : base(context)
        {
            _lines = lines;
            _metresPerUnit = metresPerUnit;
            RenderOrigin = origin;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context)
            {
                Name = "Planet Road Material",
                Shader = new ManaPlanetRoadShader(Context)
            };
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();
            var vertexCount = 0;
            var indexCount = 0;
            foreach (var line in _lines)
            {
                if (line.Length < 2)
                {
                    continue;
                }
                vertexCount += line.Length * 2;
                indexCount += (line.Length - 1) * 6;
            }

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var sides = new Vector4[vertexCount];
            var uvs = new Vector2[vertexCount];
            var indexes = new uint[indexCount];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var vertex = 0;
            var index = 0;
            foreach (var line in _lines)
            {
                if (line.Length < 2)
                {
                    continue;
                }
                var start = (uint)vertex;
                var along = 0.0f;
                for (var i = 0; i < line.Length; i++)
                {
                    var point = line[i];
                    if (i > 0)
                    {
                        along += Vector3.Distance(point, line[i - 1]) * _metresPerUnit;
                    }
                    var before = line[Math.Max(0, i - 1)];
                    var after = line[Math.Min(line.Length - 1, i + 1)];
                    var up = Vector3.Normalize(point + RenderOrigin.GetValueOrDefault().ToVector3());
                    var tangent = UnitDirection(point - before) + UnitDirection(after - point);
                    if (tangent.LengthSquared() < 1e-6f)
                    {
                        tangent = after - before;
                    }
                    var side = Vector3.Cross(tangent, up);
                    if (side.LengthSquared() > 1e-12f)
                    {
                        side = Vector3.Normalize(side);
                    }
                    var sideColour = new Vector4(side, 0.0f);
                    for (var s = 0; s < 2; s++)
                    {
                        positions[vertex] = point;
                        normals[vertex] = up;
                        sides[vertex] = sideColour;
                        uvs[vertex] = new Vector2(s * 2.0f - 1.0f, along);
                        vertex++;
                    }
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
                for (var i = 0; i + 1 < line.Length; i++)
                {
                    var a = start + (uint)(i * 2);
                    indexes[index++] = a;
                    indexes[index++] = a + 1;
                    indexes[index++] = a + 2;
                    indexes[index++] = a + 1;
                    indexes[index++] = a + 3;
                    indexes[index++] = a + 2;
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
                Colors = sides,
                TextureCoordinates0 = uvs,
                TextureCoordinates1 = new Vector2[vertexCount],
                Unshared = true
            });
        }

        private static Vector3 UnitDirection(Vector3 delta)
        {
            if (delta.LengthSquared() > 1e-12f)
            {
                return Vector3.Normalize(delta);
            }
            return Vector3.Zero;
        }
    }
}
