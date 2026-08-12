using System.Collections;
using System.Collections.Specialized;

namespace ManaUI
{
    /// <summary>
    /// Renders a collection by building one element per item from a template.
    /// <para>
    /// A template here is just a <c>Func&lt;T, UiElement&gt;</c> rather than a declarative resource. It is
    /// the same decision as the fluent builders: an ordinary C# function is compile-checked, debuggable,
    /// and can branch on the item without inventing a mini-language for it.
    /// </para>
    /// <para>
    /// When the source is an <see cref="INotifyCollectionChanged"/>, changes are applied incrementally --
    /// adding one row to a thousand-row list rebuilds one element, not a thousand.
    /// </para>
    /// </summary>
    public class ItemsControl : Panel
    {
        private IEnumerable? _itemsSource;
        private Func<object, UiElement>? _itemTemplate;
        private INotifyCollectionChanged? _observableSource;
        private Orientation _orientation = Orientation.Vertical;
        private float _spacing;

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                if (ReferenceEquals(_itemsSource, value))
                {
                    return;
                }

                if (_observableSource != null)
                {
                    _observableSource.CollectionChanged -= OnCollectionChanged;
                    _observableSource = null;
                }

                _itemsSource = value;

                if (value is INotifyCollectionChanged observable)
                {
                    _observableSource = observable;
                    _observableSource.CollectionChanged += OnCollectionChanged;
                }

                RebuildItems();
            }
        }

        /// <summary>Builds the element for one item. Setting it rebuilds every row.</summary>
        public Func<object, UiElement>? ItemTemplate
        {
            get => _itemTemplate;
            set
            {
                _itemTemplate = value;
                RebuildItems();
            }
        }

        public Orientation Orientation
        {
            get => _orientation;
            set => SetLayoutProperty(ref _orientation, value);
        }

        public float Spacing
        {
            get => _spacing;
            set => SetLayoutProperty(ref _spacing, value);
        }

        /// <summary>Shown in place of the rows when the source is empty or unset.</summary>
        public UiElement? EmptyPlaceholder { get; set; }

        /// <summary>Strongly-typed template helper, so callers avoid casting inside every template.</summary>
        public void SetItemTemplate<T>(Func<T, UiElement> template)
        {
            ArgumentNullException.ThrowIfNull(template);
            ItemTemplate = item => template((T)item);
        }

        private void RebuildItems()
        {
            Children.Clear();

            if (_itemsSource == null || _itemTemplate == null)
            {
                AddPlaceholderIfEmpty();
                return;
            }

            foreach (var item in _itemsSource)
            {
                Children.Add(CreateContainer(item));
            }

            AddPlaceholderIfEmpty();
        }

        private void AddPlaceholderIfEmpty()
        {
            if (Children.Count == 0 && EmptyPlaceholder != null)
            {
                Children.Add(EmptyPlaceholder);
            }
        }

        private UiElement CreateContainer(object? item)
        {
            var element = _itemTemplate!(item!);

            // The item is stashed so Remove can find the row that belongs to it without re-running the
            // template or relying on index arithmetic that a concurrent edit could invalidate.
            element.Tag = item;
            return element;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_itemTemplate == null)
            {
                return;
            }

            // The placeholder is not a row; drop it before any incremental edit so indices line up.
            if (EmptyPlaceholder != null && Children.Count == 1 && ReferenceEquals(Children[0], EmptyPlaceholder))
            {
                Children.Clear();
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                {
                    var index = e.NewStartingIndex < 0 ? Children.Count : e.NewStartingIndex;
                    foreach (var item in e.NewItems)
                    {
                        Children.Insert(Math.Clamp(index++, 0, Children.Count), CreateContainer(item));
                    }

                    break;
                }
                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                {
                    foreach (var item in e.OldItems)
                    {
                        RemoveContainerFor(item);
                    }

                    break;
                }
                case NotifyCollectionChangedAction.Replace when e.NewItems != null:
                {
                    var index = e.NewStartingIndex;
                    foreach (var item in e.NewItems)
                    {
                        if (index >= 0 && index < Children.Count)
                        {
                            Children[index] = CreateContainer(item);
                        }

                        index++;
                    }

                    break;
                }
                case NotifyCollectionChangedAction.Move:
                {
                    if (e.OldStartingIndex >= 0 && e.OldStartingIndex < Children.Count)
                    {
                        var moved = Children[e.OldStartingIndex];
                        Children.RemoveAt(e.OldStartingIndex);
                        Children.Insert(Math.Clamp(e.NewStartingIndex, 0, Children.Count), moved);
                    }

                    break;
                }
                default:
                {
                    // Reset carries no item information, so a full rebuild is the only correct response.
                    RebuildItems();
                    return;
                }
            }

            AddPlaceholderIfEmpty();
        }

        private void RemoveContainerFor(object? item)
        {
            for (var i = 0; i < Children.Count; i++)
            {
                if (Equals(Children[i].Tag, item))
                {
                    Children.RemoveAt(i);
                    return;
                }
            }
        }

        // ----- layout: same stacking rules as StackPanel ------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var isVertical = _orientation == Orientation.Vertical;

            var childConstraint = isVertical
                ? new Vector2(inner.X, LayoutHelper.Infinity)
                : new Vector2(LayoutHelper.Infinity, inner.Y);

            var stacked = 0.0f;
            var crossMax = 0.0f;
            var visibleCount = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                child.Measure(childConstraint);
                var desired = child.DesiredSize;

                if (isVertical)
                {
                    stacked += desired.Y;
                    crossMax = MathF.Max(crossMax, desired.X);
                }
                else
                {
                    stacked += desired.X;
                    crossMax = MathF.Max(crossMax, desired.Y);
                }

                visibleCount++;
            }

            if (visibleCount > 1)
            {
                stacked += _spacing * (visibleCount - 1);
            }

            var content = isVertical ? new Vector2(crossMax, stacked) : new Vector2(stacked, crossMax);
            return LayoutHelper.Inflate(content, Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var isVertical = _orientation == Orientation.Vertical;
            var origin = Padding.TopLeft;
            var inner = LayoutHelper.Deflate(finalSize, Padding);
            var offset = 0.0f;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var desired = child.DesiredSize;

                var slot = isVertical
                    ? new UiRect(Bounds.X + origin.X, Bounds.Y + origin.Y + offset, inner.X, desired.Y)
                    : new UiRect(Bounds.X + origin.X + offset, Bounds.Y + origin.Y, desired.X, inner.Y);

                child.Arrange(slot);
                offset += (isVertical ? desired.Y : desired.X) + _spacing;
            }

            return finalSize;
        }
    }
}
