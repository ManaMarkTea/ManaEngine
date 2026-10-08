namespace ManaUI
{
    /// <summary>
    /// A wrapping grid of same-sized items that builds only the rows inside its scroll viewer, plus one either side.
    /// A WrapPanel of every item laid out and drew all of them each frame: two thousand asset tiles cost 14 ms a frame
    /// to show the thirty on screen. Items are made on demand and kept by index while they stay near the view.
    /// </summary>
    public class VirtualWrapPanel : Panel
    {
        private IReadOnlyList<object> _items = Array.Empty<object>();
        private readonly Dictionary<int, UiElement> _made = new();
        private readonly List<int> _shown = new();
        private Vector2 _madeAtSize;
        private int _columns = 1;
        private int _arrangedColumns;
        private ScrollViewer? _scroller;

        /// <summary>The items; setting a new list drops every made element.</summary>
        public IReadOnlyList<object> Items
        {
            get => _items;
            set
            {
                _items = value;
                _made.Clear();
                InvalidateMeasure();
            }
        }

        /// <summary>Builds the element for the item at an index.</summary>
        public Func<int, UiElement> CreateItem { get; set; } = _ => new Border();

        /// <summary>Every item's size; polled each measure, and a change rebuilds the items at the new size.</summary>
        public Func<Vector2> ItemSize { get; set; } = () => new Vector2(64, 64);

        public float Spacing { get; set; }

        /// <summary>How many items exist as elements now -- for tests.</summary>
        public int RealizedCount => _shown.Count;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var size = ItemSize();
            if (size != _madeAtSize)
            {
                _made.Clear();
                _madeAtSize = size;
            }

            var stepX = size.X + Spacing;
            var stepY = size.Y + Spacing;
            _columns = float.IsInfinity(inner.X) ? 1 : Math.Max(1, (int)((inner.X + Spacing) / MathF.Max(stepX, 1.0f)));
            var rows = (_items.Count + _columns - 1) / _columns;

            // Which items to make follows the width last arranged at, not this pass's: a parent may measure several
            // widths a frame (a dock sizing its columns), and making tiles for each threw away the ones the next pass
            // made again -- every frame, so no tile lived long enough to ask for its thumbnail.
            var columns = _arrangedColumns > 0 ? _arrangedColumns : _columns;
            var realizedRows = (_items.Count + columns - 1) / columns;

            // Rows in view, from the scroll viewer: the panel is its content, so the offset counts from our top.
            var firstRow = 0;
            var lastRow = Math.Min(realizedRows, 12);
            if (FindScrollViewer() is { } scroller && scroller.Viewport.Y > 0.0f)
            {
                firstRow = Math.Clamp((int)(scroller.Offset.Y / MathF.Max(stepY, 1.0f)) - 1, 0, realizedRows);
                lastRow = Math.Clamp((int)MathF.Ceiling((scroller.Offset.Y + scroller.Viewport.Y) / MathF.Max(stepY, 1.0f)) + 1, 0, realizedRows);
            }

            var first = firstRow * columns;
            var end = Math.Min(_items.Count, lastRow * columns);

            // Kept while what is made still covers the view, so a narrower pass before the real one changes nothing.
            var shownFirst = _shown.Count > 0 ? _shown[0] : -1;
            var shownEnd = _shown.Count > 0 ? _shown[^1] + 1 : -1;
            var covered = shownFirst == first && shownEnd >= end && shownEnd <= _items.Count && Children.Count == _shown.Count;
            if (!covered)
            {
                if (shownFirst == first)
                {
                    end = Math.Min(_items.Count, Math.Max(end, shownEnd));
                }

                foreach (var stale in _made.Keys.Where(index => index < first || index >= end).ToList())
                {
                    _made.Remove(stale);
                }
                _shown.Clear();
                Children.Clear();
                for (var index = first; index < end; index++)
                {
                    if (!_made.TryGetValue(index, out var element))
                    {
                        element = CreateItem(index);
                        _made[index] = element;
                    }
                    _shown.Add(index);
                    Children.Add(element);
                }
            }

            foreach (var child in Children)
            {
                child.Measure(size);
            }

            // The full extent, not just what is made, so the scroll bar covers every row.
            var width = Math.Min(_items.Count, _columns) * stepX - Spacing;
            var height = rows * stepY - Spacing;
            return LayoutHelper.Inflate(new Vector2(MathF.Max(0.0f, width), MathF.Max(0.0f, height)), Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var size = _madeAtSize;

            // Columns from the width actually given, not from whichever measure pass came last.
            _columns = Math.Max(1, (int)((content.Width + Spacing) / MathF.Max(size.X + Spacing, 1.0f)));
            if (_columns != _arrangedColumns)
            {
                _arrangedColumns = _columns;
                InvalidateMeasure();
            }
            for (var i = 0; i < Children.Count && i < _shown.Count; i++)
            {
                var index = _shown[i];
                var x = content.X + (index % _columns) * (size.X + Spacing);
                var y = content.Y + (index / _columns) * (size.Y + Spacing);
                Children[i].Arrange(new UiRect(x, y, size.X, size.Y));
            }
            return finalSize;
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
                    // Scrolling changes which rows exist, not just where they sit.
                    scroller.ScrollChanged += (_, _) => InvalidateMeasure();
                    return scroller;
                }
            }
            return null;
        }
    }
}
