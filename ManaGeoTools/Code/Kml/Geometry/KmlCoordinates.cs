using System.Globalization;
using System.Xml.Linq;

namespace ManaGeoTools.Code.Kml.Geometry
{
    public sealed class KmlCoordinates : KmlElement
    {
        public List<Vector3> Data { get; } = new();
        public List<LatLong> DataLatLong { get; } = new();

        public KmlCoordinates(XElement element) : base(element)
        {
            var lines = element.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 3)
                {
                    continue;
                }

                var longitude = float.Parse(parts[0], CultureInfo.InvariantCulture);
                var latitude = float.Parse(parts[1], CultureInfo.InvariantCulture);
                var altitude = float.Parse(parts[2], CultureInfo.InvariantCulture);

                var latLong = LatLong.FromLongitudeAndLatitude(longitude, latitude);
                latLong.Altitude = altitude;

                Data.Add(latLong.ToSphere(Kml.EarthRadius, Kml.AltitudeScale));
                DataLatLong.Add(latLong);
            }
        }
    }
}
