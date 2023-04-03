using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaVertexBuilder
    {
        public List<ManaVertex> Verts = new List<ManaVertex>();

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

        public List<Vector3> Positions
        {
            get
            {
                return Verts.Select((item)=>item.Position).ToList();
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
    }
}
