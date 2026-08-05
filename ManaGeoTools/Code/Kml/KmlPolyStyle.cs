using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlPolyStyle : KmlColorStyle
    {
        public bool Fill { get; private set; } = true;
        public bool Outline { get; private set; } = true;

        public KmlPolyStyle(XElement element) : base(element)
        {
            var fillElement = element.LocalElement("fill");
            if (fillElement != null)
            {
                Fill = ParseBool(fillElement.Value);
            }

            var outlineElement = element.LocalElement("outline");
            if (outlineElement != null)
            {
                Outline = ParseBool(outlineElement.Value);
            }
        }
    }
}
