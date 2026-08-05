using System.Globalization;
using System.Xml.Linq;
using ManaGeoTools.Code.Xml;

namespace ManaGeoTools.Code.Osm
{
    public sealed class OsmNode : OsmElement
    {
        public LatLong? Location { get; }

        // Populated externally by OsmWay while it walks <nd> references, not self-populated.
        public List<OsmNode> Connections { get; } = new();

        public OsmNode(XElement element) : base(element)
        {
            if (element.HasLocalAttribute("lat") && element.HasLocalAttribute("lon"))
            {
                var lat = float.Parse(element.LocalAttribute("lat"), CultureInfo.InvariantCulture);
                var lon = float.Parse(element.LocalAttribute("lon"), CultureInfo.InvariantCulture);
                Location = LatLong.FromLongitudeAndLatitude(lon, lat);

                if (Tags.TryGetValue("ele", out var ele) &&
                    float.TryParse(ele, NumberStyles.Float, CultureInfo.InvariantCulture, out var eleValue))
                {
                    Location.Altitude = eleValue;
                }
            }
        }
    }
}
