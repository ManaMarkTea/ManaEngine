using System.Globalization;
using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlLatLonAltBox : KmlElement
    {
        public float North { get; }
        public float South { get; }
        public float East { get; }
        public float West { get; }
        public float MinAltitude { get; }
        public float MaxAltitude { get; }

        public KmlLatLonAltBox(XElement element) : base(element)
        {
            North = ParseFloat(element, "north");
            South = ParseFloat(element, "south");
            East = ParseFloat(element, "east");
            West = ParseFloat(element, "west");
            MinAltitude = ParseFloat(element, "minAltitude");
            MaxAltitude = ParseFloat(element, "maxAltitude");
        }

        private static float ParseFloat(XElement element, string childName)
        {
            var child = element.LocalElement(childName);
            return child != null ? float.Parse(child.Value, CultureInfo.InvariantCulture) : 0f;
        }
    }
}
