using System.Collections;

namespace ManaUI
{
    /// <summary>
    /// A hierarchical list with expandable rows.
    /// <para>
    /// Items are plain objects with a children selector rather than a dedicated node type, so a tree can be
    /// projected straight over an existing model -- a scene outliner mixes scenes, nodes and components in
    /// one tree and should not have to wrap any of them.
    /// </para>
    /// <para>
    /// Only expanded rows are built. Collapsing a large subtree removes its rows entirely rather than
    /// hiding them, and what remains is culled by the enclosing ScrollViewer, so a deep hierarchy costs
    /// what is on screen.
    /// </para>
    /// </summary>
    public class TreeView : Panel
    {
        // Reference identity: model objects may override Equals, and two equal-but-distinct nodes must
        // still expand independently.
        private readonly HashSet<object> _expanded = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Nodes closed by hand. Only meaningful against <see cref="IsExpandedByDefault"/> -- without it an
        /// absence from <see cref="_expanded"/> already means closed, and this stays empty.
        /// </summary>
        private readonly HashSet<object> _collapsed = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _visibleItems = new();
        private readonly List<int> _depths = new();
        private readonly List<bool> _hasChildren = new();

        private ScrollViewer? _scroller;
        private int _realizedFirst = -1;
        private int _realizedCount;

        private IEnumerable? _itemsSource;
        private object? _selectedItem;
        private Func<int>? _structureSignature;
        private int _structureRevision;
        private bool _signatureBound;
        private bool _rebuildQueued = true;

        public TreeView()
        {
            Focusable = true;
        }

        public event EventHandler<object?>? SelectionChanged;

        /// <summary>Raised on double-click or Enter -- "open this", distinct from merely selecting it.</summary>
        public event EventHandler<object>? ItemActivated;

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                _itemsSource = value;
                QueueRebuild();
            }
        }

        /// <summary>Returns an item's children, or null for a leaf.</summary>
        public Func<object, IEnumerable?>? ChildrenSelector { get; set; }

        /// <summary>
        /// Optional cheap "does this have children" test. Without it the tree has to ask for the children
        /// of every visible item just to decide whether to draw an expander, which for a model that
        /// materialises a collection per call means an allocation per row per rebuild.
        /// </summary>
        public Func<object, bool>? HasChildrenSelector { get; set; }

        /// <summary>Uniform row height. NaN follows the theme's control height.</summary>
        public float RowHeight { get; set; } = float.NaN;

        /// <summary>
        /// Whether to build only the rows inside the scroll viewport. On by default; turning it off is
        /// only useful for a tree small enough that the bookkeeping is not worth it.
        /// </summary>
        public bool IsVirtualizing { get; set; } = true;

        private float ResolvedRowHeight => LayoutHelper.IsAuto(RowHeight)
            ? MathF.Max(1.0f, (Root?.Theme.ControlHeight ?? 26.0f) - 4.0f)
            : RowHeight;

        /// <summary>Row elements that currently exist, which is a window onto <see cref="VisibleItems"/>.</summary>
        public int RealizedRowCount => Children.Count;

        /// <summary>Builds the content of a row. The expander and indent are drawn around it.</summary>
        public Func<object, UiElement>? ItemTemplate { get; set; }

        /// <summary>
        /// Optional cheap signature polled each frame; the tree rebuilds when it changes. The escape hatch
        /// for models with no change notification -- a scene graph does not announce new nodes, and
        /// something like a node count is far cheaper than diffing the tree every frame.
        /// </summary>
        public Func<int>? StructureSignature
        {
            get => _structureSignature;
            set
            {
                _structureSignature = value;

                // Routed through a polled binding rather than checked during layout. The root refreshes
                // polled bindings *before* deciding whether layout is dirty, so this can trigger a rebuild
                // from a standing start; a check inside MeasureOverride only ran when something else had
                // already dirtied layout, which is never the case for a model that changed silently.
                if (value != null && !_signatureBound)
                {
                    _signatureBound = true;
                    this.BindPolled(t => t.StructureRevision, () => _structureSignature?.Invoke() ?? 0);
                }
            }
        }

        /// <summary>The last observed <see cref="StructureSignature"/>. Assigning a new value rebuilds.</summary>
        public int StructureRevision
        {
            get => _structureRevision;
            set
            {
                if (_structureRevision == value)
                {
                    return;
                }

                _structureRevision = value;
                QueueRebuild();
            }
        }

        public float IndentSize { get; set; } = 14.0f;

        public object? SelectedItem
        {
            get => _selectedItem;
            set => SetSelectedItem(value, notify: true);
        }

        private void SetSelectedItem(object? value, bool notify)
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            InvalidateVisual();

            if (notify)
            {
                SelectionChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Selection target for external model selection. Setting this mirrors <see cref="SelectedItem"/>
        /// and expands the ancestor chain so the selected row is actually visible in the tree.
        /// </summary>
        public object? RevealedSelectedItem
        {
            get => SelectedItem;
            set
            {
                SetSelectedItem(value, notify: false);

                if (value != null)
                {
                    ExpandTo(value);
                }
            }
        }

        /// <summary>Items currently shown, in display order.</summary>
        public IReadOnlyList<object> VisibleItems => _visibleItems;

        /// <summary>
        /// Which nodes start open. A file tree wants its folders open on arrival -- a panel that opens showing
        /// nothing but folder names has hidden everything it exists to show -- while a scene graph wants the
        /// opposite. Closing one by hand still sticks.
        /// </summary>
        public Func<object, bool>? IsExpandedByDefault { get; set; }

        public bool IsExpanded(object item)
        {
            if (_collapsed.Contains(item))
            {
                return false;
            }

            return _expanded.Contains(item) || IsExpandedByDefault?.Invoke(item) == true;
        }

        public void Expand(object item)
        {
            var changed = _collapsed.Remove(item);
            changed |= _expanded.Add(item);

            if (changed)
            {
                QueueRebuild();
            }
        }

        public void Collapse(object item)
        {
            var changed = _expanded.Remove(item);
            changed |= _collapsed.Add(item);

            if (changed)
            {
                QueueRebuild();
            }
        }

        public void ToggleExpanded(object item)
        {
            if (IsExpanded(item))
            {
                Collapse(item);
                return;
            }

            Expand(item);
        }

        /// <summary>Expands every ancestor of an item so it becomes visible.</summary>
        public void ExpandTo(object item)
        {
            if (_itemsSource == null || ChildrenSelector == null)
            {
                return;
            }

            var path = new List<object>();
            if (FindPath(_itemsSource, item, path))
            {
                // The item itself is the last entry and does not need expanding to be seen.
                for (var i = 0; i < path.Count - 1; i++)
                {
                    _collapsed.Remove(path[i]);
                    _expanded.Add(path[i]);
                }

                QueueRebuild();
            }
        }

        private bool FindPath(IEnumerable items, object target, List<object> path)
        {
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                path.Add(item);

                if (ReferenceEquals(item, target))
                {
                    return true;
                }

                var children = ChildrenSelector?.Invoke(item);
                if (children != null && FindPath(children, target, path))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        public void CollapseAll()
        {
            _expanded.Clear();

            // Default-expanded nodes have to be closed explicitly, or clearing the set leaves them exactly
            // as open as they were. Only what is visible can be reached, which is also all that is on screen
            // to have looked open in the first place.
            if (IsExpandedByDefault != null)
            {
                foreach (var item in _visibleItems)
                {
                    _collapsed.Add(item);
                }
            }

            QueueRebuild();
        }

        /// <summary>Forces the row list to be rebuilt on the next layout.</summary>
        public void QueueRebuild()
        {
            _rebuildQueued = true;
            InvalidateMeasure();
        }

        // ----- row construction -----------------------------------------------------------------------

        /// <summary>
        /// Walks the model into flat parallel lists. Builds no elements: a scene graph can expand to
        /// thousands of rows, and materialising all of them was what made expanding a large node drop the
        /// frame rate -- especially since a model that changes every frame forces this to run every frame.
        /// </summary>
        private void Flatten()
        {
            _rebuildQueued = false;

            _visibleItems.Clear();
            _depths.Clear();
            _hasChildren.Clear();

            if (_itemsSource == null || ItemTemplate == null)
            {
                return;
            }

            AddLevel(_itemsSource, 0);

            // Whatever was realised describes the old list, so it cannot be reused as-is.
            _realizedFirst = -1;
        }

        private void AddLevel(IEnumerable items, int depth)
        {
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var expanded = IsExpanded(item);

                // The children of a collapsed node are never needed, so asking for them is skipped
                // entirely unless HasChildrenSelector is absent and the expander state has to be derived.
                IEnumerable? children = null;
                bool hasChildren;

                if (HasChildrenSelector != null)
                {
                    hasChildren = HasChildrenSelector(item);
                    if (hasChildren && expanded)
                    {
                        children = ChildrenSelector?.Invoke(item);
                    }
                }
                else
                {
                    children = ChildrenSelector?.Invoke(item);
                    hasChildren = children != null && AnyItem(children);
                }

                _visibleItems.Add(item);
                _depths.Add(depth);
                _hasChildren.Add(hasChildren);

                if (hasChildren && expanded && children != null)
                {
                    AddLevel(children, depth + 1);
                }
            }
        }

        private static bool AnyItem(IEnumerable items)
        {
            foreach (var _ in items)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds row elements only for the slice inside the scroll viewport, plus a little overscan.
        /// <para>
        /// Rows are a uniform height so an index maps to a position arithmetically, which is what lets a
        /// tree of any size cost the same as one screenful.
        /// </para>
        /// </summary>
        private void RealizeRows(float rowHeight)
        {
            var (first, count) = VisibleRange(rowHeight);

            if (first == _realizedFirst && count == _realizedCount && Children.Count == count)
            {
                return;
            }

            _realizedFirst = first;
            _realizedCount = count;

            Children.Clear();

            for (var i = first; i < first + count; i++)
            {
                Children.Add(new TreeViewRow(this, _visibleItems[i], _depths[i], _hasChildren[i])
                {
                    Child = ItemTemplate!(_visibleItems[i])
                });
            }
        }

        /// <summary>The index range to realise, from the enclosing scroll viewport.</summary>
        private (int First, int Count) VisibleRange(float rowHeight)
        {
            var total = _visibleItems.Count;
            if (total == 0 || rowHeight <= 0.0f)
            {
                return (0, 0);
            }

            var scroller = FindScrollViewer();
            if (scroller == null || !IsVirtualizing)
            {
                return (0, total);
            }

            // A few rows either side, so a scroll of less than one row does not have to rebuild.
            const int Overscan = 3;

            var viewportHeight = scroller.Viewport.Y;
            if (viewportHeight <= 0.0f)
            {
                return (0, total);
            }

            var visible = (int)MathF.Ceiling(viewportHeight / rowHeight) + Overscan * 2;
            // The offset can still be the old one after the tree shrinks; keep the window on the last rows.
            var first = Math.Clamp((int)(scroller.Offset.Y / rowHeight) - Overscan, 0, Math.Max(0, total - visible));

            return (first, Math.Clamp(visible, 0, total - first));
        }

        private ScrollViewer? FindScrollViewer()
        {
            if (_scroller != null)
            {
                return _scroller;
            }

            for (var current = Parent; current != null; current = current.Parent)
            {
                if (current is ScrollViewer scroller)
                {
                    _scroller = scroller;

                    // Scrolling changes which rows exist, not just where they sit, so it has to re-measure.
                    scroller.ScrollChanged += (_, _) => InvalidateMeasure();
                    return scroller;
                }
            }

            return null;
        }

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_rebuildQueued)
            {
                Flatten();
            }

            var rowHeight = ResolvedRowHeight;
            RealizeRows(rowHeight);

            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var width = 0.0f;

            foreach (var child in Children)
            {
                child.Measure(new Vector2(inner.X, rowHeight));
                width = MathF.Max(width, child.DesiredSize.X);
            }

            // Height comes from the row count, not the realised rows: the scroll viewer has to see the
            // full extent even though only a screenful exists.
            return LayoutHelper.Inflate(new Vector2(width, _visibleItems.Count * rowHeight), Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var rowHeight = ResolvedRowHeight;

            for (var i = 0; i < Children.Count; i++)
            {
                // Positioned by absolute row index, since the realised rows are a window into the middle
                // of the list rather than the start of it.
                var y = content.Y + (_realizedFirst + i) * rowHeight;
                Children[i].Arrange(new UiRect(content.X, y, content.Width, rowHeight));
            }

            return finalSize;
        }

        // ----- keyboard -------------------------------------------------------------------------------

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            if (_visibleItems.Count == 0)
            {
                return;
            }

            var index = _selectedItem == null ? -1 : IndexOfVisible(_selectedItem);

            switch (e.Key)
            {
                case UiKey.Down:
                    SelectedItem = _visibleItems[Math.Clamp(index + 1, 0, _visibleItems.Count - 1)];
                    e.Handled = true;
                    break;
                case UiKey.Up:
                    SelectedItem = _visibleItems[Math.Clamp(index - 1, 0, _visibleItems.Count - 1)];
                    e.Handled = true;
                    break;
                case UiKey.Right when index >= 0:
                    // Right expands, then steps into the first child -- the standard two-stage behaviour.
                    if (!IsExpanded(_visibleItems[index]))
                    {
                        Expand(_visibleItems[index]);
                    }
                    else if (index + 1 < _visibleItems.Count)
                    {
                        SelectedItem = _visibleItems[index + 1];
                    }

                    e.Handled = true;
                    break;
                case UiKey.Left when index >= 0:
                    if (IsExpanded(_visibleItems[index]))
                    {
                        Collapse(_visibleItems[index]);
                    }
                    else
                    {
                        SelectParent(index);
                    }

                    e.Handled = true;
                    break;
                case UiKey.Enter when _selectedItem != null:
                    ItemActivated?.Invoke(this, _selectedItem);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Walks back up the visible rows to the nearest shallower one, which is the parent.</summary>
        private void SelectParent(int index)
        {
            if (Children[index] is not TreeViewRow row || row.Depth == 0)
            {
                return;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                if (Children[i] is TreeViewRow candidate && candidate.Depth < row.Depth)
                {
                    SelectedItem = _visibleItems[i];
                    return;
                }
            }
        }

        private int IndexOfVisible(object item)
        {
            for (var i = 0; i < _visibleItems.Count; i++)
            {
                if (ReferenceEquals(_visibleItems[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        internal void NotifyRowActivated(object item)
        {
            ItemActivated?.Invoke(this, item);
        }

        /// <summary>
        /// A row was pressed and selected (not its expander). The row handles the press itself, so its content never
        /// sees it: this is where content starts a drag of the row -- capture the pointer from here.
        /// </summary>
        public event EventHandler<UiPointerEventArgs>? RowPressed;

        internal void NotifyRowPressed(UiPointerEventArgs e)
        {
            RowPressed?.Invoke(this, e);
        }
    }

    /// <summary>One row: indent, expander, and the templated content.</summary>
    internal sealed class TreeViewRow : Control
    {
        private const float ExpanderWidth = 14.0f;

        private readonly TreeView _owner;
        private readonly object _item;
        private readonly bool _hasChildren;

        public TreeViewRow(TreeView owner, object item, int depth, bool hasChildren)
        {
            _owner = owner;
            _item = item;
            _hasChildren = hasChildren;
            Depth = depth;

            // Rows are pointer targets, but keyboard navigation belongs to the tree, so they stay out of
            // the tab order.
            Focusable = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        public int Depth { get; }

        private float ContentOffset => Depth * _owner.IndentSize + ExpanderWidth;

        protected override Thickness ContentInset => new Thickness(ContentOffset, 2.0f, 4.0f, 2.0f);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var desired = base.MeasureOverride(availableSize);
            return new Vector2(desired.X, MathF.Max(desired.Y, Theme.ControlHeight - 4.0f));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var root = Root;
            var theme = Theme;
            if (root == null)
            {
                return;
            }

            var selected = ReferenceEquals(_owner.SelectedItem, _item);

            if (selected)
            {
                drawList.FillRect(Bounds, theme.Selected, new CornerRadius(3.0f));
            }
            else if (IsPointerOver)
            {
                drawList.FillRect(Bounds, theme.Hover, new CornerRadius(3.0f));
            }

            if (!_hasChildren)
            {
                return;
            }

            // Drawn rather than taken from a font: Roboto has no geometric-shape arrows, so this used to
            // render .notdef -- the box people saw in the tree.
            var centre = new Vector2(
                Bounds.X + Depth * _owner.IndentSize + ExpanderWidth * 0.5f,
                Bounds.Center.Y);

            var size = 8.0f;
            var box = new UiRect(centre.X - size * 0.5f, centre.Y - size * 0.5f, size, size);

            if (drawList.DrawSkinIcon(theme, _owner.IsExpanded(_item) ? "arrowDown" : "arrowRight",
                    new UiRect(centre.X - 6.0f, centre.Y - 6.0f, 12.0f, 12.0f), UiColor.White))
            {
                return;
            }

            UiIcons.Triangle(
                drawList,
                box,
                _owner.IsExpanded(_item) ? UiIconDirection.Down : UiIconDirection.Right,
                theme.TextMuted,
                root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale));
        }

        /// <summary>The expander's hit area, which is deliberately the full row height for an easy target.</summary>
        private UiRect ExpanderRect => new UiRect(
            Bounds.X + Depth * _owner.IndentSize,
            Bounds.Y,
            ExpanderWidth,
            Bounds.Height);

        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || IsNestedControlTarget(e.Source))
            {
                return;
            }

            SelectOrToggle(e);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            SelectOrToggle(e);
        }

        private void SelectOrToggle(UiPointerEventArgs e)
        {
            _owner.Focus();

            if (_hasChildren && ExpanderRect.Contains(e.Position))
            {
                _owner.ToggleExpanded(_item);
                e.Handled = true;
                return;
            }

            _owner.SelectedItem = _item;
            _owner.NotifyRowPressed(e);

            // Double-click on a parent is the other common way to expand it, alongside the arrow.
            if (e.ClickCount >= 2)
            {
                if (_hasChildren)
                {
                    _owner.ToggleExpanded(_item);
                }

                _owner.NotifyRowActivated(_item);
            }

            e.Handled = true;
        }

        private bool IsNestedControlTarget(UiElement? target)
        {
            for (var current = target; current != null && !ReferenceEquals(current, this); current = current.Parent)
            {
                if (current is Control)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
