using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public class KmlColorStyle : KmlElement
    {
        public Vector4 Color { get; protected set; } = new Vector4(1, 1, 1, 1);
        public string ColorMode { get; protected set; } = string.Empty;

        public KmlColorStyle(XElement element) : base(element)
        {
            var colorElement = element.LocalElement("color");
            if (colorElement != null)
            {
                Color = ParseColor(colorElement.Value);
            }
        }
    }
}
