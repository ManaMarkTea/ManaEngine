using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlSchema : KmlElement
    {
        public string SchemaUrl { get; } = string.Empty;
        public Dictionary<string, string> SimpleFields { get; } = new();
        public Dictionary<string, string> SimpleFieldsDisplayNames { get; } = new();

        public KmlSchema(XElement element) : base(element)
        {
            foreach (var childElement in element.LocalElements("SimpleField"))
            {
                var type = childElement.LocalAttribute("type");
                var name = childElement.LocalAttribute("name");

                var displayNameElement = childElement.LocalElement("displayName");
                if (displayNameElement != null)
                {
                    SimpleFieldsDisplayNames[name] = displayNameElement.Value;
                }

                SimpleFields[name] = type;
            }
        }
    }
}
