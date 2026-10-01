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

            // Rows in view, from the scroll viewer: the panel is its content, so the offset counts from our top.
            var firstRow = 0;
            var lastRow = Math.Min(rows, 12);
            if (FindScrollViewer() is { } scroller && scroller.Viewport.Y > 0.0f)
            {
                firstRow = Math.Clamp((int)(scroller.Offset.Y / MathF.Max(stepY, 1.0f)) - 1, 0, rows);
                lastRow = Math.Clamp((int)MathF.Ceiling((scroller.Offset.Y + scroller.Viewport.Y) / MathF.Max(stepY, 1.0f)) + 1, 0, rows);
            }

            var first = firstRow * _columns;
            var end = Math.Min(_items.Count, lastRow * _columns);
            if (_shown.Count != Math.Max(0, end - first) || (_shown.Count > 0 && _shown[0] != first) || Children.Count != _shown.Count)
            {
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
