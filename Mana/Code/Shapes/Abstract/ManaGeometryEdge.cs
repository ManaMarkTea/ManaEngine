namespace Mana.Code
{
    public class ManaGeometryEdge
    {
        public ManaDynamicGeometryContainer Geom = null!;
        public List<ManaGeometryFace> Faces = new List<ManaGeometryFace>();

        public ManaGeometryVertex A = null!;
        public ManaGeometryVertex B = null!;

        public override string ToString()
        {
            return $"{A.Index} -> {B.Index} [{A.Position} -> {B.Position}]";
        }

    }
}
