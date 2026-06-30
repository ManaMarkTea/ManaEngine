namespace Mana
{
    public class ManaVertexBuilder
    {
        public List<ManaVertex> Verts = new List<ManaVertex>();

        public List<uint> Indexes = new List<uint>();

        public List<uint> GenerateIndexForQuads()
        {
            int faceCount = Verts.Count / 4;
            List<uint> output = new List<uint>();

            for (uint i = 0; i < faceCount; i++)
            {
                uint index = i * 4;
                output.AddRange(new uint[] { index + 0, index + 1, index + 2 });
                output.AddRange(new uint[] { index + 0, index + 2, index + 3 });
            }

            return output;
        }

        public void AddTriangle(float height, float width, Matrix4x4 rotation, Matrix4x4 scale, Vector4 color)
        {
            float halfWidth = width * 0.5f;

            Vector3 a = new Vector3(-halfWidth, -height * 0.5f, halfWidth);
            Vector3 b = new Vector3(+halfWidth, -height * 0.5f, halfWidth);
            Vector3 c = new Vector3(0.0f, height * 0.5f, 0.0f);

            var va = Vector3.Transform(a, rotation);
            var vb = Vector3.Transform(b, rotation);
            var vc = Vector3.Transform(c, rotation);

            Verts.AddRange(new List<ManaVertex>
            {
                new ManaVertex()
                {
                    Position = va,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(0,0)
                },

                new ManaVertex()
                {
                    Position = vb,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1,0)
                },

                new ManaVertex()
                {
                    Position = vc,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1,1)
                },

            });

            Indexes.Add((uint)Verts.Count - 3);
            Indexes.Add((uint)Verts.Count - 2);
            Indexes.Add((uint)Verts.Count - 1);
        }

        public void AddQuad(Vector3 quadSize, Matrix4x4 rotation, Matrix4x4 scale, Vector4 color)
        {
            var cR = Vector3.Transform(quadSize * 0.5f, scale);

            var va = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), rotation);
            var vb = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), rotation);
            var vc = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), rotation);
            var vd = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), rotation);
            var normal = new Vector3(0, 0, 1).TransformNormal(rotation);
            var center = (va + vb + vc + vd) * 0.25f;
            if (Vector3.Dot(center, normal) < 0.0f)
            {
                normal = -normal;
            }
            normal = Vector3.Normalize(normal);

            Verts.AddRange(new List<ManaVertex>
            {
                new ManaVertex()
                {
                    Position = va,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(0,0)
                },

                new ManaVertex()
                {
                    Position = vb,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(1,0)
                },

                new ManaVertex()
                {
                    Position = vc,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(1,1)
                },

                new ManaVertex()
                {
                    Position = vd,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(0,1)
                },
            });

            uint index = (uint)Verts.Count - 4;
            Indexes.AddRange(new uint[] { index + 0, index + 1, index + 2 });
            Indexes.AddRange(new uint[] { index + 0, index + 2, index + 3 });
        }

        public List<Vector3> Positions
        {
            get
            {
                return Verts.Select((item) => item.Position).ToList();
            }
        }

        public List<Vector3> Normals
        {
            get
            {
                return Verts.Select((item) => item.Normal).ToList();
            }
        }

        public List<Vector4> Colors
        {
            get
            {
                return Verts.Select((item) => item.Color).ToList();
            }
        }

        public List<Vector2> TexCoords0
        {
            get
            {
                return Verts.Select((item) => item.TexCoord0).ToList();
            }
        }

        public List<Vector2> TexCoords1
        {
            get
            {
                return Verts.Select((item) => item.TexCoord1).ToList();
            }
        }

    }

    public struct ManaVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Color;
        public Vector2 TexCoord0;
        public Vector2 TexCoord1;
        public Vector3 Tangent;
        public Vector3 Bitangent;

        public const int MAX_BONE_INFLUENCE = 4;
        public int[] BoneIds;
        public float[] Weights;

        public override string ToString()
        {
            return Position.ToString();
        }
    }
}
