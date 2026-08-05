using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml.Geometry
{
    // Data-only, matching the source: Kml_LineString.gd never implements a mesh/tube renderer for
    // it either (BuildMesh stays null, inherited from KmlGeometry). Left as a known gap, not
    // invented here.
    public sealed class KmlLineString : KmlGeometry
    {
        public KmlCoordinates Coordinates { get; }

        public KmlLineString(XElement element) : base(element)
        {
            var coordinatesElement = element.LocalElement("coordinates")
                ?? throw new InvalidOperationException("KML LineString missing <coordinates>.");
            Coordinates = new KmlCoordinates(coordinatesElement);
        }
    }
}
