using System.Globalization;

namespace ManaUI
{
    /// <summary>Where a <see cref="NumericBox"/> puts the part of itself that scrubs.</summary>
    public enum NumericDragHandle
    {
        /// <summary>A strip along the bottom edge. Full width, at the cost of a few pixels of height.</summary>
        Strip,

        /// <summary>
        /// A coloured chip on the leading edge, carrying <see cref="NumericBox.ChipText"/>. Costs width
        /// rather than height, which is what a dense inspector has to spare -- and the chip doubles as the
        /// field's label, so the row needs no separate one.
        /// </summary>
        Leading,

        /// <summary>No drag at all; the field is typed into only.</summary>
        None
    }

    /// <summary>
    /// A numeric field that is both a drag and a text box, which is the ergonomic the editor's ImGui
    /// inspector relies on everywhere: sweep to scrub the value, type an exact one.
    /// <para>
    /// The two gestures live in separate places rather than sharing the whole control. The text occupies
    /// the field proper and behaves as any other text box does -- caret, selection, drag-select -- while a
    /// strip along the bottom edge is the only part that scrubs. Overlaying both on one surface meant every
    /// attempt to select part of a number was also a drag, and the disambiguation could only ever be a
    /// guess about intent.
    /// </para>
    /// <para>
    /// It derives from <see cref="TextBox"/> rather than hosting one, so caret movement, selection,
    /// clipboard and the "value is settled" commit semantics come for free and behave identically to every
    /// other field. What it adds is the strip, and parsing on the way back out.
    /// </para>
    /// </summary>
    public class NumericBox : TextBox
    {
        /// <summary>Pointer travel, in logical pixels, before a press counts as a drag rather than a click.</summary>
        private const float DragThreshold = 3.0f;

        private float _value;
        private float _minimum = float.NegativeInfinity;
        private float _maximum = float.PositiveInfinity;
        private float _step = 0.01f;
        private int _decimalPlaces = 3;
        private bool _isInteger;

        private bool _dragging;
        private bool _passedThreshold;
        private Vector2 _dragOrigin;
        private float _dragOriginValue;

        private bool _sweepFine;
        private float _sweepAnchorX;
        private float _sweepAnchorValue;

        // Guards the two directions of the Value <-> Text mirror against re-entering each other.
        private bool _syncing;

        public NumericBox()
        {
            CharacterFilter = IsNumericCharacter;
            TextChanged += (_, text) => OnTextChangedCore(text);
            SyncTextFromValue();
        }

        public event EventHandler<float>? ValueChanged;

        /// <summary>Raised when an edit settles -- drag release, Enter, or focus loss -- not per drag pixel.</summary>
        public event EventHandler<float>? ValueCommitted;

        public float Value
        {
            get => _value;
            set
            {
                var coerced = Coerce(value);
                if (LayoutHelper.AreClose(_value, coerced))
                {
                    // The text can still be stale even when the number is not: typing "5abc" into a field
                    // holding 5 parses back to the same value, and the field would otherwise keep showing
                    // the rejected text.
                    SyncTextFromValue();
                    return;
                }

                _value = coerced;
                SyncTextFromValue();
                ValueChanged?.Invoke(this, coerced);
            }
        }

        public float Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                Value = _value;
            }
        }

        public float Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;
                Value = _value;
            }
        }

        /// <summary>
        /// The increment a drag moves in, and the arrow-key increment. This is the resolution of the value,
        /// not the speed of the drag -- see <see cref="PixelsPerStep"/> for that.
        /// </summary>
        public float Step
        {
            get => _step;

            // Re-formats, because the step decides how many decimals are shown. Without this, the order of
            // an object initialiser would matter: `{ Value = 2.5f, Step = 0.1f }` would keep the text the
            // default step had produced.
            set => SetFormatProperty(ref _step, value);
        }

        /// <summary>
        /// Pointer travel needed to advance one <see cref="Step"/>, for fields with no range to sweep.
        /// One step per pixel is far too fast to aim -- a two-pixel twitch would move a 0.25-step field by
        /// half a unit -- so the drag is quantised: the value only moves once the pointer has covered this
        /// much ground. Ignored when <see cref="SweepsRange"/> is true.
        /// </summary>
        public float PixelsPerStep { get; set; } = 2.0f;

        /// <summary>
        /// True when the field is bounded on both sides, in which case a drag behaves like a slider: one
        /// field width of travel sweeps the whole range, then the result snaps to the step.
        /// <para>
        /// The two are genuinely different gestures. Sweeping a bounded field is aiming at a position --
        /// the range track under the text is the thumb -- and pacing it by the step instead would take 400
        /// pixels to cross a 0..1 range in hundredths. An unbounded field has no position to aim at, so
        /// there is nothing to map travel onto and it counts steps instead.
        /// </para>
        /// </summary>
        public bool SweepsRange => !float.IsInfinity(_minimum)
            && !float.IsInfinity(_maximum)
            && _maximum > _minimum;

        /// <summary>
        /// Travel that sweeps the full range, for a bounded field. Its own width, so the gesture matches
        /// the strip it is dragging; the floor keeps a field in a narrow column usable.
        /// </summary>
        private float SweepDistance => MathF.Max(60.0f, Bounds.Width);

        /// <summary>
        /// Height of the drag strip along the bottom edge, when <see cref="DragHandle"/> is
        /// <see cref="NumericDragHandle.Strip"/>. Tall enough to aim at without the field turning into a
        /// slider with a caption.
        /// </summary>
        public float DragStripHeight { get; set; } = 7.0f;

        /// <summary>
        /// Width of the leading chip, when <see cref="DragHandle"/> is
        /// <see cref="NumericDragHandle.Leading"/>. Sized for one or two characters.
        /// </summary>
        public float DragChipWidth { get; set; } = 20.0f;

        /// <summary>Where the drag gesture lives. See <see cref="NumericDragHandle"/>.</summary>
        public NumericDragHandle DragHandle { get; set; } = NumericDragHandle.Strip;

        /// <summary>Text on the leading chip -- an axis letter, or a short property name.</summary>
        public string ChipText { get; set; } = string.Empty;

        /// <summary>
        /// Chip background. Transparent falls back to the theme's raised surface, so a chip is visible
        /// without every caller having to pick a colour.
        /// </summary>
        public Vector4 ChipColor { get; set; }

        /// <summary>
        /// The part of the control a drag starts from -- the bottom strip or the leading chip. Empty when
        /// the field is typed into only.
        /// </summary>
        public UiRect DragStripRect
        {
            get
            {
                return DragHandle switch
                {
                    NumericDragHandle.Strip when DragStripHeight > 0.0f => new UiRect(
                        Bounds.X, Bounds.Bottom - DragStripHeight, Bounds.Width, DragStripHeight),

                    NumericDragHandle.Leading when DragChipWidth > 0.0f => new UiRect(
                        Bounds.X, Bounds.Y, DragChipWidth, Bounds.Height),

                    _ => UiRect.Empty
                };
            }
        }

        private bool IsScrubbable => !DragStripRect.IsEmpty && IsEffectivelyEnabled && !IsReadOnly;

        /// <summary>
        /// Reserves the handle, so the text lays out beside or above it rather than through it. Caret hit
        /// testing reads the same inset, which is what keeps clicking a digit landing on that digit.
        /// </summary>
        protected override Thickness ContentInset
        {
            get
            {
                var inset = base.ContentInset;

                return DragHandle switch
                {
                    NumericDragHandle.Strip when DragStripHeight > 0.0f => new Thickness(
                        inset.Left, inset.Top, inset.Right, inset.Bottom + DragStripHeight),

                    NumericDragHandle.Leading when DragChipWidth > 0.0f => new Thickness(
                        inset.Left + DragChipWidth, inset.Top, inset.Right, inset.Bottom),

                    _ => inset
                };
            }
        }

        /// <summary>Maximum digits shown. Trailing zeros are trimmed, but never below the step's own
        /// precision -- see <see cref="StepDecimalPlaces"/>.</summary>
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set => SetFormatProperty(ref _decimalPlaces, value);
        }

        /// <summary>Rounds to whole numbers and formats without a decimal point.</summary>
        public bool IsInteger
        {
            get => _isInteger;
            set
            {
                if (_isInteger == value)
                {
                    return;
                }

                _isInteger = value;

                // Not just a re-format: turning this on has to round the value it is already holding.
                Value = _value;
                SyncTextFromValue();
            }
        }

        /// <summary>Assigns a property that only affects how the value is written, then rewrites it.</summary>
        private void SetFormatProperty<T>(ref T storage, T value)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return;
            }

            storage = value;
            SyncTextFromValue();
        }

        /// <summary>Multiplies the drag step while Shift is held.</summary>
        public float FastMultiplier { get; set; } = 10.0f;

        /// <summary>Multiplies the drag step while Ctrl (or Cmd) is held, for fine adjustment.</summary>
        public float SlowMultiplier { get; set; } = 0.1f;

        /// <summary>True while a scrub is in progress, so callers can defer expensive work until it ends.</summary>
        public bool IsDragging => _dragging && _passedThreshold;

        // ----- value <-> text -------------------------------------------------------------------------

        /// <summary>
        /// The number as the field displays it. Round-trips through <see cref="float"/>, so a value the user
        /// typed reappears the way they typed it only up to <see cref="DecimalPlaces"/>.
        /// </summary>
        public string FormatValue(float value)
        {
            if (IsInteger)
            {
                return ((long)MathF.Round(value, MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture);
            }

            var places = Math.Clamp(DecimalPlaces, 0, 15);
            var text = MathF.Round(value, places, MidpointRounding.AwayFromZero)
                .ToString("F" + places, CultureInfo.InvariantCulture);

            // "1.500000" reads as noise in a dense inspector, but trimming all the way to "1.5" makes the
            // text change width mid-drag, and the number visibly jitters as it steps 0.25, 0.5, 0.75, 1.
            // Trimming stops at the step's own precision, so a 0.25 step reads 0.25, 0.50, 0.75, 1.00 --
            // same character count throughout, and Roboto's digits are all the same width.
            var point = text.IndexOf('.');
            if (point >= 0)
            {
                var shortest = point + 1 + Math.Min(places, StepDecimalPlaces);

                while (text.Length > shortest && text.EndsWith('0'))
                {
                    text = text[..^1];
                }

                if (text.EndsWith('.'))
                {
                    text = text[..^1];
                }
            }

            return text.Length == 0 || text == "-" ? "0" : text;
        }

        /// <summary>
        /// Decimals needed to write <see cref="Step"/> exactly -- 2 for 0.25, 1 for 0.5, 0 for 1. This is
        /// the precision a drag moves in, so it is also the precision the field should never trim below.
        /// </summary>
        public int StepDecimalPlaces
        {
            get
            {
                if (IsInteger || Step <= 0.0f)
                {
                    return 0;
                }

                var limit = Math.Clamp(DecimalPlaces, 0, 15);
                for (var places = 0; places < limit; places++)
                {
                    if (LayoutHelper.AreClose(MathF.Round(Step, places), Step))
                    {
                        return places;
                    }
                }

                return limit;
            }
        }

        /// <summary>Parses the field's text. Returns false for anything that is not a number.</summary>
        public bool TryParseText(out float parsed)
        {
            return float.TryParse(
                Text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        private void SyncTextFromValue()
        {
            _syncing = true;
            Text = FormatValue(_value);
            _syncing = false;
        }

        /// <summary>
        /// Pulls the typed text back into the value. An unparseable field reverts rather than resetting to
        /// zero -- a half-typed "-" or a stray keystroke should not destroy the number that was there.
        /// </summary>
        private void CommitText()
        {
            if (TryParseText(out var parsed))
            {
                Value = parsed;
            }
            else
            {
                SyncTextFromValue();
            }

            ValueCommitted?.Invoke(this, _value);
        }

        private float Coerce(float value)
        {
            if (float.IsNaN(value))
            {
                return _value;
            }

            if (IsInteger)
            {
                // Away from zero, not the default banker's rounding: in a field, 0.5 becoming 0 reads as a
                // bug, and it has to match how FormatValue prints the same number.
                value = MathF.Round(value, MidpointRounding.AwayFromZero);
            }

            var low = MathF.Min(_minimum, _maximum);
            var high = MathF.Max(_minimum, _maximum);
            return Math.Clamp(value, low, high);
        }

        private static bool IsNumericCharacter(char character)
        {
            return char.IsAsciiDigit(character)
                || character is '-' or '+' or '.' or 'e' or 'E';
        }

        // ----- input ----------------------------------------------------------------------------------

        /// <summary>
        /// A press is a scrub only when it lands on the strip. Everywhere else it is a text click, handled
        /// by the base exactly as in any other field -- caret placement, drag-select, double-click.
        /// </summary>
        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsScrubbable || !DragStripRect.Contains(e.Position))
            {
                base.OnPointerPressed(e);
                return;
            }

            _dragging = true;
            _passedThreshold = false;
            _dragOrigin = e.Position;
            _dragOriginValue = _value;
            _sweepAnchorX = e.Position.X;
            _sweepAnchorValue = _value;

            // The strip does nothing but scrub, so a bounded field can jump to the pointer on the press
            // itself rather than waiting for movement -- there is no click-to-type here to protect.
            // Suppressed when a modifier is already down, since that press is asking to adjust the value it
            // has rather than to reposition it.
            _sweepFine = e.Modifiers.HasFlag(UiModifiers.Shift)
                || e.Modifiers.HasFlag(UiModifiers.Control)
                || e.Modifiers.HasFlag(UiModifiers.Meta);

            if (SweepsRange && !_sweepFine)
            {
                _passedThreshold = true;
                Value = Snap(ValueAt(e.Position.X), Step > 0.0f ? Step : 0.01f);
            }

            CapturePointer();
            e.Handled = true;
        }

        /// <summary>The value at a horizontal position along the strip, for a bounded field.</summary>
        private float ValueAt(float x)
        {
            var fraction = (x - Bounds.X) / MathF.Max(1.0f, Bounds.Width);
            return _minimum + Math.Clamp(fraction, 0.0f, 1.0f) * (_maximum - _minimum);
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_dragging)
            {
                base.OnPointerMoved(e);
                return;
            }

            var delta = e.Position - _dragOrigin;
            if (!_passedThreshold)
            {
                if (MathF.Abs(delta.X) < DragThreshold && MathF.Abs(delta.Y) < DragThreshold)
                {
                    return;
                }

                _passedThreshold = true;
            }

            var step = Step > 0.0f ? Step : 0.01f;
            var sensitivity = 1.0f;

            if (e.Modifiers.HasFlag(UiModifiers.Shift))
            {
                sensitivity = FastMultiplier;
            }
            else if (e.Modifiers.HasFlag(UiModifiers.Control) || e.Modifiers.HasFlag(UiModifiers.Meta))
            {
                sensitivity = SlowMultiplier;
            }

            float target;

            if (SweepsRange)
            {
                var fine = sensitivity != 1.0f;

                // Re-anchored whenever the modifier state flips, so switching to fine adjustment mid-drag
                // continues from where the value already is instead of snapping somewhere else.
                if (fine != _sweepFine)
                {
                    _sweepFine = fine;
                    _sweepAnchorX = e.Position.X;
                    _sweepAnchorValue = _value;
                }

                if (fine)
                {
                    // Relative from the anchor: absolute positioning has no room for precision, since the
                    // whole range is one field width wide.
                    var travel = (e.Position.X - _sweepAnchorX) / SweepDistance * sensitivity;
                    target = _sweepAnchorValue + travel * (_maximum - _minimum);
                }
                else
                {
                    // Absolute, like a slider track: the value goes to wherever the pointer is along the
                    // strip, rather than accumulating from where the press landed. A field sitting at 75%
                    // grabbed at 25% jumps there and tracks from then on.
                    target = ValueAt(e.Position.X);
                }
            }
            else
            {
                // Nothing to sweep, so pace by the step instead: whole steps of pointer travel, never a
                // continuous scale of it, so a small hand movement cannot throw an unbounded value.
                // Counted from where the press landed rather than from the previous move -- accumulating
                // per-move deltas would drift whenever clamping at a limit swallowed part of one.
                var pixels = MathF.Max(0.5f, PixelsPerStep / sensitivity);
                var steps = MathF.Round(delta.X / pixels, MidpointRounding.AwayFromZero);
                target = _dragOriginValue + steps * step;
            }

            Value = Snap(target, step);
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_dragging)
            {
                base.OnPointerReleased(e);
                return;
            }

            _dragging = false;
            ReleasePointerCapture();

            // A press on the strip that never moved still counts as a scrub for a bounded field, because it
            // jumped on the press. For an unbounded one nothing happened, so there is nothing to commit.
            if (_passedThreshold)
            {
                ValueCommitted?.Invoke(this, _value);
            }

            _passedThreshold = false;
            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (IsEffectivelyEnabled && !IsReadOnly && e.Key is UiKey.Up or UiKey.Down)
            {
                var step = Step > 0.0f ? Step : 1.0f;
                if (e.Modifiers.HasFlag(UiModifiers.Shift))
                {
                    step *= FastMultiplier;
                }

                // Off the typed text rather than the stored value, so arrows continue from what is on screen
                // mid-edit instead of jumping back to the last committed number.
                var start = TryParseText(out var typed) ? typed : _value;
                Value = Snap(start + (e.Key == UiKey.Up ? step : -step), step);
                ValueCommitted?.Invoke(this, _value);
                SelectAll();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);

            if (e.Key == UiKey.Enter && e.Handled)
            {
                CommitText();
                SelectAll();
            }
        }

        protected override void OnLostFocus()
        {
            base.OnLostFocus();
            CommitText();
        }

        /// <summary>Snaps to whole multiples of the step, so a drag lands on round numbers.</summary>
        private static float Snap(float value, float step)
        {
            return step > 0.0f ? MathF.Round(value / step) * step : value;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var size = base.MeasureOverride(availableSize);

            // A bottom strip is extra height on top of whatever the text needs, not a slice taken out of
            // it -- otherwise a numeric field would sit shorter than the text box beside it in the same
            // row. A leading chip costs nothing vertically, which is the whole reason to prefer it.
            if (DragHandle == NumericDragHandle.Strip && DragStripHeight > 0.0f)
            {
                return new Vector2(size.X, size.Y + DragStripHeight);
            }

            return size;
        }

        /// <summary>
        /// The cursor changes at the handle's edge, which is how the two halves announce themselves: an
        /// I-beam over the text that selects, resize arrows over the handle that scrubs.
        /// </summary>
        public override UiCursor GetCursor(Vector2 point)
        {
            if (!IsEffectivelyEnabled || IsReadOnly)
            {
                return UiCursor.Arrow;
            }

            return DragStripRect.Contains(point) ? UiCursor.SizeWE : UiCursor.IBeam;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            base.OnRender(drawList);
            RenderDragStrip(drawList);
        }

        /// <summary>
        /// The handle. A leading chip carries its own label and colour; a bottom strip is a recessed track
        /// filled to the value's position. Either way it has to be visible -- it is the only part of the
        /// control that responds to a drag, and an invisible drag target is one nobody finds.
        /// </summary>
        private void RenderDragStrip(ManaUiDrawList drawList)
        {
            if (DragHandle == NumericDragHandle.Leading)
            {
                RenderDragChip(drawList);
                return;
            }

            if (DragHandle != NumericDragHandle.Strip || DragStripHeight <= 0.0f)
            {
                return;
            }

            var theme = Theme;
            var strip = DragStripRect.Deflate(new Thickness(3.0f, 2.0f, 3.0f, 2.0f));
            if (strip.Width <= 0.0f || strip.Height <= 0.0f)
            {
                return;
            }

            var corners = new CornerRadius(strip.Height * 0.5f);
            var enabled = IsScrubbable;
            var active = IsDragging || (enabled && IsPointerOver);

            drawList.FillRect(strip, enabled ? theme.SurfaceAlt : theme.Surface, corners);

            if (!enabled)
            {
                return;
            }

            var accent = active ? theme.AccentHover : theme.Accent;
            var tint = new Vector4(accent.X, accent.Y, accent.Z, accent.W * (active ? 0.95f : 0.65f));

            if (SweepsRange)
            {
                // Filled from the left, so the strip reads as the position it actually is.
                var progress = Math.Clamp((_value - _minimum) / (_maximum - _minimum), 0.0f, 1.0f);
                var width = strip.Width * progress;
                if (width > 0.0f)
                {
                    drawList.FillRect(new UiRect(strip.X, strip.Y, width, strip.Height), tint, corners);
                }

                return;
            }

            // Unbounded: there is no position to show, so the strip carries a centre mark instead. It says
            // "drag me" without claiming to mean anything about where the value sits.
            var mark = MathF.Min(strip.Width, 18.0f);
            drawList.FillRect(
                new UiRect(strip.Center.X - mark * 0.5f, strip.Y, mark, strip.Height),
                tint,
                corners);
        }

        /// <summary>
        /// The leading chip: a coloured square carrying its letter, which is both the drag handle and the
        /// field's label. Costs width instead of height, which is the trade a dense inspector wants.
        /// </summary>
        private void RenderDragChip(ManaUiDrawList drawList)
        {
            var root = Root;
            if (root == null || DragChipWidth <= 0.0f)
            {
                return;
            }

            var theme = Theme;
            var chip = DragStripRect.Deflate(new Thickness(1.0f));
            if (chip.Width <= 0.0f || chip.Height <= 0.0f)
            {
                return;
            }

            var enabled = IsScrubbable;
            var active = IsDragging || (enabled && chip.Contains(root.Input.PointerPosition));

            var fill = ChipColor.W > 0.0f ? ChipColor : theme.SurfaceRaised;
            if (!enabled)
            {
                fill = theme.Surface;
            }
            else if (active)
            {
                fill = Composite(fill, theme.Hover);
            }

            drawList.FillRect(chip, fill, new CornerRadius(theme.CornerRadius.TopLeft));

            if (ChipText.Length == 0)
            {
                return;
            }

            // Centred by the glyph's own metrics rather than by a text layout: the chip holds one or two
            // characters, and a layout pass per field per frame is not worth it in a panel full of them.
            var font = root.Fonts.Get(theme.FontFamilyMedium, theme.FontSizeSmall, root.Scale);
            var width = font.MeasureWidth(ChipText);
            var color = enabled ? theme.TextOnAccent : theme.TextDisabled;

            new TextLayout
            {
                Font = font,
                Text = ChipText,
                MaxWidth = MathF.Max(width, chip.Width)
            }.Render(
                drawList,
                new UiRect(chip.Center.X - width * 0.5f, chip.Y, width, chip.Height),
                color,
                VerticalAlignment.Center);
        }

        /// <summary>Text set from outside is still the source of truth for the number it spells.</summary>
        private void OnTextChangedCore(string text)
        {
            if (_syncing)
            {
                return;
            }

            // While the field has focus the user is mid-keystroke and "1." or "-" are legitimate
            // intermediate states, so parsing waits for the commit.
            if (!IsFocused && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                _value = Coerce(parsed);
            }
        }
    }
}
