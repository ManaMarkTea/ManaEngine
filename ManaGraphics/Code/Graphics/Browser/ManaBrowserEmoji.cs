#if MANA_BROWSER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManaUI;

namespace ManaGraphics.Code.Graphics.Browser
{
    /// <summary>
    /// Colour emoji drawn by the browser's own emoji font onto a canvas, so the web build need not download
    /// Noto (10 MB, a fifth of the startup bundle). Each glyph is drawn once at the size asked for and cropped.
    /// </summary>
    [SupportedOSPlatform("browser")]
    public sealed partial class ManaBrowserEmoji : IManaColorGlyphSource
    {
        private const string ModuleName = "mana_interop";

        // Drawn once per size: the warm-up below draws during the loading screen what the UI asks for after it.
        private readonly Dictionary<(int Codepoint, int Size), ManaColorGlyphImage?> _drawn = new();
        private static ManaBrowserEmoji? _installed;
        private static bool _reportedFailure;

        /// <summary>Hands ManaUI the browser's emoji. Call after <see cref="ManaBrowserInterop.InitializeAsync"/>.</summary>
        public static void Install()
        {
            _installed = new ManaBrowserEmoji();
            ManaFontManager.PlatformColorEmoji = _installed;
        }

        /// <summary>
        /// Draws these emoji ahead of the UI, at its default text size, while the loading screen is up -- which
        /// shows each one as it is made -- yielding between them so the page can paint.
        /// </summary>
        public static async System.Threading.Tasks.Task WarmAsync(IEnumerable<int> codepoints, float pixelSize)
        {
            if (_installed == null)
            {
                return;
            }

            // The UI rasterises at its physical size, quantised to quarter scales as ManaFontManager does.
            var scale = MathF.Max(1.0f, MathF.Round((float)ManaBrowserInterop.GetDevicePixelRatio() * 4.0f) / 4.0f);
            foreach (var codepoint in codepoints)
            {
                _installed.Draw(codepoint, MathF.Round(pixelSize) * scale);
                await System.Threading.Tasks.Task.Delay(40);
            }
        }

        [JSImport("drawEmoji", ModuleName)]
        private static partial double DrawEmoji(int codepoint, int size, int width, int height, int baseline,
            [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);

        public ManaColorGlyphImage? Draw(int codepoint, float pixelsPerEm)
        {
            var size = Math.Max(4, (int)MathF.Round(pixelsPerEm));
            if (_drawn.TryGetValue((codepoint, size), out var cached))
            {
                return cached;
            }

            var image = DrawUncached(codepoint, size);
            _drawn[(codepoint, size)] = image;
            return image;
        }

        private static ManaColorGlyphImage? DrawUncached(int codepoint, int size)
        {
            // Room for an emoji that overhangs its em either side; the crop below takes the margin back off.
            var pad = (size + 3) / 4;
            var width = size * 2 + pad * 2;
            var height = size + size / 2 + pad * 2;
            var baseline = pad + size;
            var pixels = new byte[width * height * 4];

            double advance;
            try
            {
                advance = DrawEmoji(codepoint, size, width, height, baseline, pixels);
            }
            catch (Exception ex)
            {
                // Said once: every emoji after would fail the same way.
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    Console.WriteLine($"[ManaBrowserEmoji] drawing failed: {ex.Message}");
                }

                return null;
            }

            if (advance <= 0 || !TryBounds(pixels, width, height, out var left, out var top, out var right, out var bottom))
            {
                return null;
            }

            var croppedWidth = right - left + 1;
            var croppedHeight = bottom - top + 1;
            var cropped = new byte[croppedWidth * croppedHeight * 4];
            for (var y = 0; y < croppedHeight; y++)
            {
                Array.Copy(pixels, ((top + y) * width + left) * 4, cropped, y * croppedWidth * 4, croppedWidth * 4);
            }

            // The canvas drew with its pen at x = pad.
            return new ManaColorGlyphImage(cropped, croppedWidth, croppedHeight, left - pad, baseline - top, (float)advance);
        }

        private static bool TryBounds(byte[] pixels, int width, int height, out int left, out int top, out int right, out int bottom)
        {
            left = width;
            top = height;
            right = -1;
            bottom = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(y * width + x) * 4 + 3] == 0)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right >= 0;
        }
    }
}
#endif
