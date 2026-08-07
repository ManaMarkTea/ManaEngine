using Mana.Code;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Converts country border rings (lat/long, as read by ShapefilePolygonReader) into a
    /// ManaLineMesh projected onto a sphere. The line-rendering itself
    /// lives in the generic ManaLineMesh (Mana engine layer) -- this class only does the
    /// planet/geo-specific part: decimating a lat/long ring (a minimum-arc-spacing filter doesn't mean
    /// anything for an arbitrary 3D polyline, only for one parameterized by lat/long) and projecting
    /// the kept points onto a sphere of the given radius.
    /// </summary>
    public static class CountryBorderLines
    {
        public static ManaLineMesh Build(
            ManaContext context,
            IReadOnlyList<IReadOnlyList<(double Longitude, double Latitude)>> rings,
            float radius,
            float thickness,
            float minSegmentDegrees,
            Vector4 color,
            float emissionStrength)
        {
            var cosThreshold = MathF.Cos(minSegmentDegrees * MathF.PI / 180.0f);
            var lines = new List<ManaLinePolyline>();

            foreach (var ring in rings)
            {
                var decimated = DecimateRing(ring, cosThreshold);
                if (decimated.Count < 2)
                {
                    continue;
                }

                var points = new Vector3[decimated.Count];
                for (var i = 0; i < decimated.Count; i++)
                {
                    points[i] = ToSpherePosition(decimated[i], radius);
                }

                // Every ring is a closed loop -- ManaLineMesh wraps the last point back to the first.
                lines.Add(new ManaLinePolyline(points, closed: true));
            }

            return new ManaLineMesh(context, lines, thickness, color, emissionStrength, ManaLineProfile.Ribbon);
        }

        private static Vector3 ToSpherePosition((double Longitude, double Latitude) point, float radius)
        {
            var latLong = LatLong.FromLongitudeAndLatitude((float)point.Longitude, (float)point.Latitude);
            return latLong.ToUnitSphere() * radius;
        }

        // Keeps a point only once it's more than the threshold's great-circle angle away from the
        // last kept point -- at 50m resolution this dataset is ~100K raw points, most of it far more
        // coastline/border detail than reads as anything but noise from a whole-planet view. Always
        // keeps the first point; ManaLineMesh wraps back to it to close the ring, so the last kept
        // point doesn't need special-casing here.
        private static List<(double Longitude, double Latitude)> DecimateRing(IReadOnlyList<(double Longitude, double Latitude)> ring, float cosThreshold)
        {
            if (ring.Count <= 3)
            {
                return new List<(double, double)>(ring);
            }

            var result = new List<(double, double)> { ring[0] };
            var lastKept = LatLong.FromLongitudeAndLatitude((float)ring[0].Longitude, (float)ring[0].Latitude).ToUnitSphere();

            for (var i = 1; i < ring.Count; i++)
            {
                var point = ring[i];
                var v = LatLong.FromLongitudeAndLatitude((float)point.Longitude, (float)point.Latitude).ToUnitSphere();
                if (Vector3.Dot(lastKept, v) < cosThreshold)
                {
                    result.Add(point);
                    lastKept = v;
                }
            }

            return result;
        }
    }
}
