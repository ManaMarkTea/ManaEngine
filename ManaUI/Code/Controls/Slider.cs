namespace ManaUI
{
    /// <summary>
    /// A draggable numeric range control. The prime consumer of pointer capture: once the thumb is grabbed
    /// it keeps tracking even when the pointer leaves the track entirely.
    /// </summary>
    public class Slider : Control
    {
        private const float TrackThickness = 4.0f;
        private const float ThumbDiameter = 14.0f;

        private float _value;
        private float _minimum;
        private float _maximum = 1.0f;

        public Slider()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public event EventHandler<float>? ValueChanged;

        public float Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                Value = _value;
                InvalidateVisual();
            }
        }

        public float Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;
                Value = _value;
                InvalidateVisual();
            }
        }

        /// <summary>Snap increment; 0 is continuous.</summary>
        public float Step { get; set; }

        /// <summary>Keyboard arrow increment. Falls back to Step, then to 1% of the range.</summary>
        public float KeyboardStep { get; set; }

        public float Value
        {
            get => _value;
            set
            {
                var clamped = Coerce(value);
                if (LayoutHelper.AreClose(_value, clamped))
                {
                    return;
                }

                _value = clamped;
                InvalidateVisual();
                ValueChanged?.Invoke(this, clamped);
            }
        }

        /// <summary>Value as 0..1 across the range; 0 when the range is degenerate.</summary>
        public float NormalizedValue
        {
            get
            {
                var range = _maximum - _minimum;
                return range <= 0.0f ? 0.0f : (_value - _minimum) / range;
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // Width is whatever the parent offers; an unconstrained slider still needs a sensible default
            // rather than collapsing to the thumb.
            var width = float.IsInfinity(availableSize.X) ? 160.0f : availableSize.X;
            return new Vector2(width, MathF.Max(ThumbDiameter, Theme.ControlHeight));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var enabled = IsEffectivelyEnabled;

            var track = TrackRect();
            var corners = new CornerRadius(TrackThickness * 0.5f);

            drawList.FillRect(track, theme.SurfaceAlt, corners);

            var fillWidth = track.Width * NormalizedValue;
            if (fillWidth > 0.0f)
            {
                drawList.FillRect(
                    new UiRect(track.X, track.Y, fillWidth, track.Height),
                    enabled ? theme.Accent : theme.Border,
                    corners);
            }

            var thumbCentre = track.X + fillWidth;
            var thumb = new UiRect(
                thumbCentre - ThumbDiameter * 0.5f,
                Bounds.Center.Y - ThumbDiameter * 0.5f,
                ThumbDiameter,
                ThumbDiameter);

            var thumbCorners = new CornerRadius(ThumbDiameter * 0.5f);
            drawList.DrawBox(
                thumb,
                ApplyStateOverlay(enabled ? theme.SurfaceRaised : theme.SurfaceAlt),
                enabled ? theme.Accent : theme.Border,
                2.0f,
                thumbCorners);

            RenderFocusRing(drawList, thumb, thumbCorners);
        }

        /// <summary>The track, inset by half a thumb so the thumb never overhangs the control's bounds.</summary>
        private UiRect TrackRect()
        {
            var inset = ThumbDiameter * 0.5f;
            return new UiRect(
                Bounds.X + inset,
                Bounds.Center.Y - TrackThickness * 0.5f,
                MathF.Max(0.0f, Bounds.Width - ThumbDiameter),
                TrackThickness);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            IsPressed = true;
            CapturePointer();

            // Clicking anywhere on the track jumps the thumb there, then continues as a drag.
            SetValueFromPointer(e.Position);
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (IsPressed)
            {
                SetValueFromPointer(e.Position);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            IsPressed = false;
            ReleasePointerCapture();
            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (!IsEffectivelyEnabled)
            {
                return;
            }

            var step = KeyboardStep > 0.0f
                ? KeyboardStep
                : Step > 0.0f ? Step : (_maximum - _minimum) * 0.01f;

            switch (e.Key)
            {
                case UiKey.Left or UiKey.Down:
                    Value -= step;
                    e.Handled = true;
                    break;
                case UiKey.Right or UiKey.Up:
                    Value += step;
                    e.Handled = true;
                    break;
                case UiKey.Home:
                    Value = _minimum;
                    e.Handled = true;
                    break;
                case UiKey.End:
                    Value = _maximum;
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnPointerWheel(UiPointerWheelEventArgs e)
        {
            // Focus is required deliberately. A slider that reacts to any wheel event under the pointer
            // hijacks scrolling of the panel it sits in, and silently edits a value the user was only
            // scrolling past -- so the wheel adjusts it only once it has been deliberately focused.
            if (!IsEffectivelyEnabled || !IsFocused)
            {
                return;
            }

            var step = Step > 0.0f ? Step : (_maximum - _minimum) * 0.02f;
            Value += e.Delta.Y * step;
            e.Handled = true;
        }

        private void SetValueFromPointer(Vector2 position)
        {
            var track = TrackRect();
            if (track.Width <= 0.0f)
            {
                return;
            }

            var normalized = Math.Clamp((position.X - track.X) / track.Width, 0.0f, 1.0f);
            Value = _minimum + normalized * (_maximum - _minimum);
        }

        private float Coerce(float value)
        {
            var low = MathF.Min(_minimum, _maximum);
            var high = MathF.Max(_minimum, _maximum);

            if (Step > 0.0f)
            {
                value = low + MathF.Round((value - low) / Step) * Step;
            }

            return Math.Clamp(value, low, high);
        }
    }
}
