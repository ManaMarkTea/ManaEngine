using System.Xml.Linq;
using ManaGeoTools.Code.Kml.Geometry;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlPlacemark : KmlElement
    {
        public string StyleUrl { get; } = string.Empty;
        public KmlGeometry? Geometry { get; }
        public LatLong Coordinates { get; }

        private static readonly string[] PossibleGeometryElementNames =
        {
            "Point", "LineString", "LinearRing", "Polygon", "MultiGeometry", "Model",
        };

        public KmlPlacemark(XElement element) : base(element)
        {
            var styleUrlElement = element.LocalElement("styleUrl");
            if (styleUrlElement != null)
            {
                StyleUrl = styleUrlElement.Value;
            }

            XElement? geometryElement = null;
            foreach (var possibleGeo in PossibleGeometryElementNames)
            {
                var geo = element.LocalElement(possibleGeo);
                if (geo != null)
                {
                    geometryElement = geo;
                    break;
                }
            }

            Geometry = geometryElement != null ? KmlGeometry.Create(geometryElement) : null;

            // The source's Kml_Geometry.Create has no "Model" case either, so a Model placemark
            // leaves Geometry null here just like it does there — fall back to (0,0) rather than
            // the source's unguarded null-dereference crash on GetCenterPoint().
            Coordinates = Geometry?.GetCenterPoint() ?? LatLong.FromLongitudeAndLatitude(0, 0);
        }
    }
}
