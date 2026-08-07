using Mana.Code;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Kml.Geometry
{
    internal static class KmlLineMeshFactory
    {
        public readonly record struct RenderStyle(Vector4 Color, float Thickness, float EmissionStrength);

        private static readonly Vector4 DefaultLineColor = new(0.35f, 0.82f, 1.0f, 1.0f);
        private const float DefaultStyleWidth = 3.0f;
        private const float ThicknessPerStyleWidth = 0.018f;
        private const float MinThickness = 0.035f;
        private const float MaxThickness = 0.12f;
        private const float EmissionStrength = 1.6f;
        private const float DuplicatePointEpsilonSq = 0.0001f * 0.0001f;
        private const float MinSegmentDegrees = 0.025f;

        public static ManaLineMesh? Build(
            ManaContext context,
            IReadOnlyList<Vector3> points,
            bool closed,
            KmlStyle? style)
        {
            if (!TryCreatePolyline(points, closed, out var line, out _))
            {
                return null;
            }

            var renderStyle = ResolveRenderStyle(style);
            return new ManaLineMesh(context, new[] { line }, renderStyle.Thickness, renderStyle.Color, renderStyle.EmissionStrength, ManaLineProfile.Ribbon);
        }

        public static RenderStyle ResolveRenderStyle(KmlStyle? style)
        {
            var color = style?.LineStyle?.Color ?? style?.PolyStyle?.Color ?? DefaultLineColor;
            var styleWidth = MathF.Max(style?.LineStyle?.Width ?? DefaultStyleWidth, 1.0f);
            var thickness = Math.Clamp(styleWidth * ThicknessPerStyleWidth, MinThickness, MaxThickness);
            return new RenderStyle(color, thickness, EmissionStrength);
        }

        public static bool TryCreatePolyline(
            IReadOnlyList<Vector3> points,
            bool closed,
            out ManaLinePolyline line,
            out int estimatedIndexCount)
        {
            var cleanPoints = DeduplicateAndDecimatePoints(points, closed);
            if (cleanPoints.Count < 2)
            {
                line = default;
                estimatedIndexCount = 0;
                return false;
            }

            // Some KML exports encode line-like boundaries as invalid LinearRings with only two
            // distinct points. They cannot make a real closed loop, but they can draw as useful open
            // boundaries.
            var renderClosed = closed && cleanPoints.Count >= 3;
            line = new ManaLinePolyline(cleanPoints, renderClosed);
            var connectionCount = renderClosed ? cleanPoints.Count : cleanPoints.Count - 1;
            estimatedIndexCount = connectionCount * 6;
            return true;
        }

        private static List<Vector3> DeduplicateAndDecimatePoints(IReadOnlyList<Vector3> points, bool closed)
        {
            var output = new List<Vector3>(points.Count);
            var cosThreshold = MathF.Cos(MinSegmentDegrees * MathF.PI / 180.0f);
            foreach (var point in points)
            {
                if (output.Count > 0 && Vector3.DistanceSquared(output[^1], point) <= DuplicatePointEpsilonSq)
                {
                    continue;
                }

                if (output.Count == 0 || ShouldKeepPoint(output[^1], point, cosThreshold))
                {
                    output.Add(point);
                }
            }

            if (closed && output.Count > 1 && Vector3.DistanceSquared(output[0], output[^1]) <= DuplicatePointEpsilonSq)
            {
                output.RemoveAt(output.Count - 1);
            }

            return output;
        }

        private static bool ShouldKeepPoint(Vector3 previous, Vector3 point, float cosThreshold)
        {
            if (previous.LengthSquared() <= DuplicatePointEpsilonSq || point.LengthSquared() <= DuplicatePointEpsilonSq)
            {
                return Vector3.DistanceSquared(previous, point) > DuplicatePointEpsilonSq;
            }

            return Vector3.Dot(Vector3.Normalize(previous), Vector3.Normalize(point)) < cosThreshold;
        }
    }
}
