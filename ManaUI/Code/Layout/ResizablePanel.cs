namespace ManaUI
{
    /// <summary>
    /// A panel with one draggable edge, for the side and bottom columns an editor is built from.
    /// <para>
    /// Distinct from <see cref="DockSplit"/>'s splitters, which divide a fixed area between siblings. This
    /// one has no sibling to trade with -- it sits over whatever is behind it, and dragging changes how
    /// much of the window it covers.
    /// </para>
    /// </summary>
    public class ResizablePanel : Decorator
    {
        /// <summary>Grab width of the handle. Wider than it looks, because a thin one is a pixel-hunt.</summary>
        private const float HandleGrabThickness = 8.0f;

        /// <summary>Painted width of the handle line.</summary>
        private const float HandleLineThickness = 2.0f;

        private float _size = 320.0f;
        private bool _dragging;
        private float _dragStartPointer;
        private float _dragStartSize;

        public ResizablePanel()
        {
            ClipToBounds = true;
        }

        /// <summary>Raised as the drag moves, so a host can persist the new size.</summary>
        public event EventHandler<float>? SizeChanged;

        /// <summary>Raised when the drag ends -- the moment worth writing to disk.</summary>
        public event EventHandler<float>? SizeCommitted;

        /// <summary>
        /// Which window edge the panel is anchored to. The handle goes on the opposite side, which is the
        /// one facing the content it is stealing room from.
        /// </summary>
        public Dock Edge { get; set; } = Dock.Right;

        /// <summary>Width for a left or right panel, height for a top or bottom one.</summary>
        public float Size
        {
            get => _size;
            set
            {
                var clamped = Math.Clamp(value, MinimumSize, MaximumSize);
                if (LayoutHelper.AreClose(_size, clamped))
                {
                    return;
                }

                _size = clamped;
                InvalidateMeasure();
                SizeChanged?.Invoke(this, clamped);
            }
        }

        public float MinimumSize { get; set; } = 180.0f;

        public float MaximumSize { get; set; } = 1200.0f;

        /// <summary>
        /// Painted across the whole panel, behind its content. Worth setting for anything sitting over a
        /// viewport: the handle gutter is part of the panel but not part of the content, so leaving it
        /// unpainted makes a solid column look translucent along its inner edge.
        /// </summary>
        public Vector4 Background { get; set; }

        /// <summary>
        /// Blurs whatever is behind this panel, in logical pixels; 0 leaves it sharp. Only visible through a
        /// translucent <see cref="Background"/>.
        /// </summary>
        public float BackdropBlur { get; set; }

        /// <summary>True while the handle is being dragged.</summary>
        public bool IsResizing => _dragging;

        private bool IsHorizontal => Edge is Dock.Left or Dock.Right;

        /// <summary>The handle's grab area, on the edge facing away from the window edge it is anchored to.</summary>
        public UiRect HandleRect
        {
            get
            {
                return Edge switch
                {
                    Dock.Right => new UiRect(Bounds.X, Bounds.Y, HandleGrabThickness, Bounds.Height),
                    Dock.Left => new UiRect(
                        Bounds.Right - HandleGrabThickness, Bounds.Y, HandleGrabThickness, Bounds.Height),
                    Dock.Bottom => new UiRect(Bounds.X, Bounds.Y, Bounds.Width, HandleGrabThickness),
                    _ => new UiRect(
                        Bounds.X, Bounds.Bottom - HandleGrabThickness, Bounds.Width, HandleGrabThickness)
                };
            }
        }

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inset = ContentInset;

            // The handle is carved out of the panel rather than added to it, so Size means what the user
            // sees the panel occupy -- dragging to 300 leaves 300 pixels of window covered.
            var content = new Vector2(
                MathF.Max(0.0f, (IsHorizontal ? _size : availableSize.X) - inset.Horizontal
                    - (IsHorizontal ? HandleGrabThickness : 0.0f)),
                MathF.Max(0.0f, (IsHorizontal ? availableSize.Y : _size) - inset.Vertical
                    - (IsHorizontal ? 0.0f : HandleGrabThickness)));

            Child?.Measure(content);

            return new Vector2(
                IsHorizontal ? _size : availableSize.X,
                IsHorizontal ? availableSize.Y : _size);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;

            // Inset away from the handle, so content never lands underneath a grab area.
            var area = Edge switch
            {
                Dock.Right => new UiRect(
                    content.X + HandleGrabThickness, content.Y,
                    MathF.Max(0.0f, content.Width - HandleGrabThickness), content.Height),
                Dock.Left => new UiRect(
                    content.X, content.Y,
                    MathF.Max(0.0f, content.Width - HandleGrabThickness), content.Height),
                Dock.Bottom => new UiRect(
                    content.X, content.Y + HandleGrabThickness,
                    content.Width, MathF.Max(0.0f, content.Height - HandleGrabThickness)),
                _ => new UiRect(
                    content.X, content.Y,
                    content.Width, MathF.Max(0.0f, content.Height - HandleGrabThickness))
            };

            Child?.Arrange(area);
            return finalSize;
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            if (BackdropBlur > 0.0f)
            {
                drawList.FillBackdrop(Bounds, Background, BackdropBlur, default);
            }
            else if (Background.W > 0.0f)
            {
                drawList.FillRect(Bounds, Background);
            }

            var handle = HandleRect;
            var active = _dragging || handle.Contains(Root?.Input.PointerPosition ?? new Vector2(-1.0f));

            // Centred in the grab area rather than filling it: the target is deliberately wider than the
            // line, and painting the whole of it would read as a thick border.
            var line = IsHorizontal
                ? new UiRect(
                    handle.Center.X - HandleLineThickness * 0.5f, handle.Y,
                    HandleLineThickness, handle.Height)
                : new UiRect(
                    handle.X, handle.Center.Y - HandleLineThickness * 0.5f,
                    handle.Width, HandleLineThickness);

            drawList.FillRect(line, active ? theme.Accent : theme.Border, default);
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override bool IsSelfHitTestVisible => true;

        /// <summary>
        /// Preview, so the handle wins over whatever is docked right up against it. The panel itself is
        /// otherwise transparent to input -- only the handle strip does anything.
        /// </summary>
        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !HandleRect.Contains(e.Position))
            {
                return;
            }

            _dragging = true;
            _dragStartPointer = IsHorizontal ? e.Position.X : e.Position.Y;
            _dragStartSize = _size;

            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            var travel = (IsHorizontal ? e.Position.X : e.Position.Y) - _dragStartPointer;

            // A right-anchored panel grows as the pointer moves left, so the sign follows the edge rather
            // than the axis. Measured from the press, not the previous move, so clamping cannot drift.
            var delta = Edge is Dock.Right or Dock.Bottom ? -travel : travel;

            Size = _dragStartSize + delta;
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
            SizeCommitted?.Invoke(this, _size);
            e.Handled = true;
        }

        public override UiCursor GetCursor(Vector2 point)
        {
            if (_dragging || HandleRect.Contains(point))
            {
                return IsHorizontal ? UiCursor.SizeWE : UiCursor.SizeNS;
            }

            return UiCursor.Arrow;
        }

        /// <summary>
        /// Only the handle is a hit target. Without this the panel would swallow clicks aimed at the
        /// viewport in whatever margin its content did not fill.
        /// </summary>
        public override UiElement? HitTest(Vector2 point)
        {
            var hit = base.HitTest(point);
            if (hit == null || !ReferenceEquals(hit, this))
            {
                return hit;
            }

            return HandleRect.Contains(point) ? this : null;
        }
    }
}
