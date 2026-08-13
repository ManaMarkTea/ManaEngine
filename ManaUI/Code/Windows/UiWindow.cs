namespace ManaUI
{
    /// <summary>Which edge or corner a resize drag is pulling.</summary>
    [Flags]
    internal enum ResizeEdge
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Top = 1 << 2,
        Bottom = 1 << 3
    }

    /// <summary>
    /// A floating window: title bar, close button, drag to move, resize from any edge or corner.
    /// <para>
    /// Lives in <see cref="UiRoot.FloatingLayer"/>, which is a Canvas, so position is just the canvas
    /// offsets and stacking is ZIndex. Keeping windows in their own layer rather than in the content tree
    /// is what stops them fighting docked panels for z-order.
    /// </para>
    /// </summary>
    public class UiWindow : Decorator
    {
        /// <summary>Grab margin along an edge. Generous on purpose -- a 1px target is unusable.</summary>
        private const float ResizeMargin = 9.0f;

        /// <summary>
        /// Grab margin near a corner, where both axes are live. Wider than the edges because a corner is
        /// the handle people actually reach for, and the plain edge band made it a pixel-hunt.
        /// </summary>
        private const float CornerResizeMargin = 20.0f;

        /// <summary>
        /// Grab margin along the top edge, which is the one competing with drag-to-move. Deliberately
        /// slimmer than the others: nobody resizes a window upwards nearly as often as they move it, and a
        /// wide band there turns the top of the title bar into a trap.
        /// </summary>
        private const float TitleBarResizeMargin = 5.0f;

        /// <summary>Size of one dot in the bottom-right grip, and the gap between dot centres.</summary>
        private const float GripDotSize = 2.5f;
        private const float GripDotPitch = 4.5f;

        private string _title = string.Empty;
        private bool _dragging;
        private ResizeEdge _resizing;
        private Vector2 _dragPointerStart;
        private Vector2 _dragOriginStart;
        private Vector2 _dragSizeStart;

        public UiWindow()
        {
            MinWidth = 120.0f;
            MinHeight = 80.0f;
            Width = 320.0f;
            Height = 240.0f;
            Focusable = true;
            ClipToBounds = true;
        }

        public UiWindow(string title)
            : this()
        {
            _title = title;
        }

        /// <summary>Raised after the window has removed itself from its layer.</summary>
        public event EventHandler? Closed;

        /// <summary>Raised when the title bar drag starts, carrying the pointer position. Docking hooks this.</summary>
        public event EventHandler<Vector2>? DragStarted;

        public event EventHandler<Vector2>? Dragging;
        public event EventHandler<Vector2>? DragEnded;

        public string Title
        {
            get => _title;
            set => SetVisualProperty(ref _title, value ?? string.Empty);
        }

        public bool CanClose { get; set; } = true;
        public bool CanResize { get; set; } = true;
        public bool CanMove { get; set; } = true;

        /// <summary>Height of the title bar; falls back to the theme's control height.</summary>
        public float TitleBarHeight { get; set; } = float.NaN;

        private float ResolvedTitleBarHeight =>
            LayoutHelper.IsAuto(TitleBarHeight) ? (Root?.Theme.ControlHeight ?? 26.0f) + 4.0f : TitleBarHeight;

        protected override Thickness ContentInset =>
            new Thickness(1.0f, ResolvedTitleBarHeight, 1.0f, 1.0f) + Padding;

        // ----- placement -----------------------------------------------------------------------------

        /// <summary>Top-left corner within the floating layer.</summary>
        public Vector2 Position
        {
            get
            {
                var slot = LayoutSlotOrNull;
                return new Vector2(
                    slot == null || float.IsNaN(slot.CanvasLeft) ? 0.0f : slot.CanvasLeft,
                    slot == null || float.IsNaN(slot.CanvasTop) ? 0.0f : slot.CanvasTop);
            }
            set
            {
                var slot = this.EnsureLayoutSlot();
                slot.CanvasLeft = value.X;
                slot.CanvasTop = value.Y;
                Parent?.InvalidateArrange();
            }
        }

        /// <summary>Raises the window above its siblings.</summary>
        public void BringToFront()
        {
            if (Parent is not Panel panel)
            {
                return;
            }

            var highest = 0;
            foreach (var sibling in panel.Children)
            {
                highest = Math.Max(highest, sibling.ZIndex);
            }

            if (ZIndex <= highest)
            {
                this.EnsureLayoutSlot().ZIndex = highest + 1;
                panel.Children.InvalidatePaintOrder();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Takes over a drag already in progress elsewhere. Used when a tab is pulled out of a dock group:
        /// the group releases capture mid-gesture and the new floating window has to pick it up without the
        /// user releasing the button.
        /// </summary>
        internal void BeginExternalDrag(Vector2 pointer)
        {
            _dragging = true;
            _dragPointerStart = pointer;
            _dragOriginStart = Position;
            CapturePointer();
            DragStarted?.Invoke(this, pointer);
        }

        public void Close()
        {
            if (Parent is Panel panel)
            {
                panel.Children.Remove(this);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }

        // ----- rendering -----------------------------------------------------------------------------

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var corners = theme.CornerRadiusLarge;

            drawList.DrawShadow(Bounds, new Vector4(0.0f, 0.0f, 0.0f, 0.4f), 20.0f, corners);
            drawList.DrawBox(Bounds, theme.Surface, theme.BorderStrong, 1.0f, corners);

            var titleBar = TitleBarRect();

            // Only the top corners are rounded, so the bar meets the window's sides flush.
            drawList.FillRect(
                titleBar,
                IsFocusedWindow ? theme.SurfaceRaised : theme.SurfaceAlt,
                CornerRadius.Top(corners.TopLeft));

            drawList.FillRect(
                new UiRect(titleBar.X, titleBar.Bottom - 1.0f, titleBar.Width, 1.0f),
                theme.Border);

            RenderTitleText(drawList, theme, titleBar);

            if (CanClose)
            {
                RenderCloseButton(drawList, theme);
            }

            if (CanResize)
            {
                RenderResizeGrip(drawList, theme);
            }
        }

        /// <summary>
        /// The stepped triangle of dots every desktop toolkit uses for a resize corner. Without it the grab
        /// area is invisible and people hunt for the edge; with it the corner reads as a handle.
        /// </summary>
        private void RenderResizeGrip(ManaUiDrawList drawList, UiTheme theme)
        {
            var grip = ResizeGripRect();
            var hovered = grip.Contains(Root?.Input.PointerPosition ?? new Vector2(-1.0f))
                || _resizing != ResizeEdge.None;

            var color = hovered ? theme.Text : theme.TextDisabled;
            var corners = new CornerRadius(GripDotSize * 0.5f);

            // Anchored off the inside corner and stepped inwards, so the dots follow the diagonal and the
            // outermost one sits clear of the window's rounded border.
            var anchor = new Vector2(
                Bounds.Right - 4.0f - GripDotSize,
                Bounds.Bottom - 4.0f - GripDotSize);

            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column <= row; column++)
                {
                    var dot = new UiRect(
                        anchor.X - column * GripDotPitch,
                        anchor.Y - (row - column) * GripDotPitch,
                        GripDotSize,
                        GripDotSize);

                    drawList.FillRect(dot, color, corners);
                }
            }
        }

        private void RenderTitleText(ManaUiDrawList drawList, UiTheme theme, UiRect titleBar)
        {
            var root = Root;
            if (root == null || _title.Length == 0)
            {
                return;
            }

            var font = root.Fonts.Get(theme.FontFamilyMedium, theme.FontSize, root.Scale);
            var layout = new TextLayout
            {
                Font = font,
                Text = _title,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxWidth = MathF.Max(0.0f, titleBar.Width - 12.0f - (CanClose ? titleBar.Height : 0.0f))
            };

            var textArea = new UiRect(
                titleBar.X + 10.0f,
                titleBar.Y,
                layout.MaxWidth,
                titleBar.Height);

            layout.Render(drawList, textArea, IsFocusedWindow ? theme.Text : theme.TextMuted,
                VerticalAlignment.Center);
        }

        private void RenderCloseButton(ManaUiDrawList drawList, UiTheme theme)
        {
            var button = CloseButtonRect();
            var hovered = button.Contains(Root?.Input.PointerPosition ?? new Vector2(-1.0f));

            if (hovered)
            {
                drawList.FillRect(button, theme.Danger, new CornerRadius(4.0f));
            }

            // An X needs diagonals, which the axis-aligned box primitive cannot draw, so the glyph path
            // does it -- same approach as the checkbox tick.
            var root = Root;
            if (root == null)
            {
                return;
            }

            var font = root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale);
            var glyph = font.GetGlyph('×');
            var color = hovered ? theme.TextOnAccent : theme.TextMuted;

            if (glyph.IsDrawable)
            {
                drawList.DrawGlyph(
                    new UiRect(
                        font.SnapToPixel(button.Center.X - glyph.Size.X * 0.5f),
                        font.SnapToPixel(button.Center.Y - glyph.Size.Y * 0.5f),
                        glyph.Size.X,
                        glyph.Size.Y),
                    glyph.AtlasUv,
                    color);
            }
            else
            {
                // A font with no multiplication sign is unlikely but no longer silent: .notdef is detected
                // now, so this actually runs rather than drawing a tofu box and calling it an X.
                UiIcons.Cross(drawList, button.Deflate(new Thickness(5.0f)), color);
            }
        }

        private bool IsFocusedWindow
        {
            get
            {
                var focused = Root?.FocusManager.Focused;
                return focused != null && IsAncestorOf(focused);
            }
        }

        private UiRect TitleBarRect()
        {
            return new UiRect(Bounds.X, Bounds.Y, Bounds.Width, ResolvedTitleBarHeight);
        }

        private UiRect CloseButtonRect()
        {
            var bar = TitleBarRect();
            var size = bar.Height - 8.0f;
            return new UiRect(bar.Right - size - 4.0f, bar.Y + 4.0f, size, size);
        }

        // ----- input ---------------------------------------------------------------------------------

        public override UiCursor GetCursor(Vector2 point)
        {
            if (_resizing != ResizeEdge.None)
            {
                return CursorForEdge(_resizing);
            }

            if (_dragging)
            {
                return UiCursor.SizeAll;
            }

            if (CanClose && CloseButtonRect().Contains(point))
            {
                return UiCursor.Arrow;
            }

            var edge = EdgeAt(point);
            return edge != ResizeEdge.None ? CursorForEdge(edge) : UiCursor.Arrow;
        }

        private static UiCursor CursorForEdge(ResizeEdge edge)
        {
            var horizontal = edge.HasFlag(ResizeEdge.Left) || edge.HasFlag(ResizeEdge.Right);
            var vertical = edge.HasFlag(ResizeEdge.Top) || edge.HasFlag(ResizeEdge.Bottom);

            if (horizontal && vertical)
            {
                var topLeft = edge.HasFlag(ResizeEdge.Top) && edge.HasFlag(ResizeEdge.Left);
                var bottomRight = edge.HasFlag(ResizeEdge.Bottom) && edge.HasFlag(ResizeEdge.Right);
                return topLeft || bottomRight ? UiCursor.SizeNWSE : UiCursor.SizeNESW;
            }

            return horizontal ? UiCursor.SizeWE : UiCursor.SizeNS;
        }

        /// <summary>Which resize edge, if any, a point falls in.</summary>
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

            // The corner band is checked first and against the wider margin, so approaching a corner from
            // along an edge widens into the two-axis grab rather than staying a one-axis one.
            var nearLeft = fromLeft <= CornerResizeMargin;
            var nearRight = fromRight <= CornerResizeMargin;
            var nearTop = fromTop <= CornerResizeMargin;
            var nearBottom = fromBottom <= CornerResizeMargin;

            // Not over the title bar. Widening the corner there would eat into the drag-to-move area and
            // swallow the close button, which sits inside the top-right corner band.
            var overTitleBar = point.Y < Bounds.Y + ResolvedTitleBarHeight;

            if (!overTitleBar && (nearLeft || nearRight) && (nearTop || nearBottom))
            {
                var corner = ResizeEdge.None;
                corner |= nearLeft && fromLeft <= fromRight ? ResizeEdge.Left : ResizeEdge.Right;
                corner |= nearTop && fromTop <= fromBottom ? ResizeEdge.Top : ResizeEdge.Bottom;
                return corner;
            }

            var edge = ResizeEdge.None;

            if (fromLeft <= ResizeMargin)
            {
                edge |= ResizeEdge.Left;
            }
            else if (fromRight <= ResizeMargin)
            {
                edge |= ResizeEdge.Right;
            }

            if (fromTop <= (overTitleBar ? TitleBarResizeMargin : ResizeMargin))
            {
                edge |= ResizeEdge.Top;
            }
            else if (fromBottom <= ResizeMargin)
            {
                edge |= ResizeEdge.Bottom;
            }

            return edge;
        }

        /// <summary>The bottom-right corner's grab square -- the region the visible grip advertises.</summary>
        public UiRect ResizeGripRect()
        {
            return new UiRect(
                Bounds.Right - CornerResizeMargin,
                Bounds.Bottom - CornerResizeMargin,
                CornerResizeMargin,
                CornerResizeMargin);
        }

        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            // Preview, not bubble: a click anywhere in the window raises it, and must do so before the
            // content has a chance to mark the event handled.
            if (e.Button == PointerButton.Left)
            {
                BringToFront();
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            if (CanClose && CloseButtonRect().Contains(e.Position))
            {
                Close();
                e.Handled = true;
                return;
            }

            var edge = EdgeAt(e.Position);
            if (edge != ResizeEdge.None)
            {
                _resizing = edge;
                _dragPointerStart = e.Position;
                _dragOriginStart = Position;
                _dragSizeStart = Bounds.Size;
                CapturePointer();
                e.Handled = true;
                return;
            }

            if (CanMove && TitleBarRect().Contains(e.Position))
            {
                _dragging = true;
                _dragPointerStart = e.Position;
                _dragOriginStart = Position;
                CapturePointer();
                DragStarted?.Invoke(this, e.Position);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_resizing != ResizeEdge.None)
            {
                ApplyResize(e.Position - _dragPointerStart);
                e.Handled = true;
                return;
            }

            if (_dragging)
            {
                Position = _dragOriginStart + (e.Position - _dragPointerStart);
                Dragging?.Invoke(this, e.Position);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_resizing == ResizeEdge.None && !_dragging)
            {
                return;
            }

            var wasDragging = _dragging;

            _resizing = ResizeEdge.None;
            _dragging = false;
            ReleasePointerCapture();

            if (wasDragging)
            {
                DragEnded?.Invoke(this, e.Position);
            }

            e.Handled = true;
        }

        /// <summary>
        /// Applies a resize delta. Dragging a left or top edge moves the origin as well as changing the
        /// size, and the min-size clamp has to move the origin back by the amount it refused -- otherwise
        /// the opposite edge creeps as the user keeps pulling past the limit.
        /// </summary>
        private void ApplyResize(Vector2 delta)
        {
            var origin = _dragOriginStart;
            var size = _dragSizeStart;

            if (_resizing.HasFlag(ResizeEdge.Right))
            {
                size.X = _dragSizeStart.X + delta.X;
            }
            else if (_resizing.HasFlag(ResizeEdge.Left))
            {
                size.X = _dragSizeStart.X - delta.X;
                origin.X = _dragOriginStart.X + delta.X;
            }

            if (_resizing.HasFlag(ResizeEdge.Bottom))
            {
                size.Y = _dragSizeStart.Y + delta.Y;
            }
            else if (_resizing.HasFlag(ResizeEdge.Top))
            {
                size.Y = _dragSizeStart.Y - delta.Y;
                origin.Y = _dragOriginStart.Y + delta.Y;
            }

            var clampedX = Math.Clamp(size.X, MinWidth, MaxWidth);
            var clampedY = Math.Clamp(size.Y, MinHeight, MaxHeight);

            if (_resizing.HasFlag(ResizeEdge.Left))
            {
                origin.X -= clampedX - size.X;
            }

            if (_resizing.HasFlag(ResizeEdge.Top))
            {
                origin.Y -= clampedY - size.Y;
            }

            Width = clampedX;
            Height = clampedY;
            Position = origin;
        }

        /// <summary>A window is a hit target across its whole area, chrome and background alike.</summary>
        protected override bool IsSelfHitTestVisible => true;
    }
}
