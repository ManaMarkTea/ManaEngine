using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Osm
{
    public class OsmElement
    {
        public string Id { get; protected set; } = string.Empty;
        public string Name { get; protected set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Attributes { get; }
        public Dictionary<string, string> Tags { get; } = new();

        public OsmElement(XElement element)
        {
            Attributes = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
            if (element.HasLocalAttribute("id"))
            {
                Id = element.LocalAttribute("id");
            }

            foreach (var tagElement in element.LocalElements("tag"))
            {
                var key = tagElement.LocalAttribute("k");
                var value = tagElement.LocalAttribute("v");
                Tags[key] = value;
            }

            if (Tags.TryGetValue("name", out var name))
            {
                Name = name;
            }

            if (Tags.TryGetValue("source:name", out var sourceName))
            {
                Name = sourceName;
            }
        }
    }
}
