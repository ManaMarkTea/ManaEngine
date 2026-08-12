namespace ManaUI
{
    /// <summary>
    /// The visual half of a colour picker: a saturation/value square, a hue strip beneath it, and an
    /// optional alpha strip.
    /// <para>
    /// Drawn as tiles rather than as gradients, because the whole library renders through one solid rounded
    /// box and there is no gradient primitive to reach for. A few hundred small quads inside a popup costs
    /// nothing when the entire frame is a single draw call anyway, and it keeps the picker from dragging a
    /// shader change through both backends.
    /// </para>
    /// <para>
    /// Hue and saturation are kept here rather than derived from the colour on every read. Grey has no hue
    /// and full black no saturation, so a round trip through RGB would silently reset the slider the moment
    /// a drag reached an edge -- and the edge is exactly where people drag to.
    /// </para>
    /// </summary>
    public class ColorSpectrum : Control
    {
        /// <summary>Tiles per axis in the square. Enough that the banding reads as a gradient.</summary>
        private const int SquareSteps = 24;

        /// <summary>Slices along the hue and alpha strips.</summary>
        private const int StripSteps = 48;

        private const float StripHeight = 14.0f;
        private const float StripGap = 6.0f;
        private const float MarkerRadius = 5.0f;

        private float _hue;
        private float _saturation = 1.0f;
        private float _value = 1.0f;
        private float _alpha = 1.0f;

        private Region _dragging = Region.None;

        public ColorSpectrum()
        {
            Focusable = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
        }

        private enum Region
        {
            None,
            Square,
            Hue,
            Alpha
        }

        /// <summary>Fires continuously while a drag moves.</summary>
        public event EventHandler<Vector4>? ColorChanged;

        /// <summary>Fires when a drag ends -- the moment worth committing.</summary>
        public event EventHandler<Vector4>? ColorCommitted;

        /// <summary>Whether the alpha strip is shown at all.</summary>
        public bool ShowAlpha { get; set; } = true;

        /// <summary>Height of the saturation/value square. Width is whatever the parent offers.</summary>
        public float SquareHeight { get; set; } = 120.0f;

        public float Hue
        {
            get => _hue;
            set => SetComponents(value, _saturation, _value, _alpha);
        }

        public float Saturation
        {
            get => _saturation;
            set => SetComponents(_hue, value, _value, _alpha);
        }

        public float Value
        {
            get => _value;
            set => SetComponents(_hue, _saturation, value, _alpha);
        }

        public float Alpha
        {
            get => _alpha;
            set => SetComponents(_hue, _saturation, _value, value);
        }

        /// <summary>
        /// The colour being edited. Reading rebuilds it from the stored hue, saturation and value; writing
        /// decomposes it, but keeps the existing hue when the incoming colour is a grey that has none.
        /// </summary>
        public Vector4 Color
        {
            get => UiColor.FromHsv(_hue, _saturation, _value, _alpha);
            set
            {
                var hsv = UiColor.ToHsv(value);
                SetComponents(hsv.Y <= 0.0f ? _hue : hsv.X, hsv.Y, hsv.Z, value.W);
            }
        }

        private void SetComponents(float hue, float saturation, float value, float alpha)
        {
            hue = ((hue % 360.0f) + 360.0f) % 360.0f;
            saturation = Math.Clamp(saturation, 0.0f, 1.0f);
            value = Math.Clamp(value, 0.0f, 1.0f);
            alpha = Math.Clamp(alpha, 0.0f, 1.0f);

            if (LayoutHelper.AreClose(_hue, hue)
                && LayoutHelper.AreClose(_saturation, saturation)
                && LayoutHelper.AreClose(_value, value)
                && LayoutHelper.AreClose(_alpha, alpha))
            {
                return;
            }

            _hue = hue;
            _saturation = saturation;
            _value = value;
            _alpha = alpha;

            InvalidateVisual();
            ColorChanged?.Invoke(this, Color);
        }

        // ----- geometry -------------------------------------------------------------------------------

        public UiRect SquareRect => new(Bounds.X, Bounds.Y, Bounds.Width, SquareHeight);

        public UiRect HueRect => new(Bounds.X, Bounds.Y + SquareHeight + StripGap, Bounds.Width, StripHeight);

        public UiRect AlphaRect => ShowAlpha
            ? new UiRect(Bounds.X, HueRect.Bottom + StripGap, Bounds.Width, StripHeight)
            : UiRect.Empty;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = float.IsInfinity(availableSize.X) ? 200.0f : availableSize.X;
            var height = SquareHeight + StripGap + StripHeight + (ShowAlpha ? StripGap + StripHeight : 0.0f);
            return new Vector2(width, height);
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            RenderSquare(drawList);
            RenderHueStrip(drawList);

            if (ShowAlpha)
            {
                RenderAlphaStrip(drawList);
            }
        }

        /// <summary>
        /// Saturation left to right, value top to bottom. Tiles overlap by a fraction of a pixel so the
        /// seams between them never show as a grid of hairlines.
        /// </summary>
        private void RenderSquare(ManaUiDrawList drawList)
        {
            var square = SquareRect;
            if (square.Width <= 0.0f || square.Height <= 0.0f)
            {
                return;
            }

            var cellWidth = square.Width / SquareSteps;
            var cellHeight = square.Height / SquareSteps;

            for (var y = 0; y < SquareSteps; y++)
            {
                // Sampled at the cell's centre rather than its corner, so the tiled approximation is
                // centred on the true gradient instead of biased towards one edge.
                var value = 1.0f - (y + 0.5f) / SquareSteps;

                for (var x = 0; x < SquareSteps; x++)
                {
                    var saturation = (x + 0.5f) / SquareSteps;

                    drawList.FillRect(
                        new UiRect(
                            square.X + x * cellWidth,
                            square.Y + y * cellHeight,
                            cellWidth + 0.5f,
                            cellHeight + 0.5f),
                        UiColor.FromHsv(_hue, saturation, value));
                }
            }

            var marker = new Vector2(
                square.X + square.Width * _saturation,
                square.Y + square.Height * (1.0f - _value));

            RenderMarker(drawList, marker);
        }

        private void RenderHueStrip(ManaUiDrawList drawList)
        {
            var strip = HueRect;
            if (strip.Width <= 0.0f)
            {
                return;
            }

            var sliceWidth = strip.Width / StripSteps;
            for (var i = 0; i < StripSteps; i++)
            {
                drawList.FillRect(
                    new UiRect(strip.X + i * sliceWidth, strip.Y, sliceWidth + 0.5f, strip.Height),
                    UiColor.FromHsv((i + 0.5f) / StripSteps * 360.0f, 1.0f, 1.0f));
            }

            RenderMarker(drawList, new Vector2(strip.X + strip.Width * (_hue / 360.0f), strip.Center.Y));
        }

        /// <summary>
        /// Alpha over a light/dark chequer, so a half-transparent colour is distinguishable from a pale
        /// opaque one -- which it is not against a plain background.
        /// </summary>
        private void RenderAlphaStrip(ManaUiDrawList drawList)
        {
            var strip = AlphaRect;
            if (strip.Width <= 0.0f)
            {
                return;
            }

            var opaque = UiColor.FromHsv(_hue, _saturation, _value);
            var sliceWidth = strip.Width / StripSteps;
            var half = strip.Height * 0.5f;

            for (var i = 0; i < StripSteps; i++)
            {
                var alpha = (i + 0.5f) / StripSteps;
                var x = strip.X + i * sliceWidth;

                var top = i % 2 == 0 ? UiColor.White : new Vector4(0.65f, 0.65f, 0.65f, 1.0f);
                var bottom = i % 2 == 0 ? new Vector4(0.65f, 0.65f, 0.65f, 1.0f) : UiColor.White;

                drawList.FillRect(new UiRect(x, strip.Y, sliceWidth + 0.5f, half), top);
                drawList.FillRect(new UiRect(x, strip.Y + half, sliceWidth + 0.5f, half), bottom);

                drawList.FillRect(
                    new UiRect(x, strip.Y, sliceWidth + 0.5f, strip.Height),
                    opaque.WithAlpha(alpha));
            }

            RenderMarker(drawList, new Vector2(strip.X + strip.Width * _alpha, strip.Center.Y));
        }

        /// <summary>
        /// A ring rather than a dot: the marker sits on top of the colour it is selecting, and a filled one
        /// would hide the very thing being aimed at. Two boxes, since there is no stroked-circle primitive.
        /// </summary>
        private void RenderMarker(ManaUiDrawList drawList, Vector2 centre)
        {
            var outer = new UiRect(
                centre.X - MarkerRadius, centre.Y - MarkerRadius, MarkerRadius * 2.0f, MarkerRadius * 2.0f);

            drawList.DrawBox(
                outer,
                UiColor.Transparent,
                UiColor.White,
                2.0f,
                new CornerRadius(MarkerRadius));

            drawList.DrawBox(
                outer.Deflate(new Thickness(2.0f)),
                UiColor.Transparent,
                new Vector4(0.0f, 0.0f, 0.0f, 0.7f),
                1.0f,
                new CornerRadius(MarkerRadius));
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            _dragging = RegionAt(e.Position);
            if (_dragging == Region.None)
            {
                return;
            }

            Focus();
            CapturePointer();
            ApplyPointer(e.Position);
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_dragging == Region.None)
            {
                return;
            }

            ApplyPointer(e.Position);
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_dragging == Region.None)
            {
                return;
            }

            _dragging = Region.None;
            ReleasePointerCapture();
            ColorCommitted?.Invoke(this, Color);
            e.Handled = true;
        }

        private Region RegionAt(Vector2 point)
        {
            if (SquareRect.Contains(point))
            {
                return Region.Square;
            }

            if (HueRect.Contains(point))
            {
                return Region.Hue;
            }

            return ShowAlpha && AlphaRect.Contains(point) ? Region.Alpha : Region.None;
        }

        /// <summary>
        /// Applies a pointer position to whichever region the drag started in -- not whichever it is over
        /// now. Dragging out of the square and across the hue strip must keep editing the square.
        /// </summary>
        private void ApplyPointer(Vector2 position)
        {
            switch (_dragging)
            {
                case Region.Square:
                {
                    var square = SquareRect;
                    if (square.Width <= 0.0f || square.Height <= 0.0f)
                    {
                        return;
                    }

                    SetComponents(
                        _hue,
                        Math.Clamp((position.X - square.X) / square.Width, 0.0f, 1.0f),
                        1.0f - Math.Clamp((position.Y - square.Y) / square.Height, 0.0f, 1.0f),
                        _alpha);
                    break;
                }

                case Region.Hue:
                {
                    var strip = HueRect;
                    if (strip.Width <= 0.0f)
                    {
                        return;
                    }

                    SetComponents(
                        Math.Clamp((position.X - strip.X) / strip.Width, 0.0f, 1.0f) * 360.0f,
                        _saturation,
                        _value,
                        _alpha);
                    break;
                }

                case Region.Alpha:
                {
                    var strip = AlphaRect;
                    if (strip.Width <= 0.0f)
                    {
                        return;
                    }

                    SetComponents(
                        _hue,
                        _saturation,
                        _value,
                        Math.Clamp((position.X - strip.X) / strip.Width, 0.0f, 1.0f));
                    break;
                }
            }
        }

        public override UiCursor GetCursor(Vector2 point)
        {
            return _dragging != Region.None || RegionAt(point) != Region.None
                ? UiCursor.Hand
                : UiCursor.Arrow;
        }
    }
}
