using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlFolder : KmlElement
    {
        public KmlRegion? Region { get; }
        public List<KmlPlacemark> PlaceMarks { get; } = new();

        public KmlFolder(XElement element) : base(element)
        {
            var regionElement = element.LocalElement("Region");
            if (regionElement != null)
            {
                Region = new KmlRegion(regionElement);
            }

            foreach (var placemarkElement in element.LocalElements("Placemark"))
            {
                PlaceMarks.Add(new KmlPlacemark(placemarkElement));
            }
        }
    }
}
