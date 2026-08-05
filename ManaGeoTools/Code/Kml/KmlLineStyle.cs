using System.Globalization;
using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlLineStyle : KmlColorStyle
    {
        public float Width { get; private set; } = 1.0f;
        public Vector4 OuterColor { get; private set; } = new Vector4(1, 1, 1, 1);
        public float OuterWidth { get; private set; }
        public float PhysicalWidth { get; private set; }
        public bool LabelVisibility { get; private set; }

        public KmlLineStyle(XElement element) : base(element)
        {
            var widthElement = element.LocalElement("width");
            if (widthElement != null)
            {
                Width = float.Parse(widthElement.Value, CultureInfo.InvariantCulture);
            }

            var outerWidthElement = element.LocalElement("outerWidth");
            if (outerWidthElement != null)
            {
                OuterWidth = float.Parse(outerWidthElement.Value, CultureInfo.InvariantCulture);
            }

            var physicalWidthElement = element.LocalElement("physicalWidth");
            if (physicalWidthElement != null)
            {
                PhysicalWidth = float.Parse(physicalWidthElement.Value, CultureInfo.InvariantCulture);
            }

            var labelVisibilityElement = element.LocalElement("labelVisibility");
            if (labelVisibilityElement != null)
            {
                LabelVisibility = ParseBool(labelVisibilityElement.Value);
            }

            var outerColorElement = element.LocalElement("outerColor");
            if (outerColorElement != null)
            {
                OuterColor = ParseColor(outerColorElement.Value);
            }
        }
    }
}
