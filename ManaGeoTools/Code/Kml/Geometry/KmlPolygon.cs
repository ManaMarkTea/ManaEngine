using System.Xml.Linq;
using Mana.Code;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public sealed class KmlPolygon : KmlGeometry
    {
        // Whether to connect the Polygon to the ground; carried through but unused (the source
        // never consumes it beyond passing it to OuterBoundary, which also doesn't act on it).
        public bool Extrude { get; }
        public KmlAltitudeModes AltitudeMode { get; } = KmlAltitudeModes.Absolute;

        public KmlLinearRing OuterBoundary { get; }

        // Never populated — the source declares InnerBoundaries but never parses/cuts holes from
        // them. Preserved as an always-empty list rather than invented hole-cutting logic.
        public List<KmlLinearRing> InnerBoundaries { get; } = new();

        public KmlPolygon(XElement element) : base(element)
        {
            var outerBoundaryIsElement = element.LocalElement("outerBoundaryIs")
                ?? throw new InvalidOperationException("KML Polygon missing <outerBoundaryIs>.");
            var linearRingElement = outerBoundaryIsElement.LocalElement("LinearRing")
                ?? throw new InvalidOperationException("KML Polygon <outerBoundaryIs> missing <LinearRing>.");
            OuterBoundary = new KmlLinearRing(linearRingElement);

            var extrudeElement = element.LocalElement("extrude");
            if (extrudeElement != null)
            {
                Extrude = ParseBool(extrudeElement.Value);
                OuterBoundary.Extrude = Extrude;
            }
        }

        public override ManaMesh? BuildMesh(ManaContext context)
        {
            return BuildMesh(context, style: null);
        }

        public override ManaMesh? BuildMesh(ManaContext context, KmlStyle? style)
        {
            return OuterBoundary.BuildMesh(context, style);
        }

        public override LatLong GetCenterPoint()
        {
            return OuterBoundary.GetCenterPoint();
        }
    }
}
