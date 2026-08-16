namespace ManaUI
{
    /// <summary>
    /// Divides its area between child nodes along one axis, with draggable splitters between them.
    /// <para>
    /// Panes are the children; splitters are drawn and hit-tested by this node rather than being elements.
    /// That keeps <see cref="Panel.Children"/> a clean list of panes, which is what makes the tree
    /// straightforward to walk, serialise and restructure -- a splitter in the child list would have to be
    /// filtered out of every one of those operations.
    /// </para>
    /// <para>
    /// Sizes are proportional rather than absolute so a layout survives a window resize intact.
    /// </para>
    /// </summary>
    public class DockSplit : DockNode
    {
        private const float SplitterThickness = 6.0f;

        /// <summary>Smallest a pane may be squeezed to by a splitter drag.</summary>
        private const float MinimumPaneSize = 40.0f;

        private readonly List<float> _weights = new();

        private Orientation _orientation = Orientation.Horizontal;
        private int _draggingSplitter = -1;
        private float _dragStartPointer;
        private float _dragStartWeightBefore;
        private float _dragStartWeightAfter;

        public DockSplit()
        {
        }

        public DockSplit(Orientation orientation)
        {
            _orientation = orientation;
        }

        /// <summary>Horizontal places panes side by side; vertical stacks them.</summary>
        public Orientation Orientation
        {
            get => _orientation;
            set => SetLayoutProperty(ref _orientation, value);
        }

        public override bool IsEmpty => Children.Count == 0;

        /// <summary>Relative share of each pane. Always normalised to sum to one.</summary>
        public IReadOnlyList<float> Weights
        {
            get
            {
                NormalizeWeights();
                return _weights;
            }
        }

        public void Add(DockNode node, float weight = 1.0f)
        {
            Children.Add(node);
            _weights.Add(MathF.Max(0.01f, weight));
            NormalizeWeights();
        }

        public void Insert(int index, DockNode node, float weight = 1.0f)
        {
            index = Math.Clamp(index, 0, Children.Count);
            Children.Insert(index, node);
            _weights.Insert(index, MathF.Max(0.01f, weight));
            NormalizeWeights();
        }

        public void Remove(DockNode node)
        {
            var index = Children.IndexOf(node);
            if (index < 0)
            {
                return;
            }

            Children.RemoveAt(index);
            if (index < _weights.Count)
            {
                _weights.RemoveAt(index);
            }

            NormalizeWeights();
        }

        /// <summary>
        /// Sets one pane's share. Weights are renormalised afterwards, so this changes the ratio between
        /// this pane and the others rather than assigning an absolute fraction -- to restore an exact set
        /// of proportions, use <see cref="SetWeights"/>.
        /// </summary>
        public void SetWeight(int index, float weight)
        {
            if (index >= 0 && index < _weights.Count)
            {
                _weights[index] = MathF.Max(0.01f, weight);
                NormalizeWeights();
                InvalidateArrange();
            }
        }

        /// <summary>
        /// Replaces every weight at once, normalising a single time.
        /// <para>
        /// Necessary for restoring a saved layout: assigning weights one at a time renormalises after each
        /// call, so each later assignment is measured against ratios the earlier ones already changed and
        /// the result drifts from what was saved.
        /// </para>
        /// </summary>
        public void SetWeights(IReadOnlyList<float> weights)
        {
            ArgumentNullException.ThrowIfNull(weights);

            _weights.Clear();
            for (var i = 0; i < Children.Count; i++)
            {
                _weights.Add(i < weights.Count ? MathF.Max(0.01f, weights[i]) : 1.0f);
            }

            NormalizeWeights();
            InvalidateArrange();
        }

        /// <summary>
        /// Keeps the weight list the same length as the children and summing to one. Called defensively
        /// because Children is public and can be edited without going through Add/Remove.
        /// </summary>
        private void NormalizeWeights()
        {
            while (_weights.Count < Children.Count)
            {
                _weights.Add(1.0f);
            }

            while (_weights.Count > Children.Count)
            {
                _weights.RemoveAt(_weights.Count - 1);
            }

            var total = 0.0f;
            foreach (var weight in _weights)
            {
                total += weight;
            }

            if (total <= 0.0f)
            {
                for (var i = 0; i < _weights.Count; i++)
                {
                    _weights[i] = 1.0f / MathF.Max(1, _weights.Count);
                }

                return;
            }

            for (var i = 0; i < _weights.Count; i++)
            {
                _weights[i] /= total;
            }
        }

        // ----- layout ---------------------------------------------------------------------------------

        private bool IsHorizontal => _orientation == Orientation.Horizontal;

        private float TotalSplitterThickness => MathF.Max(0, Children.Count - 1) * SplitterThickness;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            NormalizeWeights();

            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var usable = MathF.Max(0.0f, (IsHorizontal ? inner.X : inner.Y) - TotalSplitterThickness);

            for (var i = 0; i < Children.Count; i++)
            {
                var share = usable * _weights[i];
                Children[i].Measure(IsHorizontal
                    ? new Vector2(share, inner.Y)
                    : new Vector2(inner.X, share));
            }

            // A split fills whatever it is given; its children's desired sizes do not drive it.
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            NormalizeWeights();

            var content = ContentBounds;
            var usable = MathF.Max(0.0f, (IsHorizontal ? content.Width : content.Height) - TotalSplitterThickness);
            var offset = 0.0f;

            for (var i = 0; i < Children.Count; i++)
            {
                // The last pane takes the rounding remainder, so the panes always tile the area exactly
                // rather than leaving a sub-pixel gap at the end.
                var share = i == Children.Count - 1
                    ? MathF.Max(0.0f, usable - offset)
                    : MathF.Round(usable * _weights[i]);

                Children[i].Arrange(IsHorizontal
                    ? new UiRect(content.X + offset + i * SplitterThickness, content.Y, share, content.Height)
                    : new UiRect(content.X, content.Y + offset + i * SplitterThickness, content.Width, share));

                offset += share;
            }

            return finalSize;
        }

        /// <summary>The splitter between pane <paramref name="index"/> and the one after it.</summary>
        private UiRect SplitterRect(int index)
        {
            if (index < 0 || index >= Children.Count - 1)
            {
                return UiRect.Empty;
            }

            var pane = Children[index].Bounds;

            return IsHorizontal
                ? new UiRect(pane.Right, pane.Y, SplitterThickness, pane.Height)
                : new UiRect(pane.X, pane.Bottom, pane.Width, SplitterThickness);
        }

        private int SplitterAt(Vector2 point)
        {
            for (var i = 0; i < Children.Count - 1; i++)
            {
                if (SplitterRect(i).Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var pointer = Root?.Input.PointerPosition ?? new Vector2(-1.0f);

            for (var i = 0; i < Children.Count - 1; i++)
            {
                var rect = SplitterRect(i);
                var active = _draggingSplitter == i || (_draggingSplitter < 0 && rect.Contains(pointer));

                // Filled first, always. The gutter between two panes is a gap in the panel surface, and
                // leaving it unpainted let whatever the dock sits over -- the viewport -- show through.
                // Surface, not Background: Background is the shell colour outside the dock entirely, and
                // painting the gutter with it left a seam noticeably darker than the panes either side of
                // it -- which read as a gap even though every pixel was opaque.
                if (theme.BackdropBlur > 0.0f)
                {
                    drawList.FillBackdrop(rect, theme.Frosted(theme.Surface), theme.BackdropBlur, default);
                }
                else
                {
                    drawList.FillRect(rect, theme.Surface);
                }

                // The visible line is thinner than the grab area: a 6px bar is easy to hit but ugly to look
                // at, so only a hairline is drawn unless it is being used.
                if (active)
                {
                    drawList.FillRect(rect, theme.Accent.WithAlpha(0.5f));
                }

                var line = IsHorizontal
                    ? new UiRect(rect.Center.X - 0.5f, rect.Y, 1.0f, rect.Height)
                    : new UiRect(rect.X, rect.Center.Y - 0.5f, rect.Width, 1.0f);

                drawList.FillRect(line, theme.Border);
            }
        }

        // ----- input ----------------------------------------------------------------------------------

        public override UiCursor GetCursor(Vector2 point)
        {
            if (_draggingSplitter >= 0 || SplitterAt(point) >= 0)
            {
                return IsHorizontal ? UiCursor.SizeWE : UiCursor.SizeNS;
            }

            return UiCursor.Arrow;
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            var index = SplitterAt(e.Position);
            if (index < 0)
            {
                return;
            }

            _draggingSplitter = index;
            _dragStartPointer = IsHorizontal ? e.Position.X : e.Position.Y;
            _dragStartWeightBefore = _weights[index];
            _dragStartWeightAfter = _weights[index + 1];

            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_draggingSplitter < 0)
            {
                return;
            }

            var content = ContentBounds;
            var usable = MathF.Max(1.0f, (IsHorizontal ? content.Width : content.Height) - TotalSplitterThickness);

            var pointer = IsHorizontal ? e.Position.X : e.Position.Y;
            var deltaWeight = (pointer - _dragStartPointer) / usable;

            // A drag only ever moves weight between the two adjacent panes; everything else in the split
            // keeps its share, which is what stops a drag from rippling across the whole layout.
            var pair = _dragStartWeightBefore + _dragStartWeightAfter;
            var minimum = MinimumPaneSize / usable;

            var before = Math.Clamp(_dragStartWeightBefore + deltaWeight, minimum, MathF.Max(minimum, pair - minimum));

            _weights[_draggingSplitter] = before;
            _weights[_draggingSplitter + 1] = pair - before;

            InvalidateArrange();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_draggingSplitter >= 0)
            {
                _draggingSplitter = -1;
                ReleasePointerCapture();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        /// <summary>Hit-testable so splitter drags work, even though a split paints no background.</summary>
        protected override bool IsSelfHitTestVisible => true;
    }
}
