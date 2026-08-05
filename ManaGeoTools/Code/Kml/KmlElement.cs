using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public class KmlElement
    {
        public string Id { get; protected set; } = string.Empty;
        public string Name { get; protected set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Attributes { get; }
        public KmlExtendedData? ExtendedData { get; protected set; }

        public KmlElement(XElement element)
        {
            Attributes = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);

            if (element.HasLocalAttribute("id"))
            {
                Id = element.LocalAttribute("id");
            }

            if (element.HasLocalAttribute("name"))
            {
                Name = element.LocalAttribute("name");
            }

            foreach (var childElement in element.Elements())
            {
                if (childElement.Name.LocalName == "ExtendedData")
                {
                    ExtendedData = new KmlExtendedData(childElement);
                }

                if (childElement.Name.LocalName == "name")
                {
                    Name = childElement.Value;
                }
            }
        }

        protected static bool ParseBool(string kmlBoolString)
        {
            var boolString = kmlBoolString.ToLowerInvariant();
            return boolString == "1" || boolString == "true";
        }

        protected static Vector4 ParseColor(string kmlColorString)
        {
            // KML colors are aabbggrr hex. This engine has no Color type — everything is a Vector4.
            var aabbggrr = kmlColorString;
            var output = new Vector4(1, 1, 1, 1); // white fallback, matches source Color.WHITE
            if (aabbggrr.Length == 8)
            {
                var aa = Convert.ToByte(aabbggrr.Substring(0, 2), 16);
                var bb = Convert.ToByte(aabbggrr.Substring(2, 2), 16);
                var gg = Convert.ToByte(aabbggrr.Substring(4, 2), 16);
                var rr = Convert.ToByte(aabbggrr.Substring(6, 2), 16);
                output = new Vector4(rr / 255f, gg / 255f, bb / 255f, aa / 255f);
            }
            return output;
        }
    }
}
