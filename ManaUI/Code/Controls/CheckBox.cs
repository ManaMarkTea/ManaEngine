namespace ManaUI
{
    /// <summary>
    /// A labelled on/off toggle. The box and tick are drawn directly rather than composed from child
    /// elements -- at three rectangles it is cheaper and simpler than a template.
    /// </summary>
    public class CheckBox : Control
    {
        private const float BoxSize = 16.0f;

        private bool _isChecked;
        private string _text = string.Empty;
        private TextBlock? _label;

        public CheckBox()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public CheckBox(string text)
            : this()
        {
            Text = text;
        }

        public event EventHandler<bool>? CheckedChanged;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                InvalidateVisual();
                CheckedChanged?.Invoke(this, value);
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value)
                {
                    return;
                }

                _text = value ?? string.Empty;
                EnsureLabel().Text = _text;
            }
        }

        /// <summary>The label sits to the right of the box, so the child is inset past it.</summary>
        protected override Thickness ContentInset =>
            new Thickness(BoxSize + Theme.Spacing, 0.0f, 0.0f, 0.0f);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var desired = base.MeasureOverride(availableSize);
            return new Vector2(
                MathF.Max(desired.X, BoxSize),
                MathF.Max(desired.Y, MathF.Max(BoxSize, Theme.ControlHeight)));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var enabled = IsEffectivelyEnabled;

            // Vertically centred against the whole control so the box lines up with the label's text
            // regardless of how tall the row ends up.
            var box = new UiRect(
                Bounds.X,
                Bounds.Y + (Bounds.Height - BoxSize) * 0.5f,
                BoxSize,
                BoxSize);

            var corners = new CornerRadius(3.0f);

            if (_isChecked)
            {
                var fill = enabled ? theme.Accent : theme.SurfaceAlt;
                if (!drawList.DrawSkin(theme, "primary", box, enabled ? (IsPointerOver ? new Vector4(1.15f, 1.15f, 1.15f, 1.0f) : UiColor.White) : new Vector4(0.5f, 0.5f, 0.5f, 1.0f), lift: false))
                {
                    drawList.DrawBox(box, ApplyStateOverlay(fill), UiColor.Transparent, 0.0f, corners);
                }
                DrawTick(drawList, box, enabled ? theme.TextOnAccent : theme.TextDisabled);
            }
            else
            {
                if (drawList.DrawSkin(theme, "input", box, enabled ? UiColor.White : new Vector4(1.0f, 1.0f, 1.0f, 0.5f), lift: false))
                {
                    drawList.DrawBorder(box, theme.BorderStrong, 1.0f, corners);
                }
                else
                {
                    drawList.DrawBox(
                        box,
                        ApplyStateOverlay(theme.SurfaceAlt),
                        enabled ? theme.BorderStrong : theme.Border,
                        theme.BorderThickness,
                        corners);
                }
            }

            RenderFocusRing(drawList, box, corners);

            if (_label != null)
            {
                _label.Foreground = enabled ? theme.Text : theme.TextDisabled;
            }
        }

        /// <summary>
        /// Draws the tick. Drawn from primitives rather than as U+2713, which Roboto does not have --
        /// it rendered as .notdef, and the "fall back to a square" path never fired because a tofu box has
        /// pixels like any other glyph.
        /// </summary>
        private void DrawTick(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            var root = Root;
            var font = root?.Fonts.Get(root.Theme.FontFamily, BoxSize, root.Scale);
            UiIcons.Check(drawList, box.Deflate(new Thickness(2.0f)), color, font);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            IsPressed = true;
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            var wasPressed = IsPressed;
            IsPressed = false;
            ReleasePointerCapture();

            if (wasPressed && Bounds.Contains(e.Position) && IsEffectivelyEnabled)
            {
                IsChecked = !IsChecked;
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (e.Key == UiKey.Space && IsEffectivelyEnabled)
            {
                IsChecked = !IsChecked;
                e.Handled = true;
            }
        }

        private TextBlock EnsureLabel()
        {
            if (_label == null)
            {
                _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                Child = _label;
            }

            return _label;
        }
    }
}
