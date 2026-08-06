using System.Xml.Linq;
using Mana.Code;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public sealed class KmlLinearRing : KmlGeometry
    {
        public KmlCoordinates Coordinates { get; }
        public bool Extrude { get; set; }

        public KmlLinearRing(XElement element) : base(element)
        {
            var coordinatesElement = element.LocalElement("coordinates")
                ?? throw new InvalidOperationException("KML LinearRing missing <coordinates>.");
            Coordinates = new KmlCoordinates(coordinatesElement);
        }

        public override ManaMesh? BuildMesh(ManaContext context)
        {
            var mesh = new KmlLinearRingMesh(context, this);

            // Degenerate rings (most commonly a "ring" with only 2 distinct points, repeated to look
            // closed — see PolygonTriangulator/KmlLinearRingMesh) triangulate to zero triangles and
            // leave the mesh with no vertices at all. Returning it anyway would add a MeshRendererComponent
            // whose ManaMesh.GetOrCreateCpuData() throws InvalidOperationException every single frame
            // (CollectRenderMeshInstances catches it, but throwing+catching per node per frame is very
            // expensive, especially under an attached debugger) — return null instead, exactly like
            // KmlPoint/KmlLineString already do for "no renderable geometry", so KmlSceneBuilder falls
            // back to its existing plain mesh-less node path.
            return mesh.DynamicGeometryContainer.Verts.Count > 0 ? mesh : null;
        }

        public override LatLong GetCenterPoint()
        {
            var center = LatLong.FromLongitudeAndLatitude(0, 0);
            float total = 0;
            foreach (var coord in Coordinates.DataLatLong)
            {
                center.Add(coord);
                total += 1;
            }

            if (total == 0)
            {
                total = 1;
            }

            center.Divide(total);
            return center;
        }
    }
}
