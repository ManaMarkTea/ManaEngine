namespace ManaUI
{
    /// <summary>
    /// A colour swatch that opens a <see cref="ColorPicker"/> when clicked -- the form-row shape of a colour
    /// property, where a full picker inline would dwarf every other row in the panel.
    /// <para>
    /// The swatch shows the value against a chequer, so a translucent colour is distinguishable from a pale
    /// opaque one. Against a flat panel background the two look identical, which matters most for exactly
    /// the colours where alpha is being edited deliberately.
    /// </para>
    /// </summary>
    public class ColorField : Control
    {
        private const float ChequerSize = 6.0f;

        private Vector4 _color = UiColor.White;
        private UiPopup? _popup;
        private ColorPicker? _picker;

        public ColorField()
        {
            Focusable = true;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
        }

        /// <summary>Fires continuously while the picker is being dragged.</summary>
        public event EventHandler<Vector4>? ColorChanged;

        /// <summary>Fires when an edit settles, and when the picker closes.</summary>
        public event EventHandler<Vector4>? ColorCommitted;

        public Vector4 Color
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    return;
                }

                _color = value;
                InvalidateVisual();

                // Pushed into an open picker so a value changed elsewhere -- a bound property, an
                // animation -- is reflected rather than being silently overwritten on the next drag.
                if (_picker != null)
                {
                    _picker.Color = value;
                }

                ColorChanged?.Invoke(this, value);
            }
        }

        public bool ShowAlpha { get; set; } = true;

        /// <summary>Shown beside the swatch. Empty hides the text entirely and leaves a bare swatch.</summary>
        public bool ShowHexLabel { get; set; } = true;

        public bool IsOpen => _popup?.IsOpen == true;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = float.IsInfinity(availableSize.X) ? 120.0f : availableSize.X;
            var font = Root?.Fonts.Get(Theme.FontFamily, Theme.FontSize, Root.Scale);
            var height = font == null ? Theme.ControlHeight : font.LineHeight + 4.0f;
            return new Vector2(width, MathF.Max(18.0f, height));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var root = Root;
            var corners = theme.CornerRadius;
            var enabled = IsEffectivelyEnabled;

            var swatchWidth = ShowHexLabel ? MathF.Min(Bounds.Width, 34.0f) : Bounds.Width;
            var swatch = new UiRect(Bounds.X, Bounds.Y, swatchWidth, Bounds.Height);

            RenderChequer(drawList, swatch, corners);

            drawList.DrawBox(
                swatch,
                enabled ? _color : _color.WithAlpha(_color.W * 0.4f),
                ApplyStateOverlay(theme.Border),
                theme.BorderThickness,
                corners);

            RenderFocusRing(drawList, swatch, corners);

            if (!ShowHexLabel || root == null || Bounds.Width <= swatchWidth + 4.0f)
            {
                return;
            }

            var font = root.Fonts.Get(theme.FontFamilyMono, theme.FontSizeSmall, root.Scale);
            new TextLayout
            {
                Font = font,
                Text = UiColor.ToHex(_color, ShowAlpha),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Bounds.Width - swatchWidth - 8.0f
            }.Render(
                drawList,
                new UiRect(
                    swatch.Right + 8.0f,
                    Bounds.Y,
                    Bounds.Width - swatchWidth - 8.0f,
                    Bounds.Height),
                enabled ? theme.TextMuted : theme.TextDisabled,
                VerticalAlignment.Center);
        }

        /// <summary>
        /// The light/dark grid behind the swatch. Drawn before the colour so alpha reveals it, which is the
        /// only way a translucent value reads as translucent rather than as a paler one.
        /// </summary>
        private void RenderChequer(ManaUiDrawList drawList, UiRect area, CornerRadius corners)
        {
            if (_color.W >= 1.0f)
            {
                return;
            }

            drawList.PushClip(area);
            drawList.FillRect(area, UiColor.White, corners);

            var columns = (int)MathF.Ceiling(area.Width / ChequerSize);
            var rows = (int)MathF.Ceiling(area.Height / ChequerSize);
            var dark = new Vector4(0.72f, 0.72f, 0.74f, 1.0f);

            for (var y = 0; y < rows; y++)
            {
                for (var x = (y % 2 == 0) ? 0 : 1; x < columns; x += 2)
                {
                    drawList.FillRect(
                        new UiRect(
                            area.X + x * ChequerSize,
                            area.Y + y * ChequerSize,
                            MathF.Min(ChequerSize, area.Right - (area.X + x * ChequerSize)),
                            MathF.Min(ChequerSize, area.Bottom - (area.Y + y * ChequerSize))),
                        dark);
                }
            }

            drawList.PopClip();
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            IsPressed = true;
            Focus();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsPressed)
            {
                return;
            }

            IsPressed = false;

            // Only a release still over the swatch counts, so dragging off cancels -- the same contract
            // every other button in the library follows.
            if (Bounds.Contains(e.Position))
            {
                Toggle();
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            switch (e.Key)
            {
                case UiKey.Space or UiKey.Enter:
                    Toggle();
                    break;
                case UiKey.Escape when IsOpen:
                    Close();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        public void Open()
        {
            var root = Root;
            if (root == null || IsOpen)
            {
                return;
            }

            var theme = root.Theme;

            _picker = new ColorPicker
            {
                Color = _color,
                ShowAlpha = ShowAlpha,
                Width = 220.0f
            };

            _picker.ColorChanged += (_, color) => Color = color;
            _picker.ColorCommitted += (_, color) => ColorCommitted?.Invoke(this, color);

            _popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                Child = new Border()
                    .Fill(theme.SurfaceRaised)
                    .Outline(theme.BorderStrong, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Shadow(14.0f)
                    .PaddingOf(8.0f)
                    .Content(_picker)
            };

            _popup.Closed += (_, _) =>
            {
                _popup = null;
                _picker = null;

                // Closing settles the value, so anything too expensive to run per drag pixel has a single
                // moment to act on.
                ColorCommitted?.Invoke(this, _color);
                InvalidateVisual();
            };

            root.Popups.Show(_popup, this);
            InvalidateVisual();
        }

        public void Close()
        {
            _popup?.Close();
        }

        public override UiCursor GetCursor(Vector2 point)
        {
            return IsEffectivelyEnabled ? UiCursor.Hand : UiCursor.Arrow;
        }
    }
}
