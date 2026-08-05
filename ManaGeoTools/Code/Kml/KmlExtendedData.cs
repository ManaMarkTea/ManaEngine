using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlExtendedData : KmlElement
    {
        public KmlSchemaData? SchemaData { get; }

        public KmlExtendedData(XElement element) : base(element)
        {
            var schemaDataElement = element.LocalElement("SchemaData");
            if (schemaDataElement != null)
            {
                SchemaData = new KmlSchemaData(schemaDataElement);
            }
        }
    }
}
