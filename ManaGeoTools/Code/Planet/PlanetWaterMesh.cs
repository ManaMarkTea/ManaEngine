using Mana;
using Mana.Code;
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Water areas laid on a planet: triangles over a cell's draped points, drawn flat-coloured by draped_line.wgsl with
    /// no widening (zero vertex offsets), so they get the same pull toward the camera that keeps lines above the ground.
    /// Reads the point arrays it is given every rebuild, so laying them on the ground again is a rebuild.
    /// </summary>
    internal sealed class PlanetWaterMesh : ManaProceduralMesh
    {
        private readonly Vector3[][] pieces;
        private readonly uint[][] triangles;
        private readonly Vector4 colour;

        public PlanetWaterMesh(ManaContext context, Vector3[][] pieces, uint[][] triangles, Vector4 colour) : base(context)
        {
            this.pieces = pieces;
            this.triangles = triangles;
            this.colour = colour;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context)
            {
                Name = "Planet Water Material",
                Shader = new ManaDrapedLineShader(Context)
            };
            material.Albedo.Value = colour;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();
            var vertexCount = 0;
            var indexCount = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                vertexCount += pieces[i].Length;
                indexCount += triangles[i].Length;
            }

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var indexes = new uint[indexCount];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var vertex = 0;
            var index = 0;
            for (var i = 0; i < pieces.Length; i++)
            {
                var start = (uint)vertex;
                foreach (var point in pieces[i])
                {
                    positions[vertex] = point;
                    normals[vertex] = Vector3.Normalize(point);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    vertex++;
                }
                foreach (var corner in triangles[i])
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
            // Zero colour: the shader widens a vertex along its colour, and an area is not widened.
            SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = new Vector4[vertexCount],
                TextureCoordinates0 = new Vector2[vertexCount],
                TextureCoordinates1 = new Vector2[vertexCount],
                Unshared = true
            });
        }
    }
}
