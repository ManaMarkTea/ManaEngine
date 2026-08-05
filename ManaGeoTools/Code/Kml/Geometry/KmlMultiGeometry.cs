using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public sealed class KmlMultiGeometry : KmlGeometry
    {
        public List<KmlGeometry> Geometries { get; } = new();

        public KmlMultiGeometry(XElement element) : base(element)
        {
            foreach (var child in element.Elements())
            {
                var newChildGeo = Create(child);
                if (newChildGeo != null)
                {
                    Geometries.Add(newChildGeo);
                }
            }
        }

        public override LatLong GetCenterPoint()
        {
            var center = LatLong.FromLongitudeAndLatitude(0, 0);
            float total = 0;
            foreach (var geo in Geometries)
            {
                center.Add(geo.GetCenterPoint());
                total += 1.0f;
            }

            if (total == 0)
            {
                total = 1.0f;
            }

            center.Divide(total);
            return center;
        }
    }
}
