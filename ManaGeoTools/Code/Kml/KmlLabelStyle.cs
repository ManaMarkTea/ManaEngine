using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlLabelStyle : KmlColorStyle
    {
        public float Scale { get; } = 1.0f;

        public KmlLabelStyle(XElement element) : base(element)
        {
        }
    }
}
