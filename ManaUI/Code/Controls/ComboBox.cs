using System.Collections;

namespace ManaUI
{
    /// <summary>
    /// A dropdown selector. Closed it shows the selection; open it shows the options in a popup.
    /// <para>
    /// The list lives in a popup rather than inline so it is not clipped by whatever scroll viewer or
    /// panel the control happens to sit in -- the reason the popup layer exists.
    /// </para>
    /// </summary>
    public class ComboBox : Control
    {
        private const float ArrowWidth = 18.0f;

        private IEnumerable? _itemsSource;
        private int _selectedIndex = -1;
        private UiPopup? _popup;
        private readonly List<object> _items = new();

        public ComboBox()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public event EventHandler<int>? SelectionChanged;

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                _itemsSource = value;
                RebuildItems();
            }
        }

        /// <summary>Formats an item for display. Defaults to ToString.</summary>
        public Func<object, string> DisplayMember { get; set; } = item => item?.ToString() ?? string.Empty;

        /// <summary>Longest list height before it scrolls.</summary>
        public float MaxDropDownHeight { get; set; } = 260.0f;

        public string Placeholder { get; set; } = "Select…";

        public IReadOnlyList<object> Items => _items;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
                if (_selectedIndex == clamped)
                {
                    return;
                }

                _selectedIndex = clamped;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, clamped);
            }
        }

        public object? SelectedItem =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        public bool IsDropDownOpen => _popup is { IsOpen: true };

        public void SetItems<T>(IEnumerable<T> items, Func<T, string>? display = null)
        {
            if (display != null)
            {
                DisplayMember = item => display((T)item);
            }

            ItemsSource = (IEnumerable)items;
        }

        private void RebuildItems()
        {
            _items.Clear();

            if (_itemsSource != null)
            {
                foreach (var item in _itemsSource)
                {
                    _items.Add(item!);
                }
            }

            _selectedIndex = Math.Clamp(_selectedIndex, -1, _items.Count - 1);
            InvalidateVisual();
        }

        protected override Thickness ContentInset => Padding.IsZero ? Theme.ControlPadding : Padding;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = float.IsInfinity(availableSize.X) ? 160.0f : availableSize.X;
            return new Vector2(width, Theme.ControlHeight);
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root;
            var theme = Theme;
            if (root == null)
            {
                return;
            }

            var enabled = IsEffectivelyEnabled;
            var corners = theme.CornerRadius;

            var comboFill = ApplyStateOverlay(enabled ? theme.SurfaceRaised : theme.SurfaceAlt);
            if (drawList.DrawSkin(theme, "button", Bounds, comboFill))
            {
                if (IsDropDownOpen)
                {
                    drawList.DrawBorder(Bounds, theme.Accent, 1.5f, corners);
                }
            }
            else
            {
                drawList.DrawBox(
                    Bounds,
                    comboFill,
                    IsDropDownOpen ? theme.Accent : theme.Border,
                    theme.BorderThickness,
                    corners);
            }

            RenderFocusRing(drawList, Bounds, corners);

            var inset = ContentInset;
            var textArea = new UiRect(
                Bounds.X + inset.Left,
                Bounds.Y,
                MathF.Max(0.0f, Bounds.Width - inset.Horizontal - ArrowWidth),
                Bounds.Height);

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var hasSelection = SelectedItem != null;

            var layout = new TextLayout
            {
                Font = font,
                Text = hasSelection ? DisplayMember(SelectedItem!) : Placeholder,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxWidth = textArea.Width
            };

            var color = !enabled ? theme.TextDisabled : hasSelection ? theme.Text : theme.TextDisabled;
            layout.Render(drawList, textArea, color, VerticalAlignment.Center);

            RenderArrow(drawList, font, enabled ? theme.TextMuted : theme.TextDisabled);
        }

        private void RenderArrow(ManaUiDrawList drawList, ManaFont font, Vector4 color)
        {
            // Drawn, not a glyph. The triangle characters are absent from Roboto and from most UI fonts,
            // and a control's chrome should not change shape with the theme's typeface anyway.
            var centre = new Vector2(Bounds.Right - ArrowWidth * 0.5f - 4.0f, Bounds.Center.Y);
            var box = new UiRect(centre.X - 4.5f, centre.Y - 3.0f, 9.0f, 6.0f);

            if (drawList.DrawSkinIcon(Theme, IsDropDownOpen ? "arrowUp" : "arrowDown", new UiRect(centre.X - 6.0f, centre.Y - 6.0f, 12.0f, 12.0f), UiColor.White))
            {
                return;
            }

            UiIcons.Triangle(
                drawList,
                box,
                IsDropDownOpen ? UiIconDirection.Up : UiIconDirection.Down,
                color,
                font);
        }

        // ----- drop down ------------------------------------------------------------------------------

        public void OpenDropDown()
        {
            var root = Root;
            if (root == null || IsDropDownOpen || !IsEffectivelyEnabled || _items.Count == 0)
            {
                return;
            }

            var theme = root.Theme;
            var list = new StackPanel().Vertical();

            for (var i = 0; i < _items.Count; i++)
            {
                list.Children.Add(BuildOptionRow(theme, i));
            }

            _popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                MatchAnchorWidth = true,
                MaxHeight = MaxDropDownHeight,
                Child = new Border()
                    .Fill(theme.SurfaceRaised)
                    .Outline(theme.BorderStrong, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Shadow(14.0f)
                    .PaddingOf(3.0f)
                    .Content(list.Scrollable().Configure(viewer => viewer.MaxHeight = MaxDropDownHeight - 6.0f))
            };

            _popup.Closed += (_, _) => InvalidateVisual();

            root.Popups.Show(_popup, this);
            InvalidateVisual();
        }

        private UiElement BuildOptionRow(UiTheme theme, int index)
        {
            var isSelected = index == _selectedIndex;

            var row = new ComboBoxOption(this, index)
            {
                Child = new TextBlock()
                    .Text(DisplayMember(_items[index]))
                    .Foreground(isSelected ? theme.TextOnAccent : theme.Text)
                    .Ellipsis()
                    .CenterV()
            };

            row.IsSelectedOption = isSelected;
            return row;
        }

        public void CloseDropDown()
        {
            if (_popup != null)
            {
                Root?.Popups.Close(_popup);
                _popup = null;
            }

            InvalidateVisual();
        }

        internal void CommitSelection(int index)
        {
            SelectedIndex = index;
            CloseDropDown();
            Focus();
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            Focus();

            if (IsDropDownOpen)
            {
                CloseDropDown();
            }
            else
            {
                OpenDropDown();
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (!IsEffectivelyEnabled || _items.Count == 0)
            {
                return;
            }

            switch (e.Key)
            {
                case UiKey.Down when !IsDropDownOpen:
                case UiKey.Right:
                    SelectedIndex = Math.Min(_selectedIndex + 1, _items.Count - 1);
                    e.Handled = true;
                    break;
                case UiKey.Up when !IsDropDownOpen:
                case UiKey.Left:
                    SelectedIndex = Math.Max(_selectedIndex - 1, 0);
                    e.Handled = true;
                    break;
                case UiKey.Down when IsDropDownOpen:
                    SelectedIndex = Math.Min(_selectedIndex + 1, _items.Count - 1);
                    RefreshDropDown();
                    e.Handled = true;
                    break;
                case UiKey.Up when IsDropDownOpen:
                    SelectedIndex = Math.Max(_selectedIndex - 1, 0);
                    RefreshDropDown();
                    e.Handled = true;
                    break;
                case UiKey.Enter or UiKey.Space:
                    if (IsDropDownOpen)
                    {
                        CloseDropDown();
                    }
                    else
                    {
                        OpenDropDown();
                    }

                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Rebuilds the open list so the highlighted row follows keyboard selection.</summary>
        private void RefreshDropDown()
        {
            if (!IsDropDownOpen)
            {
                return;
            }

            CloseDropDown();
            OpenDropDown();
        }

        protected override void OnPointerWheel(UiPointerWheelEventArgs e)
        {
            // Only when focused, for the same reason as Slider: a combo that consumed any wheel under the
            // pointer would hijack scrolling of the panel it sits in and silently change a value.
            if (!IsEffectivelyEnabled || !IsFocused || IsDropDownOpen || _items.Count == 0)
            {
                return;
            }

            SelectedIndex = Math.Clamp(_selectedIndex - (int)MathF.Sign(e.Delta.Y), 0, _items.Count - 1);
            e.Handled = true;
        }
    }

    /// <summary>One row in an open dropdown.</summary>
    internal sealed class ComboBoxOption : Control
    {
        private readonly ComboBox _owner;
        private readonly int _index;

        public ComboBoxOption(ComboBox owner, int index)
        {
            _owner = owner;
            _index = index;

            // Rows are pointer-driven; keyboard selection is handled by the ComboBox itself, so they stay
            // out of the tab order.
            Focusable = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        public bool IsSelectedOption { get; set; }

        protected override Thickness ContentInset => new Thickness(8.0f, 4.0f);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var desired = base.MeasureOverride(availableSize);
            return new Vector2(desired.X, MathF.Max(desired.Y, Theme.ControlHeight - 2.0f));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;

            if (IsSelectedOption)
            {
                if (!drawList.DrawSkin(theme, "primary", Bounds, UiColor.White, lift: false))
                {
                    drawList.FillRect(Bounds, theme.Accent, new CornerRadius(3.0f));
                }
            }
            else if (IsPointerOver)
            {
                drawList.FillRect(Bounds, theme.Hover, new CornerRadius(3.0f));
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button == PointerButton.Left)
            {
                _owner.CommitSelection(_index);
                e.Handled = true;
            }
        }
    }
}
