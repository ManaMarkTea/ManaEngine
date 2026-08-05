using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml
{
    public sealed class KmlStyle : KmlElement
    {
        public KmlBalloonStyle? BalloonStyle { get; private set; }
        public KmlIconStyle? IconStyle { get; private set; }
        public KmlLabelStyle? LabelStyle { get; private set; }
        public KmlLineStyle? LineStyle { get; private set; }
        public KmlListStyle? ListStyle { get; private set; }
        public KmlPolyStyle? PolyStyle { get; private set; }

        public KmlStyle(XElement element) : base(element)
        {
            foreach (var childElement in element.Elements())
            {
                switch (childElement.Name.LocalName)
                {
                    case "BalloonStyle":
                        BalloonStyle = new KmlBalloonStyle(childElement);
                        break;
                    case "IconStyle":
                        IconStyle = new KmlIconStyle(childElement);
                        break;
                    case "LabelStyle":
                        LabelStyle = new KmlLabelStyle(childElement);
                        break;
                    case "LineStyle":
                        LineStyle = new KmlLineStyle(childElement);
                        break;
                    case "ListStyle":
                        ListStyle = new KmlListStyle(childElement);
                        break;
                    case "PolyStyle":
                        PolyStyle = new KmlPolyStyle(childElement);
                        break;
                }
            }
        }
    }
}
