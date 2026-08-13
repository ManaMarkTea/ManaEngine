namespace ManaUI
{
    /// <summary>
    /// A small graph of a recent series -- frame times, memory, anything sampled over and over.
    /// <para>
    /// Drawn as one filled column per sample rather than as a line. The UI has exactly one primitive, a
    /// rounded box, so a line would have to be built from rotated segments; columns cost the same
    /// instances, read better at this size, and keep the whole thing inside the single draw call.
    /// </para>
    /// <para>
    /// The series is not copied. A caller graphing a ring buffer it updates every frame should not be
    /// paying for an allocation per frame to do it.
    /// </para>
    /// </summary>
    public sealed class Sparkline : Control
    {
        private IReadOnlyList<float>? _samples;

        /// <summary>The series, newest last. Read at render time, never copied.</summary>
        public IReadOnlyList<float>? Samples
        {
            get => _samples;
            set => SetVisualProperty(ref _samples, value);
        }

        /// <summary>Bottom of the graph. Fixed rather than the series minimum, so it does not breathe.</summary>
        public float Minimum { get; set; }

        /// <summary>
        /// Top of the graph, or null to follow the tallest sample. Fixed is usually what you want -- an
        /// auto-scaled frame-time graph looks identical whether the game runs at 30fps or 300.
        /// </summary>
        public float? Maximum { get; set; }

        /// <summary>Drawn across the graph, for a target: sixty frames a second, a memory budget.</summary>
        public float? Threshold { get; set; }

        public Vector4? Fill { get; set; }

        public Vector4? ThresholdColor { get; set; }

        /// <summary>Widest a column may be, so a short series does not become a row of slabs.</summary>
        public float MaximumColumnWidth { get; set; } = 6.0f;

        public Sparkline()
        {
            Height = 32.0f;
            Focusable = false;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return new Vector2(LayoutHelper.IsAuto(Width) ? 120.0f : Width, Height);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;

            drawList.FillRect(Bounds, theme.SurfaceAlt, theme.CornerRadius);

            var samples = _samples;
            if (samples == null || samples.Count == 0 || Bounds.Width <= 2.0f || Bounds.Height <= 2.0f)
            {
                return;
            }

            var inner = new UiRect(Bounds.X + 1.0f, Bounds.Y + 1.0f, Bounds.Width - 2.0f, Bounds.Height - 2.0f);

            var top = Maximum ?? Highest(samples);
            var range = top - Minimum;
            if (range <= 0.0001f)
            {
                return;
            }

            var columnWidth = MathF.Min(MaximumColumnWidth, inner.Width / samples.Count);
            var visible = Math.Min(samples.Count, Math.Max(1, (int)(inner.Width / MathF.Max(columnWidth, 0.5f))));

            // The newest samples, right-aligned: a graph that scrolled from the left would put the moment
            // you care about at whichever end happened to have room.
            var first = samples.Count - visible;
            var fill = Fill ?? theme.Accent;

            for (var i = 0; i < visible; i++)
            {
                var value = Math.Clamp(samples[first + i], Minimum, top);
                var height = (value - Minimum) / range * inner.Height;

                if (height <= 0.5f)
                {
                    continue;
                }

                var x = inner.Right - (visible - i) * columnWidth;

                drawList.FillRect(
                    new UiRect(x, inner.Bottom - height, MathF.Max(1.0f, columnWidth - 1.0f), height),
                    fill);
            }

            if (Threshold is { } threshold && threshold > Minimum && threshold < top)
            {
                var y = inner.Bottom - (threshold - Minimum) / range * inner.Height;
                drawList.FillRect(
                    new UiRect(inner.X, y, inner.Width, 1.0f),
                    ThresholdColor ?? theme.Warning);
            }
        }

        private static float Highest(IReadOnlyList<float> samples)
        {
            var highest = float.NegativeInfinity;
            foreach (var sample in samples)
            {
                highest = MathF.Max(highest, sample);
            }

            return highest;
        }
    }
}
