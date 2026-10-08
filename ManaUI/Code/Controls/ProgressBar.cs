namespace ManaUI
{
    /// <summary>What a bar or light means, which picks its colour: the accent, or good, warning, bad.</summary>
    public enum UiTone
    {
        Accent,
        Success,
        Warning,
        Danger
    }

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

        /// <summary>Bar height in logical pixels (its width, when <see cref="Vertical"/>).</summary>
        public float Thickness { get; set; } = 8.0f;

        /// <summary>The fill's colour by meaning: a shield bar is red, a reactor yellow, an energy gauge green.</summary>
        public UiTone Tone { get; set; }

        /// <summary>Fills upward from the bottom instead of rightward, for a gauge. Drawn flat even in a skin.</summary>
        public bool Vertical { get; set; }

        /// <summary>Above 1, drawn as this many separate blocks, filled from the start -- a battery, a health row. Drawn flat.</summary>
        public int Segments { get; set; }

        /// <summary>A vertical bar's height when nothing else constrains it.</summary>
        public float Length { get; set; } = 120.0f;

        private Vector4 ToneColor(UiTheme theme) => Tone switch
        {
            UiTone.Success => theme.Success,
            UiTone.Warning => theme.Warning,
            UiTone.Danger => theme.Danger,
            _ => theme.Accent
        };

        private string FillSlice => Tone switch
        {
            UiTone.Success => "fillSuccess",
            UiTone.Warning => "fillWarning",
            UiTone.Danger => "fillDanger",
            _ => "fill"
        };

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
            if (Vertical)
            {
                var height = float.IsInfinity(availableSize.Y) ? Length : MathF.Min(availableSize.Y, Length);
                return new Vector2(Thickness, height);
            }

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

            if (Vertical || Segments > 1)
            {
                RenderFlat(drawList, theme);
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

            if (skin != null && (skin.TryGet(FillSlice, out var fill) || skin.TryGet("fill", out fill)))
            {
                drawList.DrawNineSlice(filled, fill, Vector4.One, Bounds.Height / fill.Size.Y);
                return;
            }

            var color = ToneColor(theme);
            drawList.FillRect(
                filled,
                theme.GradientTop(color, theme.AccentGradient),
                theme.GradientBottom(color, theme.AccentGradient),
                fillCorners);
        }

        // Gauges and block rows: flat rounded boxes in the tone's colour, whatever the theme's skin.
        private void RenderFlat(ManaUiDrawList drawList, UiTheme theme)
        {
            var color = ToneColor(theme);
            if (Segments > 1)
            {
                const float gap = 3.0f;
                var count = Segments;
                var lit = (int)MathF.Round(Fraction * count);
                var blockWidth = (Bounds.Width - gap * (count - 1)) / count;
                for (var i = 0; i < count; i++)
                {
                    var block = new UiRect(Bounds.X + i * (blockWidth + gap), Bounds.Y, blockWidth, Bounds.Height);
                    drawList.DrawBox(block, i < lit ? color : theme.SurfaceAlt, i < lit ? UiColor.Transparent : theme.Border,
                        i < lit ? 0.0f : 1.0f, new CornerRadius(2.0f));
                }

                return;
            }

            var corners = new CornerRadius(Bounds.Width * 0.5f);
            drawList.DrawBox(Bounds, theme.SurfaceAlt, theme.Border, 1.0f, corners);
            if (Fraction > 0.0f)
            {
                var height = Bounds.Height * Fraction;
                var filled = new UiRect(Bounds.X, Bounds.Bottom - height, Bounds.Width, height);
                drawList.FillRect(filled, theme.GradientTop(color, theme.AccentGradient), theme.GradientBottom(color, theme.AccentGradient),
                    new CornerRadius(MathF.Min(corners.TopLeft, height * 0.5f)));
            }
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
