using System.Collections;

namespace ManaUI
{
    /// <summary>
    /// A panel's children. Owns the parent back-pointer and invalidates layout on every structural change,
    /// so callers never have to remember to.
    /// </summary>
    public sealed class UiElementCollection : IList<UiElement>
    {
        private readonly UiElement _owner;
        private readonly List<UiElement> _items = new();

        /// <summary>Children in paint order: ZIndex ascending, declaration order within a tier.</summary>
        private List<UiElement>? _sorted;
        private bool _sortDirty = true;

        public UiElementCollection(UiElement owner)
        {
            _owner = owner;
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public UiElement this[int index]
        {
            get => _items[index];
            set
            {
                Detach(_items[index]);
                _items[index] = Attach(value);
                Invalidate();
            }
        }

        /// <summary>
        /// Paint order. Identical to declaration order unless something set a ZIndex, in which case the list
        /// is stably sorted -- cached, because re-sorting every frame would defeat the point.
        /// </summary>
        public IReadOnlyList<UiElement> PaintOrdered
        {
            get
            {
                if (!_sortDirty && _sorted != null)
                {
                    return _sorted;
                }

                _sortDirty = false;

                var needsSort = false;
                foreach (var item in _items)
                {
                    if (item.ZIndex != 0)
                    {
                        needsSort = true;
                        break;
                    }
                }

                if (!needsSort)
                {
                    _sorted = null;
                    return _items;
                }

                _sorted = _items.OrderBy(item => item.ZIndex).ToList();
                return _sorted;
            }
        }

        public void Add(UiElement item)
        {
            _items.Add(Attach(item));
            Invalidate();
        }

        public void AddRange(IEnumerable<UiElement> items)
        {
            foreach (var item in items)
            {
                _items.Add(Attach(item));
            }

            Invalidate();
        }

        public void Insert(int index, UiElement item)
        {
            _items.Insert(index, Attach(item));
            Invalidate();
        }

        public bool Remove(UiElement item)
        {
            if (!_items.Remove(item))
            {
                return false;
            }

            Detach(item);
            Invalidate();
            return true;
        }

        public void RemoveAt(int index)
        {
            Detach(_items[index]);
            _items.RemoveAt(index);
            Invalidate();
        }

        public void Clear()
        {
            foreach (var item in _items)
            {
                Detach(item);
            }

            _items.Clear();
            Invalidate();
        }

        public bool Contains(UiElement item) => _items.Contains(item);
        public int IndexOf(UiElement item) => _items.IndexOf(item);
        public void CopyTo(UiElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public List<UiElement>.Enumerator GetEnumerator() => _items.GetEnumerator();
        IEnumerator<UiElement> IEnumerable<UiElement>.GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>Called when a child's ZIndex changes so paint order is recomputed once, lazily.</summary>
        internal void InvalidatePaintOrder()
        {
            _sortDirty = true;
        }

        private UiElement Attach(UiElement item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.Parent != null && !ReferenceEquals(item.Parent, _owner))
            {
                throw new InvalidOperationException(
                    $"{item} already has a parent ({item.Parent}). Remove it before re-parenting.");
            }

            item.Parent = _owner;
            return item;
        }

        private void Detach(UiElement item)
        {
            if (ReferenceEquals(item.Parent, _owner))
            {
                // Setting Parent to null suspends the subtree's bindings, which both unhooks it from any
                // view model and leaves it able to resume if it is re-added.
                item.Parent = null;
            }
        }

        private void Invalidate()
        {
            _sortDirty = true;
            _owner.InvalidateMeasure();
        }
    }
}
