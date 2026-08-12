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
        private readonly List<object> _visibleItems = new();

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
            set
            {
                if (ReferenceEquals(_selectedItem, value))
                {
                    return;
                }

                _selectedItem = value;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, value);
            }
        }

        /// <summary>Items currently shown, in display order.</summary>
        public IReadOnlyList<object> VisibleItems => _visibleItems;

        public bool IsExpanded(object item) => _expanded.Contains(item);

        public void Expand(object item)
        {
            if (_expanded.Add(item))
            {
                QueueRebuild();
            }
        }

        public void Collapse(object item)
        {
            if (_expanded.Remove(item))
            {
                QueueRebuild();
            }
        }

        public void ToggleExpanded(object item)
        {
            if (!_expanded.Remove(item))
            {
                _expanded.Add(item);
            }

            QueueRebuild();
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
            QueueRebuild();
        }

        /// <summary>Forces the row list to be rebuilt on the next layout.</summary>
        public void QueueRebuild()
        {
            _rebuildQueued = true;
            InvalidateMeasure();
        }

        // ----- row construction -----------------------------------------------------------------------

        private void RebuildRows()
        {
            _rebuildQueued = false;

            Children.Clear();
            _visibleItems.Clear();

            if (_itemsSource == null || ItemTemplate == null)
            {
                return;
            }

            AddLevel(_itemsSource, 0);
        }

        private void AddLevel(IEnumerable items, int depth)
        {
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var children = ChildrenSelector?.Invoke(item);
                var hasChildren = false;

                if (children != null)
                {
                    foreach (var _ in children)
                    {
                        hasChildren = true;
                        break;
                    }
                }

                _visibleItems.Add(item);
                Children.Add(new TreeViewRow(this, item, depth, hasChildren)
                {
                    Child = ItemTemplate!(item)
                });

                if (hasChildren && _expanded.Contains(item))
                {
                    AddLevel(children!, depth + 1);
                }
            }
        }

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_rebuildQueued)
            {
                RebuildRows();
            }

            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var height = 0.0f;
            var width = 0.0f;

            foreach (var child in Children)
            {
                child.Measure(new Vector2(inner.X, LayoutHelper.Infinity));
                height += child.DesiredSize.Y;
                width = MathF.Max(width, child.DesiredSize.X);
            }

            return LayoutHelper.Inflate(new Vector2(width, height), Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var offset = 0.0f;

            foreach (var child in Children)
            {
                var height = child.DesiredSize.Y;
                child.Arrange(new UiRect(content.X, content.Y + offset, content.Width, height));
                offset += height;
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

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var glyph = font.GetGlyph(_owner.IsExpanded(_item) ? '▾' : '▸');
            var centre = new Vector2(
                Bounds.X + Depth * _owner.IndentSize + ExpanderWidth * 0.5f,
                Bounds.Center.Y);

            if (glyph.HasPixels)
            {
                drawList.DrawGlyph(
                    new UiRect(
                        font.SnapToPixel(centre.X - glyph.Size.X * 0.5f),
                        font.SnapToPixel(centre.Y - glyph.Size.Y * 0.5f),
                        glyph.Size.X,
                        glyph.Size.Y),
                    glyph.AtlasUv,
                    theme.TextMuted);
            }
            else
            {
                // Fallback for a font without the arrows: a filled square reads as "has children".
                drawList.FillRect(
                    new UiRect(centre.X - 3.0f, centre.Y - 3.0f, 6.0f, 6.0f),
                    theme.TextMuted,
                    new CornerRadius(1.0f));
            }
        }

        /// <summary>The expander's hit area, which is deliberately the full row height for an easy target.</summary>
        private UiRect ExpanderRect => new UiRect(
            Bounds.X + Depth * _owner.IndentSize,
            Bounds.Y,
            ExpanderWidth,
            Bounds.Height);

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _owner.Focus();

            if (_hasChildren && ExpanderRect.Contains(e.Position))
            {
                _owner.ToggleExpanded(_item);
                e.Handled = true;
                return;
            }

            _owner.SelectedItem = _item;

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
    }
}
