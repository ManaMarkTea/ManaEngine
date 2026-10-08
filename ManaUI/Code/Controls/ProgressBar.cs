namespace ManaUI
{
    /// <summary>
    /// A horizontal fill showing how much of something is done.
    /// <para>
    /// Read-only by design -- nothing here accepts pointer input, because nothing a progress bar shows is
    /// ever something the user set. That is also why it is a <see cref="UiElement"/> rather than a
    /// <see cref="Control"/>: there is no interaction to inherit.
    /// </para>
    /// </summary>
    public class ProgressBar : UiElement
    {
        private float _fraction;

        public ProgressBar()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        /// <summary>0..1. Clamped on the way in, so a caller passing a raw ratio cannot overshoot the bar.</summary>
        public float Fraction
        {
            get => _fraction;
            set => SetVisualProperty(ref _fraction, Math.Clamp(value, 0.0f, 1.0f));
        }

        /// <summary>Bar height in logical pixels.</summary>
        public float Thickness { get; set; } = 8.0f;

        /// <summary>
        /// Indeterminate: a band sweeping the track rather than a fraction filled, for work with no
        /// meaningful percentage -- waiting on a network reply, or a step nobody has instrumented yet.
        /// </summary>
        public bool IsIndeterminate { get; set; }

        /// <summary>Seconds for one sweep of the indeterminate band. Ignored otherwise.</summary>
        public float SweepSeconds { get; set; } = 1.4f;

        /// <summary>Elapsed time, for the indeterminate sweep. The caller's clock, not this control's own.</summary>
        public float ElapsedSeconds { get; set; }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = float.IsInfinity(availableSize.X) ? 160.0f : availableSize.X;
            return new Vector2(width, Thickness);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var corners = new CornerRadius(Bounds.Height * 0.5f);

            var skin = theme.Skin;
            if (skin != null && skin.TryGet("track", out var track))
            {
                drawList.DrawNineSlice(Bounds, track, Vector4.One, Bounds.Height / track.Size.Y);
            }
            else
            {
                drawList.FillRect(Bounds, theme.SurfaceAlt, corners);
            }

            if (IsIndeterminate)
            {
                RenderIndeterminate(drawList, theme, corners);
                return;
            }

            if (Fraction <= 0.0f)
            {
                return;
            }

            var filled = new UiRect(Bounds.X, Bounds.Y, Bounds.Width * Fraction, Bounds.Height);

            // A small bar rounded at the full radius looks like a rectangle with a chip missing from
            // its far end, once the fill is narrower than the radius on both sides. Deflating the corner
            // radius by how little is left keeps a thin sliver reading as a rounded bar rather than a
            // clipped one.
            var fillCorners = new CornerRadius(MathF.Min(corners.TopLeft, filled.Width * 0.5f));

            if (skin != null && skin.TryGet("fill", out var fill))
            {
                drawList.DrawNineSlice(filled, fill, Vector4.One, Bounds.Height / fill.Size.Y);
                return;
            }

            drawList.FillRect(
                filled,
                theme.GradientTop(theme.Accent, theme.AccentGradient),
                theme.GradientBottom(theme.Accent, theme.AccentGradient),
                fillCorners);
        }

        private void RenderIndeterminate(ManaUiDrawList drawList, UiTheme theme, CornerRadius corners)
        {
            // A band a third of the track wide, sweeping past both ends before it loops -- so it visibly
            // leaves the bar rather than teleporting back the moment it reaches the edge.
            var bandWidth = Bounds.Width * 0.33f;
            var travel = Bounds.Width + bandWidth;
            var t = SweepSeconds > 0.0f ? ElapsedSeconds / SweepSeconds % 1.0f : 0.0f;
            var x = Bounds.X - bandWidth + t * travel;

            var band = new UiRect(x, Bounds.Y, bandWidth, Bounds.Height);
            drawList.PushClip(Bounds);
            drawList.FillRect(
                band,
                theme.GradientTop(theme.Accent, theme.AccentGradient),
                theme.GradientBottom(theme.Accent, theme.AccentGradient),
                corners);
            drawList.PopClip();
        }
    }
}
