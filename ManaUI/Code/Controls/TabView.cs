namespace ManaUI
{
    /// <summary>One tab: a header and the content it shows.</summary>
    public sealed class TabItem
    {
        public TabItem(string header, UiElement content)
        {
            Header = header;
            Content = content;
        }

        public string Header { get; set; }

        public UiElement Content { get; }

        /// <summary>Shown after the header -- a count, a dot for unsaved work.</summary>
        public string? Badge { get; set; }

        public string? Tooltip { get; set; }

        /// <summary>Live enablement, for a tab that only applies in some states.</summary>
        public Func<bool>? IsEnabledProvider { get; set; }

        public bool ResolveEnabled() => IsEnabledProvider?.Invoke() ?? true;
    }

    /// <summary>
    /// A strip of tab headers over a single content area.
    /// <para>
    /// Every tab's content is built once and kept, with the unselected ones collapsed rather than
    /// destroyed. That is the opposite of what an immediate-mode tab bar does, and it is deliberate: a
    /// half-typed field or a scroll position in a tab you flick away from and back should still be there.
    /// The cost is that all tabs exist at once, which for a handful of editor panels is nothing.
    /// </para>
    /// </summary>
    public sealed class TabView : Panel
    {
        private readonly List<TabItem> _tabs = new();
        private readonly StackPanel _strip;
        private readonly Grid _body;

        private int _selectedIndex;

        public TabView()
        {
            _strip = new StackPanel().Horizontal(2.0f);
            _body = new Grid().Rows("*").Columns("*");

            Children.Add(_strip);
            Children.Add(_body);
        }

        public IReadOnlyList<TabItem> Tabs => _tabs;

        public event EventHandler<int>? SelectionChanged;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var clamped = _tabs.Count == 0 ? 0 : Math.Clamp(value, 0, _tabs.Count - 1);
                if (clamped == _selectedIndex)
                {
                    return;
                }

                _selectedIndex = clamped;
                SyncSelection();
                SelectionChanged?.Invoke(this, _selectedIndex);
            }
        }

        public TabItem? SelectedTab => _selectedIndex >= 0 && _selectedIndex < _tabs.Count ? _tabs[_selectedIndex] : null;

        public TabView Add(TabItem tab)
        {
            ArgumentNullException.ThrowIfNull(tab);

            var index = _tabs.Count;
            _tabs.Add(tab);

            _strip.Children.Add(new TabHeader(this, index));
            _body.Children.Add(tab.Content);

            SyncSelection();
            return this;
        }

        public TabView Add(string header, UiElement content) => Add(new TabItem(header, content));

        public TabView AddRange(params TabItem[] tabs)
        {
            foreach (var tab in tabs)
            {
                Add(tab);
            }

            return this;
        }

        /// <summary>Selects by header, for restoring which tab was open last session.</summary>
        public bool SelectByHeader(string header)
        {
            var index = _tabs.FindIndex(t => string.Equals(t.Header, header, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            SelectedIndex = index;
            return true;
        }

        private void SyncSelection()
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Content.Visibility = i == _selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            }

            InvalidateVisual();
        }

        internal void SelectFromHeader(int index)
        {
            SelectedIndex = index;
        }

        internal bool IsSelected(int index) => index == _selectedIndex;

        internal TabItem TabAt(int index) => _tabs[index];

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _strip.Measure(availableSize);

            var remaining = new Vector2(
                availableSize.X,
                LayoutHelper.Finite(availableSize).Y - _strip.DesiredSize.Y);

            _body.Measure(new Vector2(availableSize.X, MathF.Max(0.0f, remaining.Y)));

            return new Vector2(
                MathF.Max(_strip.DesiredSize.X, _body.DesiredSize.X),
                _strip.DesiredSize.Y + _body.DesiredSize.Y);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var stripHeight = _strip.DesiredSize.Y;

            _strip.Arrange(new UiRect(content.X, content.Y, content.Width, stripHeight));
            _body.Arrange(new UiRect(
                content.X,
                content.Y + stripHeight,
                content.Width,
                MathF.Max(0.0f, content.Height - stripHeight)));

            return finalSize;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var stripHeight = _strip.Bounds.Height;

            // A line under the strip, so the selected tab reads as attached to the content below it.
            drawList.FillRect(
                new UiRect(Bounds.X, Bounds.Y + stripHeight - 1.0f, Bounds.Width, 1.0f),
                theme.Border);
        }
    }

    /// <summary>One header in the strip. Draws itself rather than composing a button and a label.</summary>
    internal sealed class TabHeader : Control
    {
        private readonly TabView _owner;
        private readonly int _index;

        public TabHeader(TabView owner, int index)
        {
            _owner = owner;
            _index = index;
            Focusable = false;
        }

        private TabItem Item => _owner.TabAt(_index);

        protected override bool IsSelfHitTestVisible => true;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var theme = Theme;
            var font = (Root ?? throw new InvalidOperationException("TabHeader measured without a root."))
                .Fonts.Get(theme.FontFamily, theme.FontSize, Root.Scale);

            var width = font.MeasureWidth(Label()) + 20.0f;
            return new Vector2(width, theme.ControlHeight);
        }

        private string Label()
        {
            var item = Item;
            return string.IsNullOrEmpty(item.Badge) ? item.Header : $"{item.Header}  {item.Badge}";
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var item = Item;
            var selected = _owner.IsSelected(_index);
            var enabled = item.ResolveEnabled();

            Tooltip = item.Tooltip;

            var fill = selected ? theme.SurfaceRaised
                : IsPointerOver && enabled ? theme.SurfaceAlt
                : UiColor.Transparent;

            var corners = new CornerRadius(theme.CornerRadius.TopLeft, theme.CornerRadius.TopLeft, 0.0f, 0.0f);

            if (selected && drawList.DrawSkin(theme, "button", Bounds, fill))
            {
                // Dressed in the skin's raised button instead.
            }
            else if (selected && theme.SurfaceGradient > 0.0f)
            {
                drawList.FillRect(
                    Bounds,
                    theme.GradientTop(fill, theme.SurfaceGradient),
                    theme.GradientBottom(fill, theme.SurfaceGradient),
                    corners);
            }
            else
            {
                drawList.FillRect(Bounds, fill, corners);
            }

            if (selected)
            {
                // The accent sits on top rather than underneath, which is what makes a strip of tabs read
                // as one selected and the rest behind it.
                drawList.FillRect(new UiRect(Bounds.X, Bounds.Y, Bounds.Width, 2.0f), theme.Accent);
            }

            var foreground = !enabled ? theme.TextDisabled
                : selected ? theme.Text
                : theme.TextMuted;

            var font = Root!.Fonts.Get(theme.FontFamily, theme.FontSize, Root.Scale);
            new TextLayout { Font = font, Text = Label(), Alignment = TextAlignment.Center }
                .Render(drawList, Bounds, foreground, VerticalAlignment.Center);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !Item.ResolveEnabled())
            {
                return;
            }

            _owner.SelectFromHeader(_index);
            e.Handled = true;
        }
    }
}
