namespace ManaUI
{
    /// <summary>
    /// A stack of dockable panels sharing one area, selected by a tab strip.
    /// <para>
    /// Like the splitters in <see cref="DockSplit"/>, tabs are drawn and hit-tested by this node rather
    /// than being child elements: they are chrome, and keeping them out of the child list keeps the docked
    /// content the only thing in the tree.
    /// </para>
    /// </summary>
    public class DockTabGroup : DockNode
    {
        private const float TabPaddingX = 12.0f;
        private const float TabCloseSize = 14.0f;

        /// <summary>Narrowest a tab shrinks to before the strip gives up and splits evenly instead.</summary>
        private const float MinimumTabWidth = 44.0f;

        private readonly List<DockItem> _items = new();
        private readonly ContentHost _contentHost = new();

        private int _selectedIndex = -1;
        private int _pressedTab = -1;

        public DockTabGroup()
        {
            Children.Add(_contentHost);
        }

        public DockTabGroup(params DockItem[] items)
            : this()
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        /// <summary>Raised when the last item is removed, so the host can collapse the empty node away.</summary>
        public event EventHandler? Emptied;

        /// <summary>
        /// Raised when the active tab changes. Which tab is showing is part of the layout, so the host
        /// treats this the same as a structural change and persists it.
        /// </summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Raised when a tab is dragged off the strip; the docking layer turns this into a float.</summary>
        public event EventHandler<DockTabDragEventArgs>? TabDragOut;

        public IReadOnlyList<DockItem> Items => _items;

        public override bool IsEmpty => _items.Count == 0;

        public DockItem? SelectedItem =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
                if (_selectedIndex == clamped)
                {
                    return;
                }

                _selectedIndex = clamped;
                _contentHost.Child = SelectedItem?.Content;
                InvalidateMeasure();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public float TabHeight { get; set; } = float.NaN;

        private float ResolvedTabHeight =>
            LayoutHelper.IsAuto(TabHeight) ? (Root?.Theme.ControlHeight ?? 26.0f) + 2.0f : TabHeight;

        public void Add(DockItem item)
        {
            Insert(_items.Count, item);
        }

        public void Insert(int index, DockItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            index = Math.Clamp(index, 0, _items.Count);
            _items.Insert(index, item);
            item.Owner = this;

            // Selecting the newly added item is what the user means by opening a panel.
            _selectedIndex = index;
            _contentHost.Child = item.Content;
            InvalidateMeasure();
        }

        public bool Remove(DockItem item)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            _items.RemoveAt(index);
            if (ReferenceEquals(item.Owner, this))
            {
                item.Owner = null;
            }

            // Detach before re-hosting: the content is about to be shown elsewhere, and an element cannot
            // have two parents.
            if (ReferenceEquals(_contentHost.Child, item.Content))
            {
                _contentHost.Child = null;
            }

            _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(index, 0, _items.Count - 1);
            _contentHost.Child = SelectedItem?.Content;
            InvalidateMeasure();

            if (_items.Count == 0)
            {
                Emptied?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var contentHeight = MathF.Max(0.0f, inner.Y - ResolvedTabHeight);

            _contentHost.Measure(new Vector2(inner.X, contentHeight));
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var tabHeight = ResolvedTabHeight;

            _contentHost.Arrange(new UiRect(
                content.X,
                content.Y + tabHeight,
                content.Width,
                MathF.Max(0.0f, content.Height - tabHeight)));

            return finalSize;
        }

        // ----- tab geometry ---------------------------------------------------------------------------

        /// <summary>Width a tab would like, before the strip has had its say.</summary>
        private float NaturalTabWidth(DockItem item)
        {
            var root = Root;
            if (root == null)
            {
                return 100.0f;
            }

            var font = root.Fonts.Get(root.Theme.FontFamily, root.Theme.FontSize, root.Scale);
            var width = font.MeasureWidth(item.Title) + TabPaddingX * 2.0f;

            if (item.CanClose)
            {
                width += TabCloseSize + 4.0f;
            }

            return MathF.Max(MinimumTabWidth, width);
        }

        /// <summary>
        /// Width a tab actually gets. Natural widths whenever they fit, and shrunk proportionally when they
        /// do not -- titles already trim with an ellipsis, so a narrow tab is still a readable one, whereas
        /// a tab laid out past the strip's edge is simply unreachable. That is what hid the Inspector tab
        /// once a sixth panel was added to a 400px dock.
        /// <para>
        /// Below <see cref="MinimumTabWidth"/> proportional shrinking stops being useful -- the longest
        /// title would collapse to nothing while a short one kept its space -- so a cramped strip splits
        /// evenly instead. Every tab stays clickable either way.
        /// </para>
        /// </summary>
        private float TabWidth(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return 0.0f;
            }

            var available = Bounds.Width;
            var natural = 0.0f;
            for (var i = 0; i < _items.Count; i++)
            {
                natural += NaturalTabWidth(_items[i]);
            }

            if (available <= 0.0f || natural <= available)
            {
                return NaturalTabWidth(_items[index]);
            }

            var scale = available / natural;
            if (NaturalTabWidth(_items[index]) * scale >= MinimumTabWidth)
            {
                return NaturalTabWidth(_items[index]) * scale;
            }

            return available / _items.Count;
        }

        /// <summary>Bounds of a tab in root coordinates.</summary>
        public UiRect TabRect(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return UiRect.Empty;
            }

            var x = Bounds.X;
            for (var i = 0; i < index; i++)
            {
                x += TabWidth(i);
            }

            return new UiRect(x, Bounds.Y, TabWidth(index), ResolvedTabHeight);
        }

        private UiRect TabCloseRect(int index)
        {
            var tab = TabRect(index);
            if (tab.IsEmpty)
            {
                return UiRect.Empty;
            }

            return new UiRect(
                tab.Right - TabCloseSize - 6.0f,
                tab.Center.Y - TabCloseSize * 0.5f,
                TabCloseSize,
                TabCloseSize);
        }

        private int TabAt(Vector2 point)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (TabRect(i).Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The strip area, including the empty space past the last tab.</summary>
        public UiRect TabStripRect => new UiRect(Bounds.X, Bounds.Y, Bounds.Width, ResolvedTabHeight);

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root;
            var theme = root?.Theme;
            if (theme == null || root == null)
            {
                return;
            }

            var strip = TabStripRect;
            drawList.FillRect(strip, theme.SurfaceAlt);
            drawList.FillRect(new UiRect(strip.X, strip.Bottom - 1.0f, strip.Width, 1.0f), theme.Border);

            drawList.FillRect(
                new UiRect(Bounds.X, strip.Bottom, Bounds.Width, MathF.Max(0.0f, Bounds.Height - strip.Height)),
                theme.Surface);

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var pointer = root.Input.PointerPosition;

            drawList.PushClip(strip);

            for (var i = 0; i < _items.Count; i++)
            {
                var tab = TabRect(i);
                var selected = i == _selectedIndex;
                var hovered = tab.Contains(pointer);

                if (selected)
                {
                    drawList.FillRect(tab, theme.Surface, CornerRadius.Top(4.0f));

                    // A short accent bar along the top reads as "this one" more clearly than fill alone.
                    drawList.FillRect(new UiRect(tab.X, tab.Y, tab.Width, 2.0f), theme.Accent,
                        CornerRadius.Top(2.0f));
                }
                else if (hovered)
                {
                    drawList.FillRect(tab, theme.Hover, CornerRadius.Top(4.0f));
                }

                var layout = new TextLayout
                {
                    Font = font,
                    Text = _items[i].Title,
                    Trimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = tab.Width - TabPaddingX * 2.0f - (_items[i].CanClose ? TabCloseSize : 0.0f)
                };

                layout.Render(
                    drawList,
                    new UiRect(tab.X + TabPaddingX, tab.Y, layout.MaxWidth, tab.Height),
                    selected ? theme.Text : theme.TextMuted,
                    VerticalAlignment.Center);

                if (_items[i].CanClose && (selected || hovered))
                {
                    RenderTabClose(drawList, theme, font, TabCloseRect(i), pointer);
                }
            }

            drawList.PopClip();
        }

        private static void RenderTabClose(
            ManaUiDrawList drawList,
            UiTheme theme,
            ManaFont font,
            UiRect rect,
            Vector2 pointer)
        {
            var hovered = rect.Contains(pointer);
            if (hovered)
            {
                drawList.FillRect(rect, theme.Danger, new CornerRadius(3.0f));
            }

            var glyph = font.GetGlyph('×');
            var color = hovered ? theme.TextOnAccent : theme.TextMuted;

            if (glyph.IsDrawable)
            {
                drawList.DrawGlyph(
                    new UiRect(
                        font.SnapToPixel(rect.Center.X - glyph.Size.X * 0.5f),
                        font.SnapToPixel(rect.Center.Y - glyph.Size.Y * 0.5f),
                        glyph.Size.X,
                        glyph.Size.Y),
                    glyph.AtlasUv,
                    color);
            }
            else
            {
                UiIcons.Cross(drawList, rect.Deflate(new Thickness(4.0f)), color);
            }
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            var index = TabAt(e.Position);
            if (index < 0)
            {
                return;
            }

            if (_items[index].CanClose && TabCloseRect(index).Contains(e.Position))
            {
                Remove(_items[index]);
                e.Handled = true;
                return;
            }

            SelectedIndex = index;
            _pressedTab = index;
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_pressedTab < 0)
            {
                return;
            }

            // Leaving the strip vertically means the user is pulling the panel out, not reordering it.
            // Reporting it rather than acting keeps this node unaware of floating windows.
            if (MathF.Abs(e.Position.Y - TabStripRect.Center.Y) > ResolvedTabHeight)
            {
                var item = _items[Math.Clamp(_pressedTab, 0, _items.Count - 1)];
                _pressedTab = -1;
                ReleasePointerCapture();
                TabDragOut?.Invoke(this, new DockTabDragEventArgs(item, e.Position));
                e.Handled = true;
                return;
            }

            var target = TabAt(e.Position);
            if (target >= 0 && target != _pressedTab)
            {
                Reorder(_pressedTab, target);
                _pressedTab = target;
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_pressedTab >= 0)
            {
                _pressedTab = -1;
                ReleasePointerCapture();
                e.Handled = true;
            }
        }

        private void Reorder(int from, int to)
        {
            if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count)
            {
                return;
            }

            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
            _selectedIndex = to;
            InvalidateVisual();
        }

        protected override bool IsSelfHitTestVisible => true;
    }

    public sealed class DockTabDragEventArgs : EventArgs
    {
        public DockTabDragEventArgs(DockItem item, Vector2 position)
        {
            Item = item;
            Position = position;
        }

        public DockItem Item { get; }

        /// <summary>Pointer position, in root coordinates, when the tab left the strip.</summary>
        public Vector2 Position { get; }
    }
}
