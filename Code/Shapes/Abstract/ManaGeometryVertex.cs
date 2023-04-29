using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public enum ManaVertexAttributeClass
    {
        Single,
        Vector2,
        Vector3,
        Vector4,
        Matrix4,
        String
    }

    public class ManaVertexAttribute
    {
        public string Name;
        public ManaVertexAttributeClass AttributeClass = ManaVertexAttributeClass.Vector3;

        public float X;
        public Vector2 XY;
        public Vector3 XYZ;
        public Vector4 XYZW;
        public Matrix4x4 Mat4;
        public string Text;

        public override string ToString()
        {
            switch (AttributeClass)
            {
                case ManaVertexAttributeClass.Single: return X.ToString();
                case ManaVertexAttributeClass.Vector2: return XY.ToString();
                case ManaVertexAttributeClass.Vector3: return XYZ.ToString();
                case ManaVertexAttributeClass.Vector4: return XYZW.ToString();
                case ManaVertexAttributeClass.String: return Text.ToString();
                case ManaVertexAttributeClass.Matrix4: return Mat4.ToString();
            }

            return Name;
        }
    }

    public class ManaGeometryVertex
    {
        public ManaDynamicGeometryContainer Geom;
        public int Index = -1;

        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Color;
        public Vector2 TexCoord0;
        public Vector2 TexCoord1;

        public List<string> GroupMembership = new List<string>();
        public List<ManaVertexAttribute> ExtraAttributes = new List<ManaVertexAttribute>();        

        public override string ToString()
        {
            return Position.ToString();
        }
    }
}
