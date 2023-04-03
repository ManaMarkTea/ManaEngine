using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaCube : ManaMesh
    {        
        public ManaCube(ManaWorld world, float size = 1.0f) : base(world)
        {
            GenerateData(size);            
        }

        private void GenerateData(float size)
        {
            uint[] indexes = new uint[0];
            Vector3[] positions = new Vector3[0];
            Vector3[] normals = new Vector3[0];
            Vector4[] colors = new Vector4[0];
            Vector2[] textureCoordinates0 = new Vector2[0];
            Vector2[] textureCoordinates1 = new Vector2[0];

            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = new ManaMaterial(World);
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            float c = size * 0.5f;
            MinSize = new Vector3(-c, -c, -c);
            MaxSize = new Vector3(c, c, c);

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

            for ( int i = 0; i < 6; i++)
            {
                var rotation = rotations[i];
                var va = Vector3.Transform(new Vector3(-c, -c, -c), rotation);
                var vb = Vector3.Transform(new Vector3(c, -c, -c), rotation);
                var vc = Vector3.Transform(new Vector3(c, c, -c), rotation);
                var vd = Vector3.Transform(new Vector3(-c, c, -c), rotation);

                vertexBuilder.Verts.AddRange(new List<ManaVertex>
                {
                    new ManaVertex()
                    {
                        Position = va,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = new Vector4(1,1,1,1),
                        TexCoord0 = new Vector2(0,0)
                    },

                    new ManaVertex()
                    {
                        Position = vb,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = new Vector4(1,1,1,1),
                        TexCoord0 = new Vector2(1,0)
                    },

                    new ManaVertex()
                    {
                        Position = vc,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = new Vector4(1,1,1,1),
                        TexCoord0 = new Vector2(1,1)
                    },

                    new ManaVertex()
                    {
                        Position = vd,
                        Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                        Color = new Vector4(1,1,1,1),
                        TexCoord0 = new Vector2(0,1)
                    },
                });

            }
           
            
            positions = vertexBuilder.Positions.ToArray();
            normals = vertexBuilder.Normals.ToArray();
            colors = vertexBuilder.Colors.ToArray();
            textureCoordinates0 = vertexBuilder.TexCoords0.ToArray();
            indexes = vertexBuilder.GenerateIndexForQuads().ToArray();


            Materials = newMaterials.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();
        }

    }
}
