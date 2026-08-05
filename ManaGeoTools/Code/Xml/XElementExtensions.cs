using System.Xml.Linq;

namespace ManaGeoTools.Code.Xml
{
    // KML documents typically declare a default namespace (e.g. http://www.opengis.net/kml/2.2),
    // which XElement.Element("name")/.Elements("name") silently fail to match. These helpers
    // compare by local name only so parsing is agnostic to whether a namespace is present.
    public static class XElementExtensions
    {
        public static XElement? LocalElement(this XElement parent, string localName)
        {
            return parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        }

        public static IEnumerable<XElement> LocalElements(this XElement parent, string localName)
        {
            return parent.Elements().Where(e => e.Name.LocalName == localName);
        }

        public static string LocalAttribute(this XElement element, string name, string defaultValue = "")
        {
            return element.Attribute(name)?.Value ?? defaultValue;
        }

        public static bool HasLocalAttribute(this XElement element, string name)
        {
            return element.Attribute(name) != null;
        }
    }
}
