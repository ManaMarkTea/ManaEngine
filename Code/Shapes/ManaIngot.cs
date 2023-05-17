using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mana.Code.Materials;

namespace Mana.Code.Shapes
{
    public class ManaIngot : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaIngot(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            Vector3 c = CubeSize;
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>
            {
                GenerateMaterial()
            };

            MinSize = -c;
            MaxSize = c;

            ManaVertexBuilder vertexBuilder = new ManaVertexBuilder();

            var rotations = new Matrix4x4[]
            {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(2 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(3 * 90.0f.ToRadians(), Vector3.Zero),

                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(-1 * 90.0f.ToRadians(), Vector3.Zero),
            };

            var scaleRotations = new Matrix4x4[]
            {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),

                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
            };


            for (int i = 0; i < 6; i++)
            {
                var rotation = rotations[i];
                var scaleRotation = scaleRotations[i];
                vertexBuilder.AddQuad(c, rotation, scaleRotation, this.DefaultVertexColor);
            }


            var positions = vertexBuilder.Positions.ToArray();
            var normals = vertexBuilder.Normals.ToArray();
            var colors = vertexBuilder.Colors.ToArray();
            var textureCoordinates0 = vertexBuilder.TexCoords0.ToArray();
            var textureCoordinates1 = vertexBuilder.TexCoords0.ToArray();
            var indexes = vertexBuilder.Indexes.ToArray();

            Materials = newMaterials.ToArray();

            VertexBufferObject = new ManaVertexBufferObject(Context, indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            VertexBufferObject.ConstructVBO();
        }

    }
}
