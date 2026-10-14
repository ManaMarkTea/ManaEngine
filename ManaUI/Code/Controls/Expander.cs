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
        private Vector4? _headerForeground;
        private Vector4? _headerBackground;

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

        /// <summary>Optional heading colour; null follows the theme.</summary>
        public Vector4? HeaderForeground
        {
            get => _headerForeground;
            set { _headerForeground = value; _header.InvalidateVisual(); }
        }

        /// <summary>Optional header fill; null follows the theme.</summary>
        public Vector4? HeaderBackground
        {
            get => _headerBackground;
            set { _headerBackground = value; _header.InvalidateVisual(); }
        }

        /// <summary>Heading size in logical pixels; NaN follows the theme.</summary>
        public float HeaderFontSize
        {
            get => _header.TextSize;
            set => _header.TextSize = value;
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

            _header.UpdateDisclosure();
            InvalidateMeasure();
        }

        /// <summary>
        /// The header row. Its own control so it owns the hover, press and focus states -- an Expander that
        /// handled them itself would light up when the pointer was anywhere over its content.
        /// </summary>
        private sealed class ExpanderHeader : Control
        {
            private readonly Expander _owner;
            private readonly TextBlock _label = new();
            private readonly TextBlock _disclosure = new();
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

                _grid.Children.Add(_disclosure.Width(20.0f).CenterV().GridColumn(0));
                _grid.Children.Add(_label.CenterV().GridColumn(1));
                UpdateDisclosure();

                Child = _grid;
                Padding = new Thickness(4.0f, 3.0f);
            }

            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            public float TextSize
            {
                get => _label.FontSize;
                set => _label.FontSize = value;
            }

            public void UpdateDisclosure()
            {
                var text = Theme.Kind is UiThemeKind.Windows31 or UiThemeKind.Lcars ? "" : _owner.IsExpanded ? "🔽" : "▶️";
                if (_disclosure.Text != text)
                {
                    _disclosure.Text = text;
                    InvalidateVisual();
                }
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

                _label.Foreground = _owner.HeaderForeground ?? theme.Text;
                _label.FontFamily = theme.FontFamilyMedium;

                var headerFill = ApplyStateOverlay(_owner.HeaderBackground ?? theme.Surface);
                if (!drawList.DrawSkin(theme, "button", Bounds, headerFill))
                {
                    drawList.FillRect(Bounds, headerFill, theme.CornerRadius);
                }
                UpdateDisclosure();
                var centre = _disclosure.Bounds.Center;
                if (theme.Kind == UiThemeKind.Windows31)
                {
                    var box = new UiRect(centre.X - 9, centre.Y - 8, 18, 16);
                    drawList.DrawSkin(theme, IsPressed ? "buttonPressed" : "button", box, UiColor.White);
                    drawList.FillRect(new UiRect(centre.X - 5, centre.Y - 1.5f, 10, 3), UiColor.Black);
                    drawList.FillRect(new UiRect(centre.X - 4, centre.Y - 0.5f, 8, 1), UiColor.White);
                }
                else if (theme.Kind == UiThemeKind.Lcars)
                {
                    var width = _owner.IsExpanded ? 14 : 4;
                    var height = _owner.IsExpanded ? 4 : 14;
                    drawList.FillRect(new UiRect(centre.X - width * 0.5f, centre.Y - height * 0.5f,
                        width, height), theme.Accent, new CornerRadius(2));
                }
                RenderFocusRing(drawList, Bounds, theme.CornerRadius);
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
