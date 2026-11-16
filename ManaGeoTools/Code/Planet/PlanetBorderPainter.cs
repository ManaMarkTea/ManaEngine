using ManaCore.Imaging;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Paints lat/long polylines into an equirectangular picture of the planet, antialiased, so borders sit on
    /// the terrain at no cost a frame. Widths are ground distances: a line keeps its width towards the poles,
    /// where a pixel of the picture covers less ground east to west.
    /// </summary>
    public static class PlanetBorderPainter
    {
        private const float EquatorKilometres = 40075.0f;

        /// <summary>
        /// Draws each ring (closed) <paramref name="widthKilometres"/> wide in <paramref name="color"/> (alpha is
        /// its strength). Overlapping lines take the stronger coverage rather than adding, so shared borders --
        /// a state's edge on its country's -- are drawn once.
        /// </summary>
        public static void Paint(
            ManaImage image,
            IEnumerable<IReadOnlyList<(double Longitude, double Latitude)>> rings,
            float widthKilometres,
            Vector4 color)
        {
            var width = image.Width;
            var height = image.Height;
            var coverage = new byte[width * height];
            var halfWidth = 0.5f * widthKilometres / (EquatorKilometres / width);
            foreach (var ring in rings)
            {
                for (var i = 0; i < ring.Count; i++)
                {
                    var (lon0, lat0) = ring[i];
                    var (lon1, lat1) = ring[(i + 1) % ring.Count];
                    var x0 = (float)((lon0 + 180.0) / 360.0 * width);
                    var y0 = (float)((90.0 - lat0) / 180.0 * height);
                    var x1 = (float)((lon1 + 180.0) / 360.0 * width);
                    var y1 = (float)((90.0 - lat1) / 180.0 * height);
                    // A segment across the date line is the ring closing round the back; it is not a border.
                    if (MathF.Abs(x1 - x0) > width * 0.5f)
                    {
                        continue;
                    }

                    var squeeze = MathF.Max(0.05f, MathF.Cos((float)((lat0 + lat1) * 0.5 * Math.PI / 180.0)));
                    PaintSegment(coverage, width, height, x0, y0, x1, y1, halfWidth, squeeze);
                }
            }

            Blend(image, coverage, color);
        }

        // In the picture an east-west pixel is `squeeze` as wide on the ground, so distances are measured with x
        // shrunk by it; the box searched widens to match.
        private static void PaintSegment(byte[] coverage, int width, int height, float x0, float y0, float x1, float y1, float halfWidth, float squeeze)
        {
            var reach = halfWidth + 1.0f;
            var minX = (int)MathF.Floor(MathF.Min(x0, x1) - reach / squeeze);
            var maxX = (int)MathF.Ceiling(MathF.Max(x0, x1) + reach / squeeze);
            var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, y1) - reach));
            var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(y0, y1) + reach));
            var ax = x0 * squeeze;
            var bx = x1 * squeeze;
            var dx = bx - ax;
            var dy = y1 - y0;
            var lengthSquared = dx * dx + dy * dy;
            for (var y = minY; y <= maxY; y++)
            {
                var py = y + 0.5f;
                for (var x = minX; x <= maxX; x++)
                {
                    var px = (x + 0.5f) * squeeze;
                    var t = 0.0f;
                    if (lengthSquared > 1e-12f)
                    {
                        t = Math.Clamp(((px - ax) * dx + (py - y0) * dy) / lengthSquared, 0.0f, 1.0f);
                    }

                    var ex = px - (ax + t * dx);
                    var ey = py - (y0 + t * dy);
                    var distance = MathF.Sqrt(ex * ex + ey * ey);
                    var amount = Math.Clamp(halfWidth + 0.5f - distance, 0.0f, 1.0f);
                    if (amount <= 0.0f)
                    {
                        continue;
                    }

                    var wrapped = ((x % width) + width) % width;
                    var index = y * width + wrapped;
                    var value = (byte)(amount * 255.0f + 0.5f);
                    if (value > coverage[index])
                    {
                        coverage[index] = value;
                    }
                }
            }
        }

        private static void Blend(ManaImage image, byte[] coverage, Vector4 color)
        {
            var pixels = image.Pixels;
            var r = color.X * 255.0f;
            var g = color.Y * 255.0f;
            var b = color.Z * 255.0f;
            Parallel.For(0, image.Height, y =>
            {
                var row = y * image.Width;
                for (var x = 0; x < image.Width; x++)
                {
                    var c = coverage[row + x];
                    if (c == 0)
                    {
                        continue;
                    }

                    var a = c / 255.0f * color.W;
                    var p = (row + x) * 4;
                    pixels[p] = (byte)(pixels[p] + (r - pixels[p]) * a + 0.5f);
                    pixels[p + 1] = (byte)(pixels[p + 1] + (g - pixels[p + 1]) * a + 0.5f);
                    pixels[p + 2] = (byte)(pixels[p + 2] + (b - pixels[p + 2]) * a + 0.5f);
                }
            });
        }
    }
}
