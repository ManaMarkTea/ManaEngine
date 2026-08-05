using System.Globalization;

namespace ManaGeoTools.Code
{
    // Based on https://github.com/bbonenfant/dted/blob/primary/dted/latlon.py
    public class LatLong
    {
        private float _longitude;
        private float _latitude;

        public float Longitude
        {
            get => _longitude;
            set
            {
                _longitude = value;
                LongitudeString = ToDms(value, "E", "W");
            }
        }

        public float Latitude
        {
            get => _latitude;
            set
            {
                _latitude = value;
                LatitudeString = ToDms(value, "N", "S");
            }
        }

        public float Altitude { get; set; }

        public string LongitudeString { get; private set; } = string.Empty;
        public string LatitudeString { get; private set; } = string.Empty;

        public LatLong(float longitude, float latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }

        public LatLong Added(LatLong rhs)
        {
            var output = FromLatLong(this);
            output.Longitude += rhs.Longitude;
            output.Latitude += rhs.Latitude;
            return output;
        }

        public LatLong Subtracted(LatLong rhs)
        {
            var output = FromLatLong(this);
            output.Longitude -= rhs.Longitude;
            output.Latitude -= rhs.Latitude;
            return output;
        }

        public static LatLong FromLatLong(LatLong latLong)
        {
            var copy = new LatLong(latLong.Longitude, latLong.Latitude);
            copy.LatitudeString = latLong.LatitudeString;
            copy.LongitudeString = latLong.LongitudeString;
            return copy;
        }

        // Using Longitude and latitude in degrees
        public static LatLong FromLongitudeAndLatitude(float longitude, float latitude)
        {
            return new LatLong(longitude, latitude);
        }

        public static LatLong FromPointOnSphere(Vector3 point, float radius, float altitudeScale)
        {
            var mag = point.Length();
            var pointOnUnitSphere = point / mag;
            var longitude = MathF.Atan2(-pointOnUnitSphere.X, -pointOnUnitSphere.Z);
            var latitude = MathF.Asin(pointOnUnitSphere.Y);
            var altitude = mag - radius;
            altitude *= altitudeScale;
            var copy = new LatLong(ToDegrees(longitude), ToDegrees(latitude));
            copy.Altitude = altitude;
            return copy;
        }

        public bool Add(LatLong? otherLatLong)
        {
            if (otherLatLong == null)
            {
                return false;
            }
            Latitude += otherLatLong.Latitude;
            Longitude += otherLatLong.Longitude;
            Altitude += otherLatLong.Altitude;
            return true;
        }

        public bool Divide(float scalar)
        {
            Latitude /= scalar;
            Longitude /= scalar;
            Altitude /= scalar;
            return true;
        }

        public bool Equals(LatLong? otherLatLong)
        {
            if (otherLatLong == null)
            {
                return false;
            }
            return LatitudeString == otherLatLong.LatitudeString && LongitudeString == otherLatLong.LongitudeString;
        }

        public Vector3 ToSphere(float radius, float altitudeScale)
        {
            var output = ToUnitSphere();
            output *= radius + (Altitude * altitudeScale);
            return output;
        }

        public Vector3 ToUnitSphere()
        {
            var latitudeRad = ToRadians(Latitude);
            var longitudeRad = ToRadians(Longitude);
            var y = MathF.Sin(latitudeRad);
            var r = MathF.Cos(latitudeRad);
            var x = -MathF.Sin(longitudeRad) * r;
            var z = -MathF.Cos(longitudeRad) * r;
            return new Vector3(x, y, z);
        }

        // Return a Vector2 containing long/lat remapped to range [0,1]
        public Vector2 ToUv()
        {
            var u = (ToRadians(Longitude) / MathF.PI + 1.0f) * 0.5f;
            var v = -ToRadians(Latitude) / MathF.PI + 0.5f;
            return new Vector2(u, v);
        }

        private static string ToDms(float dd, string positiveLetter, string negativeLetter)
        {
            var dd1 = MathF.Abs(dd);
            var cdeg = (int)dd1;
            var minsec = dd1 - cdeg;
            var cmin = (int)(minsec * 60);
            var csec = (int)(minsec * 3600.0f % 60.0f);

            var letter = positiveLetter;
            if (dd < 0)
            {
                letter = negativeLetter;
            }

            return cdeg.ToString("D3", CultureInfo.InvariantCulture) + "° " +
                   cmin.ToString("D2", CultureInfo.InvariantCulture) + "' " +
                   csec.ToString("D2", CultureInfo.InvariantCulture) + "\" " + letter;
        }

        public string ToPrettyString()
        {
            return LongitudeString + ", " + LatitudeString;
        }

        public override string ToString()
        {
            return Longitude.ToString(CultureInfo.InvariantCulture) + "x" + Latitude.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Converts a degree-minute-second coordinate to a decimal-degree coordinate.</summary>
        public static float DmsToDecimal(int degree, int minute, float second)
        {
            return degree + ((minute + (second / 60.0f)) / 60.0f);
        }

        /// <summary>
        /// Parse a degree-minute-second coordinate from a DTED coordinate string.
        /// The DTED coordinate string has the following format: [D]DDMMSS[.S]
        /// where D = Degree, M = Minute, S = Second (without the hemisphere identifier).
        /// </summary>
        public static (int Degree, int Minute, float Second) ParseDmsCoordinate(string coordinate)
        {
            var secondsIndex = coordinate[^2] == '.' ? coordinate.Length - 4 : coordinate.Length - 2;
            var minutesIndex = secondsIndex - 2;
            var degreeStr = coordinate.Substring(0, minutesIndex);
            var minuteStr = coordinate.Substring(minutesIndex, secondsIndex - minutesIndex);
            var secondStr = coordinate.Substring(secondsIndex);

            var degrees = int.Parse(degreeStr, CultureInfo.InvariantCulture);
            var minutes = int.Parse(minuteStr, CultureInfo.InvariantCulture);
            var seconds = float.Parse(secondStr, CultureInfo.InvariantCulture);
            return (degrees, minutes, seconds);
        }

        /// <summary>
        /// Constructs a LatLong from DTED record coordinates in the format ([D]DDMMSS[.S]H) where
        /// D: degree value, M: minute value, S: second value, H: hemisphere.
        /// </summary>
        public static LatLong FromDted(string longitudeStr, string latitudeStr)
        {
            var latSign = latitudeStr[^1] == 'S' ? -1 : 1;
            var latParts = ParseDmsCoordinate(latitudeStr.Substring(0, latitudeStr.Length - 1));
            var latitude = latSign * DmsToDecimal(latParts.Degree, latParts.Minute, latParts.Second);

            var lonSign = longitudeStr[^1] == 'W' ? -1 : 1;
            var lonParts = ParseDmsCoordinate(longitudeStr.Substring(0, longitudeStr.Length - 1));
            var longitude = lonSign * DmsToDecimal(lonParts.Degree, lonParts.Minute, lonParts.Second);

            return new LatLong(longitude, latitude);
        }

        private static float ToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
        private static float ToDegrees(float radians) => radians * (180.0f / MathF.PI);
    }
}
