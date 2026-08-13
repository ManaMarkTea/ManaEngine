using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The panel's shared measurements. One instance per inspector, read by every row, so dragging the
    /// divider on any row moves the whole column and the labels stay aligned down the panel.
    /// </summary>
    public sealed class InspectorMetrics
    {
        /// <summary>Narrow enough to be worth doing, wide enough that a label is not a single letter.</summary>
        public const float MinimumLabelWidth = 40.0f;

        public const float MaximumLabelWidth = 320.0f;

        private float _labelWidth = 104.0f;

        /// <summary>
        /// Width of the name column. Clamped rather than free: dragged to zero the panel would be a
        /// column of anonymous editors, and dragged past the panel there would be nowhere to type.
        /// </summary>
        public float LabelWidth
        {
            get => _labelWidth;
            set => _labelWidth = Math.Clamp(value, MinimumLabelWidth, MaximumLabelWidth);
        }
    }

    /// <summary>
    /// The line between the name column and the editors, dragged to move it.
    /// <para>
    /// One of these sits in every row, which is what makes the divider a continuous line you can grab
    /// anywhere rather than a single handle to hunt for at the top of the panel.
    /// </para>
    /// </summary>
    public sealed class InspectorSplitter : UiElement
    {
        private readonly InspectorMetrics _metrics;

        private bool _dragging;
        private float _dragStartPointer;
        private float _dragStartWidth;

        public InspectorSplitter(InspectorMetrics metrics)
        {
            _metrics = metrics;
            Width = Thickness;
        }

        /// <summary>Wide enough to hit without being wide enough to notice.</summary>
        public const float Thickness = 5.0f;

        protected override bool IsSelfHitTestVisible => true;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return new Vector2(Thickness, 0.0f);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _dragging = true;
            _dragStartPointer = e.Position.X;
            _dragStartWidth = _metrics.LabelWidth;

            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            // Measured from where the press landed rather than from the last move, so clamping at either
            // end cannot make the column drift away from the pointer.
            _metrics.LabelWidth = _dragStartWidth + (e.Position.X - _dragStartPointer);
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            ReleasePointerCapture();
            e.Handled = true;
        }

        public override UiCursor GetCursor(Vector2 point)
        {
            return UiCursor.SizeWE;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            // Always drawn. Painting it only on hover made it a control you had to already know about --
            // there was nothing to aim at, and nothing to say the column could move at all. Quiet at
            // rest, obvious once the pointer is on it.
            var colour = _dragging ? theme.Accent
                : IsPointerOver ? theme.BorderStrong
                : theme.Border;

            var centre = Bounds.X + Bounds.Width * 0.5f;

            drawList.FillRect(
                new UiRect(centre - 0.5f, Bounds.Y, 1.0f, Bounds.Height),
                colour);
        }
    }
}
