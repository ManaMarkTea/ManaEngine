namespace ManaPixelFx.Core
{
    /// <summary>A box of pixels on an effect's canvas, inclusive; <see cref="IsEmpty"/> when nothing was drawn.</summary>
    public readonly record struct PixelBounds(int X0, int Y0, int X1, int Y1)
    {
        public static readonly PixelBounds Empty = new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

        public bool IsEmpty => X1 < X0 || Y1 < Y0;
        public int Width => X1 - X0 + 1;
        public int Height => Y1 - Y0 + 1;

        public PixelBounds Include(int x, int y) => new(Math.Min(X0, x), Math.Min(Y0, y), Math.Max(X1, x), Math.Max(Y1, y));
    }

    /// <summary>
    /// One run of an effect, frame by frame, as pixelfx's sheet renderer plays it: stepped at its fps from its
    /// origin, light eased off the canvas edges. What a bake, a parity check and a measurement all start from.
    /// </summary>
    public static class PixelFxRun
    {
        /// <summary>How many frames one run has at <paramref name="fps"/>: a loop's worth, or a one-shot to its end.</summary>
        public static int FrameCount(PixelEffectMeta meta, int fps)
        {
            var extra = 1;
            if (meta.Loop)
            {
                extra = 0;
            }
            return Math.Max(1, (int)Math.Round(meta.Duration * fps) + extra);
        }

        /// <summary>A screen-sized effect (a transition) keeps its light to the edges.</summary>
        public static bool IsScreen(PixelEffectMeta meta) =>
            meta.Size.W >= 320 && Math.Abs(meta.Size.W / (double)meta.Size.H - 16.0 / 9.0) < 0.05;

        /// <summary>
        /// Plays one run at <paramref name="fps"/> (the effect's own when 0), handing each drawn frame to
        /// <paramref name="each"/>; with <paramref name="drawn"/>, only the frames it picks are drawn (the rest are stepped).
        /// </summary>
        public static void Play(PixelEffect effect, IReadOnlyDictionary<string, object>? options, int fps, Action<int, PixelFrame> each, Func<int, bool>? drawn = null)
        {
            var meta = effect.Meta;
            if (fps <= 0)
            {
                fps = meta.Fps;
            }
            var (w, h) = meta.Size;
            var (ox, oy) = meta.Origin;
            var placed = new Dictionary<string, object>();
            if (options != null)
            {
                foreach (var (key, value) in options)
                {
                    placed[key] = value;
                }
            }
            placed["x"] = (double)ox;
            placed["y"] = (double)oy;
            var frame = new PixelFrame(w, h);
            var fx = new PixelFxInstance(effect, placed);
            var screen = IsScreen(meta);
            var n = FrameCount(meta, fps);
            for (var i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    fx.Update(1.0 / fps);
                }
                if (drawn != null && !drawn(i))
                {
                    continue;
                }
                frame.Clear();
                fx.Draw(frame);
                if (!screen)
                {
                    frame.EdgeFade();
                }
                each(i, frame);
            }
        }

        /// <summary>
        /// What one run draws: <c>Color</c> the pixels of its picture (the thing itself -- where it stands), <c>All</c>
        /// those and its light (what a sheet must hold).
        /// </summary>
        public static (PixelBounds Color, PixelBounds All) Measure(PixelEffect effect, IReadOnlyDictionary<string, object>? options)
        {
            var color = PixelBounds.Empty;
            var all = PixelBounds.Empty;
            Play(effect, options, 0, (_, frame) =>
            {
                for (var y = 0; y < frame.H; y++)
                {
                    for (var x = 0; x < frame.W; x++)
                    {
                        var i = y * frame.W + x;
                        if (frame.Color[i] >> 24 != 0)
                        {
                            color = color.Include(x, y);
                            all = all.Include(x, y);
                        }
                        else if (Visible(frame.Light[i]))
                        {
                            all = all.Include(x, y);
                        }
                    }
                }
            });
            return (color, all);
        }

        // Light of 1/255 added over anything is invisible; leaving it out keeps the measured box to what shows.
        private static bool Visible(uint light) => (light & 0xff) > 1 || ((light >> 8) & 0xff) > 1 || ((light >> 16) & 0xff) > 1;
    }
}
