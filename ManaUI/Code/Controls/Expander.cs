namespace ManaUI
{
    /// <summary>
    /// A collapsible section: a clickable header with a disclosure triangle, and content that folds away.
    /// The inspector is built almost entirely out of these -- the direct replacement for ImGui's
    /// CollapsingHeader.
    /// </summary>
    public class Expander : Decorator
    {
        private readonly StackPanel _layout = new() { Orientation = Orientation.Vertical };
        private readonly ExpanderHeader _header;

        private UiElement? _content;
        private bool _isExpanded = true;

        public Expander()
        {
            // A section is as tall as what is in it. Stretching would make a collapsed one still occupy the
            // whole panel, which defeats the point of collapsing it.
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;

            _header = new ExpanderHeader(this);
            _layout.Children.Add(_header);
            Child = _layout;
        }

        public event EventHandler<bool>? ExpandedChanged;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                ApplyExpansion();
                ExpandedChanged?.Invoke(this, value);
            }
        }

        public string Header
        {
            get => _header.Text;
            set => _header.Text = value ?? string.Empty;
        }

        /// <summary>Extra element on the header row, right-aligned -- a count, a reset button, a toggle.</summary>
        public UiElement? HeaderAccessory
        {
            get => _header.Accessory;
            set => _header.Accessory = value;
        }

        /// <summary>Inset applied to the content only, so it reads as belonging under the header.</summary>
        public Thickness ContentPadding { get; set; } = new Thickness(12.0f, 4.0f, 0.0f, 4.0f);

        public UiElement? Content
        {
            get => _content;
            set
            {
                if (ReferenceEquals(_content, value))
                {
                    return;
                }

                if (_content != null)
                {
                    _layout.Children.Remove(_content);
                }

                _content = value;

                if (_content != null)
                {
                    _content.Margin = ContentPadding;
                    _layout.Children.Add(_content);
                }

                ApplyExpansion();
            }
        }

        public void Toggle()
        {
            IsExpanded = !IsExpanded;
        }

        /// <summary>
        /// Collapsed, not removed. Keeping the element in the tree is what preserves scroll positions,
        /// focus and any bindings inside a section the user folds away and reopens.
        /// </summary>
        private void ApplyExpansion()
        {
            if (_content != null)
            {
                _content.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            _header.InvalidateVisual();
            InvalidateMeasure();
        }

        /// <summary>
        /// The header row. Its own control so it owns the hover, press and focus states -- an Expander that
        /// handled them itself would light up when the pointer was anywhere over its content.
        /// </summary>
        private sealed class ExpanderHeader : Control
        {
            private const float ChevronSize = 8.0f;

            private readonly Expander _owner;
            private readonly TextBlock _label = new();
            private readonly Grid _grid = new();

            private UiElement? _accessory;

            public ExpanderHeader(Expander owner)
            {
                _owner = owner;
                Focusable = true;
                HorizontalAlignment = HorizontalAlignment.Stretch;

                _grid.SetColumns("Auto, *, Auto");
                _grid.SetRows("Auto");
                _grid.ColumnSpacing = 6.0f;

                // Column 0 is left empty and sized to the chevron, which OnRender draws into. A spacer
                // element would be one more thing to lay out for something that is two triangles wide.
                _grid.Children.Add(new Border().Size(ChevronSize + 4.0f, ChevronSize).GridColumn(0));
                _grid.Children.Add(_label.CenterV().GridColumn(1));

                Child = _grid;
                Padding = new Thickness(4.0f, 3.0f);
            }

            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            public UiElement? Accessory
            {
                get => _accessory;
                set
                {
                    if (ReferenceEquals(_accessory, value))
                    {
                        return;
                    }

                    if (_accessory != null)
                    {
                        _grid.Children.Remove(_accessory);
                    }

                    _accessory = value;

                    if (_accessory != null)
                    {
                        _grid.Children.Add(_accessory.GridColumn(2));
                    }

                    InvalidateMeasure();
                }
            }

            protected override void OnRender(ManaUiDrawList drawList)
            {
                var theme = Theme;

                _label.Foreground = theme.Text;
                _label.FontFamily = theme.FontFamilyMedium;

                drawList.FillRect(Bounds, ApplyStateOverlay(theme.Surface), theme.CornerRadius);
                RenderFocusRing(drawList, Bounds, theme.CornerRadius);
                DrawChevron(drawList, theme);
            }

            /// <summary>
            /// Two nested boxes rather than a glyph: the SDF primitive has no triangle, and a font-dependent
            /// arrow would drift with the theme's font. Right-pointing when collapsed, down when expanded.
            /// </summary>
            private void DrawChevron(ManaUiDrawList drawList, UiTheme theme)
            {
                var content = Bounds.Deflate(ContentInset);
                var centre = new Vector2(content.X + (ChevronSize + 4.0f) * 0.5f, content.Center.Y);
                var color = IsEffectivelyEnabled ? theme.TextMuted : theme.TextDisabled;

                var half = ChevronSize * 0.5f;
                var corners = new CornerRadius(1.0f);

                if (_owner.IsExpanded)
                {
                    drawList.FillRect(
                        new UiRect(centre.X - half, centre.Y - 1.0f, ChevronSize, 2.0f), color, corners);
                    drawList.FillRect(
                        new UiRect(centre.X - 1.0f, centre.Y - half + 1.0f, 2.0f, ChevronSize - 2.0f), color, corners);
                    return;
                }

                drawList.FillRect(
                    new UiRect(centre.X - half, centre.Y - 1.0f, ChevronSize, 2.0f), color, corners);
            }

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

                // Only a release still over the header counts, so dragging off cancels the toggle the way
                // every other button in the library does.
                if (Bounds.Contains(e.Position))
                {
                    _owner.Toggle();
                }

                e.Handled = true;
            }

            protected override void OnKeyDown(UiKeyEventArgs e)
            {
                switch (e.Key)
                {
                    case UiKey.Space or UiKey.Enter:
                        _owner.Toggle();
                        break;
                    case UiKey.Left when _owner.IsExpanded:
                        _owner.IsExpanded = false;
                        break;
                    case UiKey.Right when !_owner.IsExpanded:
                        _owner.IsExpanded = true;
                        break;
                    default:
                        return;
                }

                e.Handled = true;
            }

            public override UiCursor GetCursor(Vector2 point)
            {
                return IsEffectivelyEnabled ? UiCursor.Hand : UiCursor.Arrow;
            }
        }
    }
}
