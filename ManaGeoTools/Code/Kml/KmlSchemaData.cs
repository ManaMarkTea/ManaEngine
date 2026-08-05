using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlSchemaData : KmlElement
    {
        public string SchemaUrl { get; } = string.Empty;
        public List<KmlSimpleData> SimpleData { get; } = new();

        public KmlSchemaData(XElement element) : base(element)
        {
            if (element.HasLocalAttribute("schemaUrl"))
            {
                SchemaUrl = element.LocalAttribute("schemaUrl");
            }

            foreach (var childElement in element.LocalElements("SimpleData"))
            {
                SimpleData.Add(new KmlSimpleData(childElement));
            }
        }
    }
}
