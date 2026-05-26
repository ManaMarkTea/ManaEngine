namespace Mana.Code
{
    public class ManaGeometryQuad
    {
        public ManaDynamicGeometryContainer Geom = null!;

        public ManaGeometryVertex A = null!;
        public ManaGeometryVertex B = null!;
        public ManaGeometryVertex C = null!;
        public ManaGeometryVertex D = null!;

        public ManaGeometryFace Face1 = null!;
        public ManaGeometryFace Face2 = null!;

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
