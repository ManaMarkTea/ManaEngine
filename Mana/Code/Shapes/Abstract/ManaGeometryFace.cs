namespace Mana.Code
{
    public class ManaGeometryFace
    {
        public ManaDynamicGeometryContainer Geom = null!;

        public ManaGeometryVertex A = null!;
        public ManaGeometryVertex B = null!;
        public ManaGeometryVertex C = null!;

        public Vector3 Normal;

        // Used to be "AssociateNeighbors": also populated A/B/C.Neighbors, A/B/C.Face, per-vertex .Edges,
        // per-edge .Faces, and Geom.Edges (3 new ManaGeometryEdge allocations per call). Confirmed via a
        // full-repo search that nothing anywhere ever reads any of that -- Normal is the only output any
        // caller consumes (ManaDynamicGeometryContainer.CreateQuadsFromVerts's Quad.Normal). For a large
        // grid mesh (e.g. a ~700x700 up-res'd planet patch, ~490K quads) that dead bookkeeping was ~1.5M
        // wasted ManaGeometryEdge allocations plus several million List<T>.Add calls per rebuild -- real
        // GC/allocation cost for output nothing used. Renamed to match what it actually does now.
        public void ComputeNormal()
        {
            Normal = (A.Normal + B.Normal + C.Normal) / 3.0f;
        }

        public override string ToString()
        {
            return $"{A.Index} + {B.Index} + {C.Index}";
        }

    }
}
