using System.Xml.Linq;
using Mana.Code;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public sealed class KmlLineString : KmlGeometry
    {
        public KmlCoordinates Coordinates { get; }

        public KmlLineString(XElement element) : base(element)
        {
            var coordinatesElement = element.LocalElement("coordinates")
                ?? throw new InvalidOperationException("KML LineString missing <coordinates>.");
            Coordinates = new KmlCoordinates(coordinatesElement);
        }

        public override ManaMesh? BuildMesh(ManaContext context)
        {
            return BuildMesh(context, style: null);
        }

        public override ManaMesh? BuildMesh(ManaContext context, KmlStyle? style)
        {
            return KmlLineMeshFactory.Build(context, Coordinates.Data, closed: false, style);
        }
    }
}
