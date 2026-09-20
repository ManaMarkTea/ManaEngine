namespace ManaUI
{
    /// <summary>
    /// The content of a borderless native window: a dock host, a thin border, and the two things an OS title
    /// bar would have provided -- moving and resizing.
    /// <para>
    /// There is no title bar to grab, so the tab strip is the handle: a press on a strip that no tab claims
    /// starts a move. Positions are reported in screen coordinates because a window that is moving cannot
    /// measure its own drag in client coordinates -- they shift under the pointer with every step.
    /// </para>
    /// <para>
    /// The frame only reports. Whoever owns the windows moves them, since a drag can also be driven from a
    /// different window's frame when a tab is torn out of it.
    /// </para>
    /// </summary>
    public sealed class DesktopWindowFrame : Decorator
    {
        private const float BorderWidth = 1.0f;

        /// <summary>Grab band along an edge, reaching in from the window's outer edge.</summary>
        private const float EdgeBand = 5.0f;

        private const float CornerBand = 14.0f;

        private enum Mode
        {
            None,
            Move,
            External,
            Resize
        }

        private Mode _mode;
        private ResizeEdge _edge;
        private Vector2 _resizeStartPointer;
        private Vector2 _resizeStartOrigin;
        private Vector2 _resizeStartSize;

        public DesktopWindowFrame()
        {
            Dock = new DockHost();
            Child = Dock;
            ClipToBounds = true;
        }

        /// <summary>The window's own layout. Panels are docked into it like into any other host.</summary>
        public DockHost Dock { get; }

        /// <summary>Screen position of the client area's top-left; supplied by the window.</summary>
        public Func<Vector2> ScreenOrigin { get; set; } = () => Vector2.Zero;

        public float MinimumWidth { get; set; } = 200.0f;

        public float MinimumHeight { get; set; } = 120.0f;

        public bool CanResize { get; set; } = true;

        /// <summary>A press on the tab strip started moving the window. Carries the pointer in screen space.</summary>
        public event Action<Vector2>? DragBegan;

        public event Action<Vector2>? DragMoved;

        public event Action<Vector2>? DragEnded;

        /// <summary>An edge drag wants the window at this screen origin and size.</summary>
        public event Action<Vector2, Vector2>? ResizeRequested;

        public bool IsDragging => _mode != Mode.None;

        public Vector2 ToScreen(Vector2 clientPoint) => ScreenOrigin() + clientPoint;

        protected override Thickness ContentInset => new Thickness(BorderWidth) + Padding;

        protected override bool IsSelfHitTestVisible => true;

        /// <summary>
        /// Takes over a drag another element already started, so the pointer capture stays in this window
        /// while something else -- a window torn out of it -- follows the pointer.
        /// </summary>
        public void BeginExternalDrag()
        {
            _mode = Mode.External;
            CapturePointer();
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            drawList.DrawBorder(Bounds, theme.BorderStrong, BorderWidth, default);
        }

        // ----- input ----------------------------------------------------------------------------------

        public override UiCursor GetCursor(Vector2 point)
        {
            var edge = _mode == Mode.Resize ? _edge : EdgeAt(point);
            if (edge == ResizeEdge.None)
            {
                return _mode == Mode.Move ? UiCursor.SizeAll : UiCursor.Arrow;
            }

            var horizontal = edge.HasFlag(ResizeEdge.Left) || edge.HasFlag(ResizeEdge.Right);
            var vertical = edge.HasFlag(ResizeEdge.Top) || edge.HasFlag(ResizeEdge.Bottom);

            if (horizontal && vertical)
            {
                var forward = edge.HasFlag(ResizeEdge.Top) == edge.HasFlag(ResizeEdge.Left);
                return forward ? UiCursor.SizeNWSE : UiCursor.SizeNESW;
            }

            return horizontal ? UiCursor.SizeWE : UiCursor.SizeNS;
        }

        private ResizeEdge EdgeAt(Vector2 point)
        {
            if (!CanResize || !Bounds.Contains(point))
            {
                return ResizeEdge.None;
            }

            var fromLeft = point.X - Bounds.Left;
            var fromRight = Bounds.Right - point.X;
            var fromTop = point.Y - Bounds.Top;
            var fromBottom = Bounds.Bottom - point.Y;

            var edge = ResizeEdge.None;
            var horizontalBand = fromTop <= CornerBand || fromBottom <= CornerBand ? CornerBand : EdgeBand;
            var verticalBand = fromLeft <= CornerBand || fromRight <= CornerBand ? CornerBand : EdgeBand;

            if (fromLeft <= horizontalBand)
            {
                edge |= ResizeEdge.Left;
            }
            else if (fromRight <= horizontalBand)
            {
                edge |= ResizeEdge.Right;
            }

            if (fromTop <= verticalBand)
            {
                edge |= ResizeEdge.Top;
            }
            else if (fromBottom <= verticalBand)
            {
                edge |= ResizeEdge.Bottom;
            }

            return edge;
        }

        /// <summary>Preview, so an edge wins over the docked content sitting right up against it.</summary>
        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            var edge = EdgeAt(e.Position);
            if (edge == ResizeEdge.None)
            {
                return;
            }

            _mode = Mode.Resize;
            _edge = edge;
            _resizeStartPointer = ToScreen(e.Position);
            _resizeStartOrigin = ScreenOrigin();
            _resizeStartSize = Bounds.Size;
            CapturePointer();
            e.Handled = true;
        }

        /// <summary>
        /// Bubbled, so a tab or a tab's close button gets first refusal. What reaches here is a press on
        /// strip that no tab claimed -- or on a tab whose group is handing its press up because the window
        /// holds nothing else to drag.
        /// </summary>
        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || _mode != Mode.None)
            {
                return;
            }

            var group = Dock.GroupAt(e.Position);
            if (group == null || !group.TabStripRect.Contains(e.Position))
            {
                return;
            }

            _mode = Mode.Move;
            CapturePointer();
            DragBegan?.Invoke(ToScreen(e.Position));
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            switch (_mode)
            {
                case Mode.Move:
                case Mode.External:
                    DragMoved?.Invoke(ToScreen(e.Position));
                    e.Handled = true;
                    break;
                case Mode.Resize:
                    ApplyResize(ToScreen(e.Position) - _resizeStartPointer);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_mode == Mode.None)
            {
                return;
            }

            var wasDrag = _mode is Mode.Move or Mode.External;
            _mode = Mode.None;
            _edge = ResizeEdge.None;
            ReleasePointerCapture();

            if (wasDrag)
            {
                DragEnded?.Invoke(ToScreen(e.Position));
            }

            e.Handled = true;
        }

        /// <summary>
        /// Same rule as <see cref="UiWindow"/>: pulling a left or top edge moves the origin too, and when the
        /// minimum size refuses part of the pull the origin gives that part back, or the far edge creeps.
        /// </summary>
        private void ApplyResize(Vector2 delta)
        {
            var origin = _resizeStartOrigin;
            var size = _resizeStartSize;

            if (_edge.HasFlag(ResizeEdge.Right))
            {
                size.X += delta.X;
            }
            else if (_edge.HasFlag(ResizeEdge.Left))
            {
                size.X -= delta.X;
                origin.X += delta.X;
            }

            if (_edge.HasFlag(ResizeEdge.Bottom))
            {
                size.Y += delta.Y;
            }
            else if (_edge.HasFlag(ResizeEdge.Top))
            {
                size.Y -= delta.Y;
                origin.Y += delta.Y;
            }

            var clamped = new Vector2(MathF.Max(size.X, MinimumWidth), MathF.Max(size.Y, MinimumHeight));

            if (_edge.HasFlag(ResizeEdge.Left))
            {
                origin.X -= clamped.X - size.X;
            }

            if (_edge.HasFlag(ResizeEdge.Top))
            {
                origin.Y -= clamped.Y - size.Y;
            }

            ResizeRequested?.Invoke(origin, clamped);
        }
    }
}
