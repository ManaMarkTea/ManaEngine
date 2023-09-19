namespace Mana.Code
{
    public class ManaGeometryFace
    {
        public ManaDynamicGeometryContainer Geom;

        public ManaGeometryVertex A;
        public ManaGeometryVertex B;
        public ManaGeometryVertex C;

        public Vector3 Normal;

        public void AssociateNeighbors()
        {
            A.Neighbors.Add(B);
            A.Neighbors.Add(C);
            B.Neighbors.Add(A);
            B.Neighbors.Add(C);
            C.Neighbors.Add(A);
            C.Neighbors.Add(B);
            A.Face = B.Face = C.Face = this;
            Normal = (A.Normal + B.Normal + C.Normal) / 3.0f;

            var newEdge1 = new ManaGeometryEdge { A = A, B = B, Geom = Geom };
            var newEdge2 = new ManaGeometryEdge { A = B, B = C, Geom = Geom };
            var newEdge3 = new ManaGeometryEdge { A = C, B = A, Geom = Geom };

            A.Edges.Add(newEdge1);
            B.Edges.Add(newEdge1);

            B.Edges.Add(newEdge2);
            C.Edges.Add(newEdge2);

            C.Edges.Add(newEdge3);
            A.Edges.Add(newEdge3);

            newEdge1.Faces.Add(this);
            newEdge2.Faces.Add(this);
            newEdge3.Faces.Add(this);

            Geom.Edges.Add(newEdge1);
            Geom.Edges.Add(newEdge2);
            Geom.Edges.Add(newEdge3);
        }

        public override string ToString()
        {
            return $"{A.Index} + {B.Index} + {C.Index}";
        }

    }
}
