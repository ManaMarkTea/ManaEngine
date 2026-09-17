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

        /// <summary>Narrowest a tab is allowed to get before the strip wraps onto another row instead.</summary>
        private const float MinimumTabWidth = 88.0f;

        /// <summary>
        /// Width of the overflow button at the end of the strip. Enough for a chevron and a count.
        /// </summary>
        private const float OverflowButtonWidth = 34.0f;

        private readonly List<DockItem> _items = new();
        private readonly ContentHost _contentHost = new();

        /// <summary>Tab rectangles relative to the strip's top-left, so moving the dock does not rebuild them.</summary>
        private readonly List<UiRect> _tabRects = new();

        private readonly List<float> _naturalWidths = new();

        /// <summary>Item indices with a visible tab, in the left-to-right order they are drawn.</summary>
        private readonly List<int> _visibleOrder = new();

        /// <summary>Overflow button, relative to the strip's top-left. Empty when everything fits.</summary>
        private UiRect _overflowRect = UiRect.Empty;

        private int _selectedIndex = -1;
        private int _pressedTab = -1;

        private float _layoutWidth = float.NaN;
        private int _layoutCount = -1;

        /// <summary>
        /// Selection the cached tab layout was built for. Part of the cache key because the selected tab is
        /// guaranteed a visible slot -- so which tabs are visible depends on it, and a selection change with a
        /// stale layout would leave the newly selected tab drawn nowhere and the old one still occupying its
        /// place.
        /// </summary>
        private int _layoutSelected = -1;
        private int _rowCount = 1;

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

        /// <summary>
        /// Collapses the tab strip to nothing while this group holds exactly one item -- a toolbar-shaped
        /// panel that is always alone in its own dock host does not need a title bar naming the one thing
        /// it can possibly be. Re-evaluated every layout pass, so the strip returns the moment a second
        /// item is tabbed in.
        /// </summary>
        public bool HideStripWhenSingle { get; set; }

        /// <summary>Height of one row of tabs. The strip is a whole number of these.</summary>
        private float TabRowHeight =>
            LayoutHelper.IsAuto(TabHeight) ? (Root?.Theme.ControlHeight ?? 26.0f) + 2.0f : TabHeight;

        /// <summary>Rows the strip is currently using. One, until the tabs stop fitting.</summary>
        public int RowCount
        {
            get
            {
                EnsureTabLayout(Bounds.Width);
                return _rowCount;
            }
        }

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
            InvalidateTabLayout();
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
            InvalidateTabLayout();
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
            var contentHeight = MathF.Max(0.0f, inner.Y - StripHeightFor(inner.X));

            _contentHost.Measure(new Vector2(inner.X, contentHeight));
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;

            // Arrange knows the real width; measure only had a budget. Laying out here is what settles how
            // many rows the strip takes, and so how much is left for the panel.
            var tabHeight = StripHeightFor(content.Width);

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
        /// Lays the tabs out on a single row, with whatever does not fit moved behind an overflow button.
        /// <para>
        /// Two earlier answers to "eleven panels, one narrow dock" are worth knowing about, because this is
        /// the third. Shrinking every tab to fit kept them all clickable but left each about 36px wide -- an
        /// ellipsis and nothing else. Wrapping onto up to three rows made them readable, but spent 78px of
        /// the dock's height on labels, and a docked panel has no height to spare either: that showed up as
        /// the editor feeling cramped next to Unreal, which never spends more than one row on a tab strip.
        /// </para>
        /// <para>
        /// So: full-width tabs for as many as fit, and the rest one click away. Height is fixed at one row no
        /// matter how many panels are tabbed together, which is the property the other two lacked.
        /// </para>
        /// <para>
        /// The selected tab is always among the visible ones -- it takes the last visible slot if it would
        /// otherwise be hidden. A strip that does not show which panel you are looking at is worse than a
        /// narrow one.
        /// </para>
        /// </summary>
        private void EnsureTabLayout(float width)
        {
            width = MathF.Max(width, 1.0f);

            if (_layoutCount == _items.Count
                && _layoutSelected == _selectedIndex
                && MathF.Abs(_layoutWidth - width) < 0.5f)
            {
                return;
            }

            _layoutWidth = width;
            _layoutCount = _items.Count;
            _layoutSelected = _selectedIndex;
            _naturalWidths.Clear();
            _visibleOrder.Clear();
            _overflowRect = UiRect.Empty;
            _rowCount = 1;

            // Index-aligned with _items and pre-filled empty, so an overflowed tab simply has no rectangle.
            // Painting and hit testing both go through TabRect, and neither draws nor hits an empty one --
            // which is why hiding a tab needs no special case anywhere else in this class.
            _tabRects.Clear();
            for (var i = 0; i < _items.Count; i++)
            {
                _tabRects.Add(UiRect.Empty);
            }

            if (_items.Count == 0)
            {
                return;
            }

            var total = 0.0f;
            for (var i = 0; i < _items.Count; i++)
            {
                var natural = NaturalTabWidth(_items[i]);
                _naturalWidths.Add(natural);
                total += natural;
            }

            var rowHeight = TabRowHeight;

            // Everything fits: natural widths, left aligned, exactly as a single-row strip has always looked.
            if (total <= width)
            {
                var x = 0.0f;
                for (var i = 0; i < _items.Count; i++)
                {
                    _tabRects[i] = new UiRect(x, 0.0f, _naturalWidths[i], rowHeight);
                    _visibleOrder.Add(i);
                    x += _naturalWidths[i];
                }

                return;
            }

            // Room for the button has to come out of the tabs' share, or the last tab would sit under it.
            var tabsWidth = MathF.Max(width - OverflowButtonWidth, MinimumTabWidth);
            var visibleCount = CountThatFit(tabsWidth);

            for (var i = 0; i < visibleCount; i++)
            {
                _visibleOrder.Add(i);
            }

            // The selected tab takes the last visible slot rather than being appended, so the count -- and
            // therefore the strip's width budget -- is unchanged by the substitution.
            if (_selectedIndex >= visibleCount && _visibleOrder.Count > 0)
            {
                _visibleOrder[^1] = _selectedIndex;
            }

            PlaceVisible(tabsWidth, rowHeight);
            _overflowRect = new UiRect(tabsWidth, 0.0f, MathF.Max(width - tabsWidth, 0.0f), rowHeight);
        }

        /// <summary>How many tabs from the start fit in <paramref name="width"/> at natural size. At least one.</summary>
        private int CountThatFit(float width)
        {
            var taken = 0;
            var used = 0.0f;

            while (taken < _items.Count)
            {
                var next = _naturalWidths[taken];
                if (taken > 0 && used + next > width)
                {
                    break;
                }

                used += next;
                taken++;
            }

            return Math.Max(taken, 1);
        }

        /// <summary>
        /// Places the visible tabs, stretched to span the tab area exactly. Ragged ends read as a broken
        /// layout rather than a deliberate one.
        /// </summary>
        private void PlaceVisible(float width, float rowHeight)
        {
            var natural = 0.0f;
            foreach (var index in _visibleOrder)
            {
                natural += _naturalWidths[index];
            }

            var scale = natural > 0.0f ? width / natural : 1.0f;
            var x = 0.0f;

            for (var slot = 0; slot < _visibleOrder.Count; slot++)
            {
                var index = _visibleOrder[slot];

                // The last one is snapped to the edge, so rounding cannot leave a seam.
                var tabWidth = slot == _visibleOrder.Count - 1 ? width - x : _naturalWidths[index] * scale;
                _tabRects[index] = new UiRect(x, 0.0f, MathF.Max(tabWidth, 0.0f), rowHeight);
                x += tabWidth;
            }
        }

        /// <summary>Item indices with no visible tab, in item order. Empty unless the strip overflowed.</summary>
        public IEnumerable<int> OverflowedIndices()
        {
            EnsureTabLayout(Bounds.Width);

            for (var i = 0; i < _items.Count; i++)
            {
                if (!_visibleOrder.Contains(i))
                {
                    yield return i;
                }
            }
        }

        /// <summary>Overflow button in root coordinates, or empty when every tab is showing.</summary>
        public UiRect OverflowRect
        {
            get
            {
                EnsureTabLayout(Bounds.Width);
                return _overflowRect.IsEmpty
                    ? UiRect.Empty
                    : new UiRect(
                        Bounds.X + _overflowRect.X,
                        Bounds.Y + _overflowRect.Y,
                        _overflowRect.Width,
                        _overflowRect.Height);
            }
        }

        private void ShowOverflowMenu()
        {
            var root = Root;
            var rect = OverflowRect;
            if (root == null || rect.IsEmpty)
            {
                return;
            }

            var items = new List<MenuItem>();
            foreach (var index in OverflowedIndices())
            {
                var target = index;
                items.Add(new MenuItem(_items[target].Title, () => SelectedIndex = target));
            }

            if (items.Count == 0)
            {
                return;
            }

            MenuFlyout.ShowAt(root, new Vector2(rect.X, rect.Bottom), items);
        }

        private void InvalidateTabLayout()
        {
            _layoutCount = -1;
        }

        /// <summary>Bounds of a tab in root coordinates.</summary>
        public UiRect TabRect(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return UiRect.Empty;
            }

            EnsureTabLayout(Bounds.Width);

            if (index >= _tabRects.Count)
            {
                return UiRect.Empty;
            }

            var rect = _tabRects[index];
            return new UiRect(Bounds.X + rect.X, Bounds.Y + rect.Y, rect.Width, rect.Height);
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
        public UiRect TabStripRect =>
            new UiRect(Bounds.X, Bounds.Y, Bounds.Width, StripHeightFor(Bounds.Width));

        /// <summary>Total strip height at a given width -- one row, or however many the tabs wrapped onto.</summary>
        private float StripHeightFor(float width)
        {
            EnsureTabLayout(width);

            if (HideStripWhenSingle && _items.Count <= 1)
            {
                return 0.0f;
            }

            return _rowCount * TabRowHeight;
        }

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
            var content = new UiRect(
                Bounds.X, strip.Bottom, Bounds.Width, MathF.Max(0.0f, Bounds.Height - strip.Height));

            // Backdrop-blurred when asked for, the same as the columns themselves -- otherwise a panel's own
            // tab strip and content area paint over the column's blur with a flat, opaque fill, and the
            // "blur everything" option only ever shows in the sliver of column not covered by a panel.
            if (theme.BackdropBlur > 0.0f)
            {
                drawList.FillBackdrop(strip, theme.Frosted(theme.SurfaceAlt), theme.BackdropBlur, default);
                drawList.FillBackdrop(content, theme.Frosted(theme.Surface), theme.BackdropBlur, default);
            }
            else
            {
                drawList.FillRect(strip, theme.SurfaceAlt);
                drawList.FillRect(content, theme.Surface);
            }

            drawList.FillRect(new UiRect(strip.X, strip.Bottom - 1.0f, strip.Width, 1.0f), theme.Border);

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var pointer = root.Input.PointerPosition;

            drawList.PushClip(strip);

            // Theme-driven rather than a fixed 4px: LCARS's CornerRadius is a near-capsule (half the tab's
            // own height), which is what makes a tab strip actually read as LCARS rather than merely
            // recolouring an otherwise ordinary rounded-rectangle tab.
            var tabRadius = CornerRadius.Top(theme.CornerRadius.TopLeft);
            var accentRadius = CornerRadius.Top(theme.CornerRadius.TopLeft * 0.5f);

            for (var i = 0; i < _items.Count; i++)
            {
                var tab = TabRect(i);

                // An overflowed tab has no rectangle, and TabRect reports that as zero-sized at the
                // strip's own origin rather than at 0,0. Hit testing is safe either way -- nothing is
                // inside a zero-sized rect -- but drawing was not: a zero-width tab gives the text layout
                // a negative width budget, which counts as overflowing, so every hidden tab painted a
                // trimming ellipsis on top of the next one in the strip's top-left corner. Three hidden
                // panels drew three stacked ellipses, which is the speck that appeared above the first tab.
                if (tab.Width <= 0.0f || tab.Height <= 0.0f)
                {
                    continue;
                }

                var selected = i == _selectedIndex;
                var hovered = tab.Contains(pointer);

                if (selected)
                {
                    drawList.FillRect(tab, theme.Surface, tabRadius);

                    // A short accent bar along the top reads as "this one" more clearly than fill alone.
                    drawList.FillRect(new UiRect(tab.X, tab.Y, tab.Width, 2.0f), theme.Accent, accentRadius);
                }
                else if (hovered)
                {
                    drawList.FillRect(tab, theme.Hover, tabRadius);
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

            RenderOverflowButton(drawList, theme, font, pointer);

            drawList.PopClip();
        }

        /// <summary>
        /// The "and N more" affordance. Draws a chevron and the hidden count, because a chevron alone says
        /// there is more without saying how much -- and with eleven panels in a dock, how much is the useful
        /// part. Nothing is drawn when every tab fits.
        /// </summary>
        private void RenderOverflowButton(
            ManaUiDrawList drawList,
            UiTheme theme,
            ManaFont font,
            Vector2 pointer)
        {
            if (_overflowRect.IsEmpty)
            {
                return;
            }

            var rect = OverflowRect;
            var hidden = _items.Count - _visibleOrder.Count;
            if (hidden <= 0)
            {
                return;
            }

            var hovered = rect.Contains(pointer);
            if (hovered)
            {
                drawList.FillRect(rect, theme.SurfaceRaised, CornerRadius.Top(4.0f));
            }

            var layout = new TextLayout
            {
                Font = font,
                Text = $"»{hidden}",
                Alignment = TextAlignment.Center,
                MaxWidth = rect.Width
            };

            layout.Render(
                drawList,
                rect,
                hovered ? theme.Text : theme.TextMuted,
                VerticalAlignment.Center);
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

            // Before the tab hit test: the button sits inside the strip, and a press there is not a press on
            // whichever tab happens to be nearest.
            if (OverflowRect.Contains(e.Position))
            {
                ShowOverflowMenu();
                e.Handled = true;
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
            var strip = TabStripRect;
            if (e.Position.Y < strip.Y - TabRowHeight || e.Position.Y > strip.Bottom + TabRowHeight)
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
