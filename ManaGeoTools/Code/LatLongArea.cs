namespace ManaGeoTools.Code
{
    public class LatLongArea
    {
        public LatLong UpperLeft;
        public LatLong UpperRight;
        public LatLong LowerLeft;
        public LatLong LowerRight;

        public Vector2 PixelSize;

        // areaWidth/areaHeight replace Godot's Vector2i; latLongPerPixel only uses X/Y.
        public LatLongArea(LatLong originPosition, int areaWidth, int areaHeight, Vector3 latLongPerPixel)
        {
            LowerLeft = LatLong.FromLatLong(originPosition);

            UpperRight = LatLong.FromLatLong(originPosition);
            UpperRight.Latitude += areaHeight * latLongPerPixel.Y;
            UpperRight.Longitude += areaWidth * latLongPerPixel.X;

            UpperLeft = LatLong.FromLatLong(originPosition);
            UpperLeft.Latitude += areaHeight * latLongPerPixel.Y;

            LowerRight = LatLong.FromLatLong(originPosition);
            LowerRight.Longitude = UpperRight.Longitude;
            LowerRight.Latitude = LowerLeft.Latitude;
        }

        public LatLong Lerp(float tLat, float tLong)
        {
            var outLong = float.Lerp(UpperLeft.Longitude, LowerRight.Longitude, tLong);
            var outLat = float.Lerp(UpperLeft.Latitude, LowerRight.Latitude, tLat);
            return LatLong.FromLongitudeAndLatitude(outLong, outLat);
        }

        public bool Contains(LatLong point)
        {
            var horizontalContains = point.Longitude >= UpperLeft.Longitude && point.Longitude <= LowerRight.Longitude;
            var verticalContains = point.Latitude >= LowerLeft.Latitude && point.Latitude <= UpperRight.Latitude;
            return horizontalContains && verticalContains;
        }

        public override string ToString()
        {
            return "[" + UpperLeft.LongitudeString + ", " + UpperLeft.LatitudeString + "]" + " -> " +
                   "[" + LowerRight.LongitudeString + ", " + LowerRight.LatitudeString + "]";
        }
    }
}
