using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml.Geometry
{
    // A single tuple consisting of longitude, latitude, and altitude (in that order); a marker,
    // not drawn.
    public sealed class KmlPoint : KmlGeometry
    {
        public KmlCoordinates Coordinates { get; }

        public KmlPoint(XElement element) : base(element)
        {
            var coordinatesElement = element.LocalElement("coordinates")
                ?? throw new InvalidOperationException("KML Point missing <coordinates>.");
            Coordinates = new KmlCoordinates(coordinatesElement);
        }

        public override LatLong GetCenterPoint()
        {
            return Coordinates.DataLatLong[0];
        }
    }
}
