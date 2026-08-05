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
            return new KmlLinearRingMesh(context, this);
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
