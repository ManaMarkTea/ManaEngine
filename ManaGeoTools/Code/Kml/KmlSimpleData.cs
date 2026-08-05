using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlSimpleData : KmlElement
    {
        public string Content { get; }

        public KmlSimpleData(XElement element) : base(element)
        {
            Content = element.Value;
        }
    }
}
