using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlIconStyle : KmlColorStyle
    {
        public float Scale { get; } = 1.0f;
        public float Heading { get; } = 0.0f;
        public string Icon { get; } = string.Empty;
        public Vector2 HotSpot { get; }

        public KmlIconStyle(XElement element) : base(element)
        {
        }
    }
}
