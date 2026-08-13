namespace ManaUI
{
    /// <summary>
    /// Base of every visual in the UI tree.
    /// <para>
    /// Layout is the WPF/Avalonia two-pass model: <see cref="Measure"/> asks "how big would you like to be
    /// given at most this much room", then <see cref="Arrange"/> tells the element the rect it actually got.
    /// Two passes are what make content-sizing, star-sized grid columns and scrolling fall out naturally
    /// rather than needing special cases.
    /// </para>
    /// <para>
    /// Both passes are cached behind dirty flags, so a frame in which nothing changed costs a walk of the
    /// render pass alone. Subclasses override <see cref="MeasureOverride"/> and <see cref="ArrangeOverride"/>
    /// and must never touch the public Measure/Arrange, which own the margin and alignment maths.
    /// </para>
    /// </summary>
    public abstract partial class UiElement
    {
        private Thickness _margin;
        private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
        private VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;
        private Visibility _visibility = Visibility.Visible;
        private float _width = float.NaN;
        private float _height = float.NaN;
        private float _minWidth;
        private float _minHeight;
        private float _maxWidth = float.PositiveInfinity;
        private float _maxHeight = float.PositiveInfinity;
        private bool _isEnabled = true;

        private Vector2 _lastMeasureConstraint = new Vector2(float.NaN, float.NaN);
        private UiRect _lastArrangeRect = UiRect.Empty;

        public string? Name { get; set; }

        private UiElement? _parent;

        public UiElement? Parent
        {
            get => _parent;
            internal set
            {
                if (ReferenceEquals(_parent, value))
                {
                    return;
                }

                _parent = value;

                if (value == null)
                {
                    // A detached element is nowhere on screen, so it must stop reporting the geometry it
                    // had when it was -- stale bounds would make hit testing and ScrollIntoView lie about
                    // something the user cannot see.
                    Bounds = UiRect.Empty;
                    IsMeasureValid = false;
                    IsArrangeValid = false;
                }

                SetBindingsActive(value != null);

                // Attaching can bring polled bindings with it. A subtree is built detached -- every
                // template does this -- so the bindings added while it was being constructed had no root
                // to tell, and the root's flat list is only rebuilt when it knows something changed.
                // Without this the whole subtree's polled bindings sit unevaluated until an unrelated
                // layout change happens to trigger a rescan, which is the sort of bug that looks like
                // "it updates when I resize the window".
                if (value != null)
                {
                    Root?.NotifyPolledBindingsChanged();
                }
            }
        }

        /// <summary>Arbitrary payload for application code; the framework never reads it.</summary>
        public object? Tag { get; set; }

        /// <summary>
        /// Panel-specific placement hints, null until something sets one. Panels should read this directly
        /// and fall back to defaults rather than calling EnsureLayoutSlot during layout.
        /// </summary>
        public UiLayoutSlot? LayoutSlotOrNull { get; internal set; }

        public int ZIndex => LayoutSlotOrNull?.ZIndex ?? 0;

        // ----- layout inputs -------------------------------------------------------------------------

        public Thickness Margin
        {
            get => _margin;
            set => SetLayoutProperty(ref _margin, value);
        }

        public HorizontalAlignment HorizontalAlignment
        {
            get => _horizontalAlignment;
            // Alignment changes placement inside an already-known slot, so arrange alone is enough.
            set => SetArrangeProperty(ref _horizontalAlignment, value);
        }

        public VerticalAlignment VerticalAlignment
        {
            get => _verticalAlignment;
            set => SetArrangeProperty(ref _verticalAlignment, value);
        }

        /// <summary>Explicit width, or NaN to size to content.</summary>
        public float Width
        {
            get => _width;
            set => SetLayoutProperty(ref _width, value);
        }

        /// <summary>Explicit height, or NaN to size to content.</summary>
        public float Height
        {
            get => _height;
            set => SetLayoutProperty(ref _height, value);
        }

        public float MinWidth
        {
            get => _minWidth;
            set => SetLayoutProperty(ref _minWidth, value);
        }

        public float MinHeight
        {
            get => _minHeight;
            set => SetLayoutProperty(ref _minHeight, value);
        }

        public float MaxWidth
        {
            get => _maxWidth;
            set => SetLayoutProperty(ref _maxWidth, value);
        }

        public float MaxHeight
        {
            get => _maxHeight;
            set => SetLayoutProperty(ref _maxHeight, value);
        }

        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility == value)
                {
                    return;
                }

                _visibility = value;

                // Panels skip collapsed children entirely, so Arrange never runs on this element again and
                // neither it nor its descendants would ever clear the geometry they had when last shown.
                // Bounds is public, and something invisible reporting where it used to be makes it lie to
                // hit testing, ScrollIntoView and anything else that asks.
                if (value == Visibility.Collapsed)
                {
                    ClearSubtreeBounds();
                }

                InvalidateMeasure();
            }
        }

        private void ClearSubtreeBounds()
        {
            Bounds = UiRect.Empty;

            // The cached validity has to go with the geometry. Measure and Arrange both short-circuit when
            // they are still valid for the same input, so clearing bounds while leaving the flags set
            // would mean a later Arrange at the identical rect returns immediately and never restores what
            // was cleared -- the subtree would come back visible but sized to nothing.
            IsMeasureValid = false;
            IsArrangeValid = false;
            _lastArrangeRect = UiRect.Empty;
            _lastMeasureConstraint = new Vector2(float.NaN, float.NaN);

            var count = VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                GetVisualChild(i).ClearSubtreeBounds();
            }
        }

        /// <summary>Local enabled flag. Read <see cref="IsEffectivelyEnabled"/> for the inherited answer.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    InvalidateVisual();
                }
            }
        }

        /// <summary>False when this element or any ancestor is disabled.</summary>
        public bool IsEffectivelyEnabled => _isEnabled && (Parent?.IsEffectivelyEnabled ?? true);

        public float Opacity { get; set; } = 1.0f;

        /// <summary>Clips descendants to <see cref="Bounds"/>. Required for scrolling; costs nothing when off.</summary>
        public bool ClipToBounds { get; set; }

        public bool IsHitTestVisible { get; set; } = true;

        // ----- layout outputs ------------------------------------------------------------------------

        /// <summary>Size requested by the last measure pass, including this element's margin.</summary>
        public Vector2 DesiredSize { get; private set; }

        /// <summary>Final rect from the last arrange pass, in root coordinates.</summary>
        public UiRect Bounds { get; private set; }

        /// <summary>Bounds minus padding -- the area a subclass lays content into. Padding-less by default.</summary>
        public virtual UiRect ContentBounds => Bounds;

        public bool IsMeasureValid { get; private set; }
        public bool IsArrangeValid { get; private set; }

        // ----- input state (written by the input router) ---------------------------------------------

        public bool IsPointerOver { get; internal set; }
        public bool IsFocused { get; internal set; }

        // ----- visual tree ---------------------------------------------------------------------------

        public virtual int VisualChildCount => 0;

        public virtual UiElement GetVisualChild(int index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "This element has no visual children.");
        }

        public IEnumerable<UiElement> VisualChildren
        {
            get
            {
                for (var i = 0; i < VisualChildCount; i++)
                {
                    yield return GetVisualChild(i);
                }
            }
        }

        /// <summary>Walks up to the owning root, or null while the element is detached.</summary>
        public UiRoot? Root
        {
            get
            {
                var current = this;
                while (current != null)
                {
                    if (current is UiRoot root)
                    {
                        return root;
                    }

                    current = current.Parent;
                }

                return null;
            }
        }

        // ----- invalidation --------------------------------------------------------------------------

        /// <summary>
        /// Marks this element's size as unknown. Propagates upward, because a child growing can change the
        /// parent's desired size -- and stops at the first ancestor already dirty, which keeps a deep tree's
        /// invalidation cost proportional to the changed path rather than the tree.
        /// </summary>
        public void InvalidateMeasure()
        {
            IsMeasureValid = false;
            IsArrangeValid = false;

            // Walks up while ancestors still believe they are valid. Stopping at the first already-invalid
            // ancestor is safe -- it has propagated once already. Stopping because *this* element was
            // invalid is not: a parent that skipped it, as panels do for collapsed children, has since
            // completed a perfectly valid layout, and would never learn it has to run again.
            var ancestor = Parent;
            while (ancestor is { IsMeasureValid: true })
            {
                ancestor.IsMeasureValid = false;
                ancestor.IsArrangeValid = false;
                ancestor = ancestor.Parent;
            }

            Root?.NotifyLayoutInvalidated();
        }

        /// <summary>Marks placement as stale without re-asking for sizes.</summary>
        public void InvalidateArrange()
        {
            IsArrangeValid = false;

            var ancestor = Parent;
            while (ancestor is { IsArrangeValid: true })
            {
                ancestor.IsArrangeValid = false;
                ancestor = ancestor.Parent;
            }

            Root?.NotifyLayoutInvalidated();
        }

        /// <summary>Appearance changed but geometry did not -- a colour, a hover state, a caret blink.</summary>
        public void InvalidateVisual()
        {
            Root?.NotifyVisualInvalidated();
        }

        protected void SetLayoutProperty<T>(ref T field, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                InvalidateMeasure();
            }
        }

        protected void SetArrangeProperty<T>(ref T field, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                InvalidateArrange();
            }
        }

        protected void SetVisualProperty<T>(ref T field, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                InvalidateVisual();
            }
        }

        // ----- measure -------------------------------------------------------------------------------

        /// <summary>
        /// Computes <see cref="DesiredSize"/> for at most <paramref name="availableSize"/>. Either axis may
        /// be infinite, meaning "unconstrained" -- an element must still return something finite.
        /// </summary>
        public void Measure(Vector2 availableSize)
        {
            if (_visibility == Visibility.Collapsed)
            {
                DesiredSize = Vector2.Zero;
                IsMeasureValid = true;
                _lastMeasureConstraint = availableSize;
                return;
            }

            if (IsMeasureValid && LayoutHelper.AreClose(_lastMeasureConstraint, availableSize))
            {
                return;
            }

            _lastMeasureConstraint = availableSize;

            var constraint = LayoutHelper.Deflate(availableSize, _margin);
            constraint = ApplySizeConstraints(constraint);

            var desired = MeasureOverride(constraint);
            desired = LayoutHelper.Finite(desired);

            // An explicit Width/Height wins over whatever the content asked for.
            if (!LayoutHelper.IsAuto(_width))
            {
                desired.X = _width;
            }

            if (!LayoutHelper.IsAuto(_height))
            {
                desired.Y = _height;
            }

            desired = ClampToMinMax(desired);

            DesiredSize = LayoutHelper.Inflate(desired, _margin);
            IsMeasureValid = true;
            IsArrangeValid = false;
        }

        /// <summary>
        /// Returns the size this element wants, given a constraint already reduced by margin and clamped by
        /// Min/Max/Width/Height. Containers measure their children here.
        /// </summary>
        protected abstract Vector2 MeasureOverride(Vector2 availableSize);

        // ----- arrange -------------------------------------------------------------------------------

        /// <summary>Places the element within <paramref name="finalRect"/>, honouring margin and alignment.</summary>
        public void Arrange(UiRect finalRect)
        {
            if (_visibility == Visibility.Collapsed)
            {
                Bounds = UiRect.Empty;
                IsArrangeValid = true;
                _lastArrangeRect = finalRect;
                return;
            }

            if (IsArrangeValid && _lastArrangeRect == finalRect)
            {
                return;
            }

            _lastArrangeRect = finalRect;

            if (!IsMeasureValid)
            {
                Measure(finalRect.Size);
            }

            var slot = finalRect.Deflate(_margin);
            var size = slot.Size;
            var contentDesired = LayoutHelper.Deflate(DesiredSize, _margin);

            // A non-stretch alignment means the element takes only what it asked for and the leftover space
            // is used for positioning instead.
            if (_horizontalAlignment != HorizontalAlignment.Stretch || !LayoutHelper.IsAuto(_width))
            {
                size.X = MathF.Min(contentDesired.X, slot.Width);
            }

            if (_verticalAlignment != VerticalAlignment.Stretch || !LayoutHelper.IsAuto(_height))
            {
                size.Y = MathF.Min(contentDesired.Y, slot.Height);
            }

            size = ClampToMinMax(size);

            var offsetX = _horizontalAlignment switch
            {
                HorizontalAlignment.Center => (slot.Width - size.X) * 0.5f,
                HorizontalAlignment.Right => slot.Width - size.X,
                _ => 0.0f
            };

            var offsetY = _verticalAlignment switch
            {
                VerticalAlignment.Center => (slot.Height - size.Y) * 0.5f,
                VerticalAlignment.Bottom => slot.Height - size.Y,
                _ => 0.0f
            };

            // Bounds is published BEFORE ArrangeOverride runs, because children are placed in absolute root
            // coordinates and containers derive their child rects from this element's Bounds/ContentBounds.
            // (WPF can arrange in local space and apply an offset transform afterwards; without a transform
            // in the pipeline, the parent's final position has to be known first.)
            var origin = new Vector2(slot.X + MathF.Max(0.0f, offsetX), slot.Y + MathF.Max(0.0f, offsetY));
            Bounds = new UiRect(origin, size);

            var actual = LayoutHelper.Finite(ArrangeOverride(size));
            actual = LayoutHelper.Min(actual, LayoutHelper.Max(size, contentDesired));

            // A container that used less than it was offered keeps its origin rather than re-aligning; the
            // alternative would mean arranging children twice for a case panels essentially never hit.
            if (!LayoutHelper.AreClose(actual, size))
            {
                Bounds = new UiRect(origin, actual);
            }

            IsArrangeValid = true;
        }

        /// <summary>
        /// Lays out children inside <paramref name="finalSize"/> and returns the size actually used. The
        /// default consumes the whole slot, which is what a leaf wants.
        /// </summary>
        protected virtual Vector2 ArrangeOverride(Vector2 finalSize)
        {
            return finalSize;
        }

        // ----- render --------------------------------------------------------------------------------

        /// <summary>Emits this element and its subtree into the draw list. Called by the root each frame.</summary>
        public void Render(ManaUiDrawList drawList)
        {
            if (_visibility != Visibility.Visible || Bounds.IsEmpty || Opacity <= 0.0f)
            {
                return;
            }

            var pushedOpacity = Opacity < 1.0f;
            if (pushedOpacity)
            {
                drawList.PushOpacity(Opacity);
            }

            OnRender(drawList);

            var pushedClip = ClipToBounds;
            if (pushedClip)
            {
                drawList.PushClip(Bounds);
            }

            // Skipping the whole subtree once the clip has collapsed is the single biggest win in a long
            // scrolled list -- offscreen rows cost one rect test rather than a full render walk.
            if (!drawList.IsClippedOut)
            {
                RenderChildren(drawList);
            }

            // Drawn after children so an element can put chrome over its own content -- scrollbars being
            // the reason this exists.
            OnRenderOverlay(drawList);

            if (pushedClip)
            {
                drawList.PopClip();
            }

            if (pushedOpacity)
            {
                drawList.PopOpacity();
            }
        }

        /// <summary>Draws this element itself, before any children.</summary>
        protected virtual void OnRender(ManaUiDrawList drawList)
        {
        }

        /// <summary>Draws on top of this element's children.</summary>
        protected virtual void OnRenderOverlay(ManaUiDrawList drawList)
        {
        }

        protected virtual void RenderChildren(ManaUiDrawList drawList)
        {
            var count = VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                GetVisualChild(i).Render(drawList);
            }
        }

        // ----- hit testing ---------------------------------------------------------------------------

        /// <summary>
        /// Whether this element can be a hit result in its own right, as opposed to merely routing the test
        /// to its children.
        /// <para>
        /// False by default, so invisible layout scaffolding -- a StackPanel with no background, a bare
        /// content host -- lets clicks fall through to whatever is behind it, which is nearly always what
        /// the author meant. Anything that paints or reacts opts in.
        /// </para>
        /// </summary>
        protected virtual bool IsSelfHitTestVisible => false;

        /// <summary>
        /// Returns the topmost hit-testable element under <paramref name="point"/>, or null. Children are
        /// tested in reverse order because later children draw on top.
        /// </summary>
        public virtual UiElement? HitTest(Vector2 point)
        {
            if (_visibility != Visibility.Visible || !IsHitTestVisible || !Bounds.Contains(point))
            {
                return null;
            }

            for (var i = VisualChildCount - 1; i >= 0; i--)
            {
                var hit = GetVisualChild(i).HitTest(point);
                if (hit != null)
                {
                    return hit;
                }
            }

            return IsSelfHitTestVisible ? this : null;
        }

        /// <summary>True when <paramref name="element"/> is this element or sits beneath it.</summary>
        public bool IsAncestorOf(UiElement? element)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, this))
                {
                    return true;
                }

                element = element.Parent;
            }

            return false;
        }

        // ----- helpers -------------------------------------------------------------------------------

        private Vector2 ApplySizeConstraints(Vector2 size)
        {
            if (!LayoutHelper.IsAuto(_width))
            {
                size.X = _width;
            }

            if (!LayoutHelper.IsAuto(_height))
            {
                size.Y = _height;
            }

            return ClampToMinMax(size);
        }

        private Vector2 ClampToMinMax(Vector2 size)
        {
            return new Vector2(
                Math.Clamp(size.X, _minWidth, MathF.Max(_minWidth, _maxWidth)),
                Math.Clamp(size.Y, _minHeight, MathF.Max(_minHeight, _maxHeight)));
        }

        public override string ToString()
        {
            return Name is null ? GetType().Name : $"{GetType().Name} '{Name}'";
        }
    }
}
