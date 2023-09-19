namespace Mana.Code
{
    public class ManaGeometryQuad
    {
        public ManaDynamicGeometryContainer Geom;

        public ManaGeometryVertex A;
        public ManaGeometryVertex B;
        public ManaGeometryVertex C;
        public ManaGeometryVertex D;

        public ManaGeometryFace Face1;
        public ManaGeometryFace Face2;

        public Vector3 Normal;

        public Vector3 Center
        {
            get
            {
                return (A.Position + B.Position + C.Position + D.Position) / 4.0f;
            }
        }

        public override string ToString()
        {
            return $"{A.Index} + {B.Index} + {C.Index} + {D.Index}";

        }

    }
}
