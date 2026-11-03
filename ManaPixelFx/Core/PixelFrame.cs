namespace ManaPixelFx.Core
{
    /// <summary>
    /// A frame: w x h pixels with its top left at (ox, oy). <see cref="Color"/> holds the pixels, <see cref="Light"/>
    /// the additive glow; <see cref="Composite"/> puts them together. Same shape as pixelfx's and Sprite Fusion's.
    /// </summary>
    public sealed class PixelFrame
    {
        public PixelFrame(int w, int h, int ox = 0, int oy = 0)
        {
            W = w;
            H = h;
            Ox = ox;
            Oy = oy;
            Color = new uint[w * h];
            Light = new uint[w * h];
            Painter = new PixelPainter(this);
        }

        public int W { get; }
        public int H { get; }
        public int Ox { get; set; }
        public int Oy { get; set; }
        public uint[] Color { get; }
        public uint[] Light { get; }

        /// <summary>The frame's painter, one per frame and reused, as painterFor does.</summary>
        public PixelPainter Painter { get; }

        public void Clear()
        {
            Array.Clear(Color);
            Array.Clear(Light);
        }

        /// <summary>
        /// Colour, plus light, into RGBA bytes (w * h * 4) over transparency: light becomes colour with alpha (its
        /// brightest channel), which reads as additive over dark backgrounds. Bytes are stored as a
        /// Uint8ClampedArray stores them, rounded half to even, so output matches pixelfx's byte for byte.
        /// </summary>
        public byte[] Composite(byte[]? output = null, double lightK = 1)
        {
            var n = W * H;
            var outBytes = output ?? new byte[n * 4];
            for (var i = 0; i < n; i++)
            {
                var c = Color[i];
                var l = Light[i] & 0xffffffu;
                double r = c & 255;
                double g = (c >> 8) & 255;
                double b = (c >> 16) & 255;
                double a = c >> 24;
                if (l != 0)
                {
                    var lr = (l & 255) * lightK;
                    var lg = ((l >> 8) & 255) * lightK;
                    var lb = ((l >> 16) & 255) * lightK;
                    if (a < 255)
                    {
                        // over transparency: premultiply what is there, add the light, divide by the new alpha
                        var pa = a / 255;
                        var pr = r * pa + lr;
                        var pg = g * pa + lg;
                        var pb = b * pa + lb;
                        var na = Math.Min(1, Math.Max(pa, Math.Max(lr, Math.Max(lg, lb)) / 255));
                        if (na > 0)
                        {
                            r = Math.Min(255, pr / na);
                            g = Math.Min(255, pg / na);
                            b = Math.Min(255, pb / na);
                            a = na * 255;
                        }
                    }
                    else
                    {
                        r = Math.Min(255, r + lr);
                        g = Math.Min(255, g + lg);
                        b = Math.Min(255, b + lb);
                    }
                }
                var j = i * 4;
                outBytes[j] = ClampByte(r);
                outBytes[j + 1] = ClampByte(g);
                outBytes[j + 2] = ClampByte(b);
                outBytes[j + 3] = ClampByte(a);
            }
            return outBytes;
        }

        /// <summary>
        /// A glow cut off by the canvas edge shows as a box once a frame sits in a game: eases the light out over the
        /// last <paramref name="e"/> pixels, as pixelfx's renderer does before writing a sheet. Colour is left alone.
        /// </summary>
        public void EdgeFade(int e = 8)
        {
            for (var y = 0; y < H; y++)
            {
                var ey = Math.Min(y, H - 1 - y);
                for (var x = 0; x < W; x++)
                {
                    var edge = Math.Min(ey, Math.Min(x, W - 1 - x));
                    if (edge >= e)
                    {
                        x = W - 1 - e;
                        continue;
                    }
                    var i = y * W + x;
                    var l = Light[i];
                    if ((l & 0xffffffu) == 0)
                    {
                        continue;
                    }
                    var u = (edge + 0.5) / e;
                    var k = u * u * (3 - 2 * u);
                    Light[i] = (l & 0xff000000u)
                        | (uint)PixelMath.Round((l & 255) * k)
                        | ((uint)PixelMath.Round(((l >> 8) & 255) * k) << 8)
                        | ((uint)PixelMath.Round(((l >> 16) & 255) * k) << 16);
                }
            }
        }

        /// <summary>A Uint8ClampedArray store: NaN to 0, clamped, halves to even.</summary>
        public static byte ClampByte(double x)
        {
            if (double.IsNaN(x) || x <= 0)
            {
                return 0;
            }
            if (x >= 255)
            {
                return 255;
            }
            return (byte)Math.Round(x, MidpointRounding.ToEven);
        }
    }
}
