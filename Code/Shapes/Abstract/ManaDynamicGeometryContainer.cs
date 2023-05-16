using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class ManaDynamicGeometryContainer
    {
        public List<ManaGeometryVertex> DisconnectedPoints = new List<ManaGeometryVertex>();
        public List<ManaGeometryVertex> Verts = new List<ManaGeometryVertex>();
        public List<ManaGeometryFace> Faces = new List<ManaGeometryFace>();

        public ManaGeometryVertex AddPoint(Vector3 point)
        {
            ManaGeometryVertex v = new ManaGeometryVertex();
            v.Position = point;
            v.Index = Verts.Count;
            v.Geom = this;

            Verts.Add(v);
            DisconnectedPoints.Add(v);
            return v;
        }

        public void CreateFaceFromDisconnectedPoints()
        {
            int consumedPoints = 0;
            for( int i = 0; i < DisconnectedPoints.Count; i+=3 )
            {
                int indexA = i + 0;
                int indexB = i + 1;
                int indexC = i + 2;

                if (indexC >= DisconnectedPoints.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = DisconnectedPoints[indexA];
                face.B = DisconnectedPoints[indexB];
                face.C = DisconnectedPoints[indexC];

                Faces.Add(face);
                consumedPoints += 3;
            }

            DisconnectedPoints = DisconnectedPoints.Skip(consumedPoints).ToList();
        }

        public void CreateQuadFromDisconnectedPoints()
        {
            int consumedPoints = 0;
            for (int i = 0; i < DisconnectedPoints.Count; i += 4)
            {
                int indexA = i + 0;
                int indexB = i + 1;
                int indexC = i + 2;
                int indexD = i + 3;

                if (indexD >= DisconnectedPoints.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = DisconnectedPoints[indexA];
                face.B = DisconnectedPoints[indexB];
                face.C = DisconnectedPoints[indexC];

                Faces.Add(face);

                ManaGeometryFace face2 = new ManaGeometryFace();
                face2.Geom = this;
                face2.A = DisconnectedPoints[indexA];
                face2.B = DisconnectedPoints[indexC];
                face2.C = DisconnectedPoints[indexD];

                Faces.Add(face2);

                consumedPoints += 4;
            }

            DisconnectedPoints = DisconnectedPoints.Skip(consumedPoints).ToList();
        }

        public void CreateFacesFromQuad(ManaGeometryVertex[] verts)
        {
            CreateFacesFromQuad(verts[0], verts[1], verts[2], verts[3]);
        }
         
        public void CreateFacesFromQuad(ManaGeometryVertex a, ManaGeometryVertex b, ManaGeometryVertex c, ManaGeometryVertex d)
        {
            ManaGeometryFace face1 = new ManaGeometryFace();
            face1.Geom = this;
            face1.A = a; 
            face1.B = b; 
            face1.C = c;
            Faces.Add(face1);

            ManaGeometryFace face2 = new ManaGeometryFace();
            face2.Geom = this;
            face2.A = a;
            face2.B = c;
            face2.C = d;
            Faces.Add(face2);

            DisconnectedPoints.RemoveAll((item) => { return item == a || item == b || item == c || item == d; });
        }

        internal void AddFaces(List<uint> indexes)
        {
            for (int i = 0; i < indexes.Count; i += 3)
            {
                int indexA = (int)(indexes[i + 0]);
                int indexB = (int)(indexes[i + 1]);
                int indexC = (int)(indexes[i + 2]);

                if (indexC >= Verts.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = Verts[indexA];
                face.B = Verts[indexB];
                face.C = Verts[indexC];

                Faces.Add(face); 
            }
        }

        public List<uint> Indexes 
        { 
            get
            {
                List<uint> output = new List<uint>();

                foreach (var face in Faces)
                {
                    output.Add((uint)face.A.Index);
                    output.Add((uint)face.B.Index);
                    output.Add((uint)face.C.Index);
                }

                return output;
            }
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
}
