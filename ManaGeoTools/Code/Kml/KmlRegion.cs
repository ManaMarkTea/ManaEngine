using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlRegion : KmlElement
    {
        public KmlLatLonAltBox? LatLonAltBox { get; }
        public KmlLod? Lod { get; }

        public KmlRegion(XElement element) : base(element)
        {
            var latLonAltBoxElement = element.LocalElement("LatLonAltBox");
            if (latLonAltBoxElement != null)
            {
                LatLonAltBox = new KmlLatLonAltBox(latLonAltBoxElement);
            }

            var lodElement = element.LocalElement("Lod");
            if (lodElement != null)
            {
                Lod = new KmlLod(lodElement);
            }
        }
    }
}
