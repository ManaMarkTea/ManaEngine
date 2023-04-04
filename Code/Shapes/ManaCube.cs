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
    public class ManaCube : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaCube(ManaWorld world, Vector4 color, Vector3 size) : base(world)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();            
        }

        protected override void GenerateData()
        {
            Vector3 c = CubeSize * 0.5f;
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = new ManaMaterial(World);
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            
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


            for ( int i = 0; i < 6; i++)
            {
                var rotation = rotations[i];
                var scaleRotation = scaleRotations[i];

                var cR = Vector3.Transform(c, scaleRotation);
                
                var va = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), rotation);
                var vb = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), rotation);
                var vc = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), rotation);
                var vd = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), rotation);

                vertexBuilder.Verts.AddRange(new List<ManaVertex>
                {
                    new ManaVertex()
                    {
                        Position = va,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = this.DefaultVertexColor,
                        TexCoord0 = new Vector2(0,0)
                    },

                    new ManaVertex()
                    {
                        Position = vb,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = this.DefaultVertexColor,
                        TexCoord0 = new Vector2(1,0)
                    },

                    new ManaVertex()
                    {
                        Position = vc,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = this.DefaultVertexColor,
                        TexCoord0 = new Vector2(1,1)
                    },

                    new ManaVertex()
                    {
                        Position = vd,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = this.DefaultVertexColor,
                        TexCoord0 = new Vector2(0,1)
                    },
                });

            }
           
            
            var positions = vertexBuilder.Positions.ToArray();
            var normals = vertexBuilder.Normals.ToArray();
            var colors = vertexBuilder.Colors.ToArray();
            var textureCoordinates0 = vertexBuilder.TexCoords0.ToArray();
            var textureCoordinates1 = vertexBuilder.TexCoords0.ToArray();
            var indexes = vertexBuilder.GenerateIndexForQuads().ToArray();

            Materials = newMaterials.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();
        }

    }
}
