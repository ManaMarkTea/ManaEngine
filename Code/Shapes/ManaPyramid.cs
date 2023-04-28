using Mana.Code;
using Mana.Code.Shapes;
using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaPyramid : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaPyramid(ManaWorld world, Vector4 color, Vector3 size) : base(world)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();            
        }

        protected override void GenerateData()
        {
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = new ManaMaterial(World);
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            
            MinSize = -CubeSize;
            MaxSize = CubeSize;

            ManaVertexBuilder vertexBuilder = new ManaVertexBuilder();

            int rows = 3;

            /*
            // Triangle 1
            0.0,1.0,0.0,
            1.0,0.0,0.0,

            -1.0,-1.0,1.0,
            0.0,1.0,0.0,

            1.0,-1.0,1.0,
            0.0,0.0,1.0,

            //Triangle 2
            0.0,1.0,0.0,
            1.0,0.0,0.0,

            1.0,-1.0,1.0,
            0.0,0.0,1.0,

            1.0,-1.0,-1.0,
            0.0,1.0,0.0,

            //Triangle 3
            0.0,1.0,0.0,
            1.0,0.0,0.0,

            1.0,-1.0,-1.0,
            0.0,1.0,0.0,

            -1.0,-1.0,-1.0,
            0.0,0.0,1.0,

            //Triangle 4
            0.0,1.0,0.0,
            1.0,0.0,0.0,

            -1.0,-1.0,-1.0,
            0.0,0.0,1.0,

            -1.0,-1.0,1.0,
            0.0,1.0,0.0*/

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



            vertexBuilder.AddTriangle(CubeSize.Y, CubeSize.X, rotations[0], scaleRotations[0], this.DefaultVertexColor);
            vertexBuilder.AddTriangle(CubeSize.Y, CubeSize.X, rotations[1], scaleRotations[1], this.DefaultVertexColor);
            vertexBuilder.AddTriangle(CubeSize.Y, CubeSize.X, rotations[2], scaleRotations[2], this.DefaultVertexColor);
            vertexBuilder.AddTriangle(CubeSize.Y, CubeSize.X, rotations[3], scaleRotations[3], this.DefaultVertexColor);
            
            vertexBuilder.AddQuad(CubeSize, rotations[5], scaleRotations[5], this.DefaultVertexColor);

            var positions = vertexBuilder.Positions.ToArray();
            var normals = vertexBuilder.Normals.ToArray();
            var colors = vertexBuilder.Colors.ToArray();
            var textureCoordinates0 = vertexBuilder.TexCoords0.ToArray();
            var textureCoordinates1 = vertexBuilder.TexCoords0.ToArray();
            var indexes = vertexBuilder.Indexes.ToArray();

            Materials = newMaterials.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();
        }

    }
}
