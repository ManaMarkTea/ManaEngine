namespace ManaUI
{
    public enum ScrollBarVisibility
    {
        /// <summary>Shown only when the content overflows.</summary>
        Auto,

        /// <summary>Always shown, and always reserving its width.</summary>
        Visible,

        /// <summary>Scrollable, but with no visible bar.</summary>
        Hidden,

        /// <summary>Not scrollable on this axis; content is constrained to the viewport instead.</summary>
        Disabled
    }

    /// <summary>
    /// A clipped viewport over content larger than itself.
    /// <para>
    /// Scrolling works by arranging the child at a negative offset in absolute coordinates -- there is no
    /// transform in the pipeline, and none is needed: the draw list already culls anything the clip rejects,
    /// so a long list costs one rect test per offscreen row rather than a render walk.
    /// </para>
    /// <para>
    /// The scrollbars are drawn and hit-tested by this control directly rather than being child elements.
    /// They are chrome, not content: making them children would put them inside the scrolled area and into
    /// the tab order, both wrong.
    /// </para>
    /// </summary>
    public class ScrollViewer : Decorator
    {
        private const float MinimumThumbLength = 24.0f;

        private Vector2 _offset;
        private Vector2 _extent;
        private Vector2 _viewport;

        private bool _draggingVertical;
        private bool _draggingHorizontal;
        private float _dragStartPointer;
        private float _dragStartOffset;

        public ScrollViewer()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        /// <summary>Raised when the offset changes. Virtualizing content uses this to re-realize rows.</summary>
        public event EventHandler<Vector2>? ScrollChanged;

        public ScrollBarVisibility VerticalScrollBar { get; set; } = ScrollBarVisibility.Auto;
        public ScrollBarVisibility HorizontalScrollBar { get; set; } = ScrollBarVisibility.Disabled;

        /// <summary>Pixels scrolled per wheel notch.</summary>
        public float WheelStep { get; set; } = 48.0f;

        /// <summary>
        /// Whether a visible bar takes its width out of the content area rather than sitting over it. On by
        /// default: the bar is drawn on top and also swallows presses, so overlaid content is both
        /// unreadable and unclickable. Turn it off for a pane where the extra pixels matter more than the
        /// last few characters of a row.
        /// </summary>
        public bool ReserveScrollBarSpace
        {
            get => _reserveScrollBarSpace;
            set
            {
                if (_reserveScrollBarSpace == value)
                {
                    return;
                }

                _reserveScrollBarSpace = value;

                // Measure, not just visual: this changes how much width the content is given.
                InvalidateMeasure();
            }
        }

        private bool _reserveScrollBarSpace = true;

        /// <summary>Current scroll position, clamped to the scrollable range.</summary>
        public Vector2 Offset
        {
            get => _offset;
            set
            {
                var clamped = ClampOffset(value);
                if (LayoutHelper.AreClose(_offset, clamped))
                {
                    return;
                }

                _offset = clamped;

                // Arrange, not measure: the content's size has not changed, only where it sits.
                InvalidateArrange();
                ScrollChanged?.Invoke(this, clamped);
            }
        }

        /// <summary>Size of the content being scrolled.</summary>
        public Vector2 Extent => _extent;

        /// <summary>Size of the visible window onto the content.</summary>
        public Vector2 Viewport => _viewport;

        /// <summary>How far the content can scroll on each axis; zero when it fits.</summary>
        public Vector2 ScrollableSize => new Vector2(
            MathF.Max(0.0f, _extent.X - _viewport.X),
            MathF.Max(0.0f, _extent.Y - _viewport.Y));

        public bool CanScrollVertically => VerticalScrollBar != ScrollBarVisibility.Disabled && ScrollableSize.Y > 0.0f;
        public bool CanScrollHorizontally => HorizontalScrollBar != ScrollBarVisibility.Disabled && ScrollableSize.X > 0.0f;

        public void ScrollToTop() => Offset = new Vector2(_offset.X, 0.0f);
        public void ScrollToBottom() => Offset = new Vector2(_offset.X, ScrollableSize.Y);

        /// <summary>Scrolls the minimum distance needed to bring an element fully into view.</summary>
        public void ScrollIntoView(UiElement element)
        {
            if (element.Bounds.IsEmpty)
            {
                return;
            }

            var viewportRect = Bounds;
            var offset = _offset;

            if (element.Bounds.Top < viewportRect.Top)
            {
                offset.Y -= viewportRect.Top - element.Bounds.Top;
            }
            else if (element.Bounds.Bottom > viewportRect.Bottom)
            {
                offset.Y += element.Bounds.Bottom - viewportRect.Bottom;
            }

            if (element.Bounds.Left < viewportRect.Left)
            {
                offset.X -= viewportRect.Left - element.Bounds.Left;
            }
            else if (element.Bounds.Right > viewportRect.Right)
            {
                offset.X += element.Bounds.Right - viewportRect.Right;
            }

            Offset = offset;
        }

        // ----- layout ---------------------------------------------------------------------------------

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inset = ContentInset;
            var inner = LayoutHelper.Deflate(availableSize, inset);

            if (Child == null || Child.Visibility == Visibility.Collapsed)
            {
                _extent = Vector2.Zero;
                return inset.Size;
            }

            // Unconstrained on any scrollable axis -- that is what lets the content report how big it
            // really wants to be instead of squashing into the viewport.
            // Published before the child is measured, not only during arrange. Virtualizing content sizes
            // its realised window from the viewport, and on the first pass after a large expansion the
            // viewport would otherwise still be zero -- so it would materialise everything exactly once,
            // on the frame that can least afford it.
            if (float.IsFinite(inner.X) && float.IsFinite(inner.Y))
            {
                _viewport = inner;
            }

            var childConstraint = new Vector2(
                HorizontalScrollBar == ScrollBarVisibility.Disabled ? inner.X : LayoutHelper.Infinity,
                VerticalScrollBar == ScrollBarVisibility.Disabled ? inner.Y : LayoutHelper.Infinity);

            Child.Measure(childConstraint);
            _extent = Child.DesiredSize;

            // Measured twice when a bar will be shown, because the bar is drawn over the content and the
            // first pass had no way to know it was coming. Without this the last few characters of every
            // row sit underneath the thumb.
            //
            // Safe from oscillation on the vertical axis: narrowing content can only make it taller or
            // leave it alone, so a bar that was needed stays needed. The horizontal axis is not symmetric
            // that way, which is why only the vertical reservation feeds back into the child's width.
            var reserved = ReservedGutter(inner);
            if (reserved.X > 0.0f || reserved.Y > 0.0f)
            {
                _viewport = new Vector2(
                    MathF.Max(0.0f, _viewport.X - reserved.X),
                    MathF.Max(0.0f, _viewport.Y - reserved.Y));

                Child.Measure(new Vector2(
                    float.IsInfinity(childConstraint.X) ? childConstraint.X : MathF.Max(0.0f, childConstraint.X - reserved.X),
                    float.IsInfinity(childConstraint.Y) ? childConstraint.Y : MathF.Max(0.0f, childConstraint.Y - reserved.Y)));

                _extent = Child.DesiredSize;
            }

            // The viewport takes whatever it is offered rather than the content's size; a scroll viewer
            // that grew to fit its content would never scroll.
            return new Vector2(
                float.IsInfinity(availableSize.X) ? _extent.X + inset.Horizontal : availableSize.X,
                float.IsInfinity(availableSize.Y) ? _extent.Y + inset.Vertical : availableSize.Y);
        }

        /// <summary>
        /// How much room the visible bars take out of the content area.
        /// <para>
        /// Reserved rather than overlaid, because the bars are drawn on top of the content and content
        /// running underneath a thumb is unreadable -- and unclickable, since the viewer now claims presses
        /// over the bar. <see cref="ScrollBarVisibility.Hidden"/> reserves nothing, which is what makes it
        /// the setting for a pane that scrolls without showing anything.
        /// </para>
        /// </summary>
        private Vector2 ReservedGutter(Vector2 viewport)
        {
            if (!ReserveScrollBarSpace)
            {
                return Vector2.Zero;
            }

            // The bar plus a gap, so content stops short of it rather than flush against it.
            var thickness = (Root?.Theme.ScrollBarThickness ?? 10.0f) + (Root?.Theme.ScrollBarGap ?? 4.0f);

            var vertical = VerticalScrollBar switch
            {
                ScrollBarVisibility.Visible => thickness,
                ScrollBarVisibility.Auto when _extent.Y > viewport.Y => thickness,
                _ => 0.0f
            };

            var horizontal = HorizontalScrollBar switch
            {
                ScrollBarVisibility.Visible => thickness,
                ScrollBarVisibility.Auto when _extent.X > viewport.X => thickness,
                _ => 0.0f
            };

            return new Vector2(vertical, horizontal);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            _viewport = content.Size;

            if (Child == null || Child.Visibility == Visibility.Collapsed)
            {
                return finalSize;
            }

            // The same reservation the measure pass made, so the child is arranged into the area it was
            // measured for rather than back under the bar.
            var reserved = ReservedGutter(content.Size);
            _viewport = new Vector2(
                MathF.Max(0.0f, content.Width - reserved.X),
                MathF.Max(0.0f, content.Height - reserved.Y));

            // Re-clamped here because the viewport size is only known now, and a window resize can make a
            // previously valid offset overscroll.
            _offset = ClampOffset(_offset);

            var width = HorizontalScrollBar == ScrollBarVisibility.Disabled
                ? _viewport.X
                : MathF.Max(_viewport.X, _extent.X);

            var height = VerticalScrollBar == ScrollBarVisibility.Disabled
                ? _viewport.Y
                : MathF.Max(_viewport.Y, _extent.Y);

            Child.Arrange(new UiRect(content.X - _offset.X, content.Y - _offset.Y, width, height));
            return finalSize;
        }

        private Vector2 ClampOffset(Vector2 offset)
        {
            var scrollable = ScrollableSize;
            return new Vector2(
                Math.Clamp(offset.X, 0.0f, scrollable.X),
                Math.Clamp(offset.Y, 0.0f, scrollable.Y));
        }

        // ----- rendering ------------------------------------------------------------------------------

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            if (ShouldShowBar(VerticalScrollBar, ScrollableSize.Y))
            {
                DrawBar(drawList, theme, vertical: true);
            }

            if (ShouldShowBar(HorizontalScrollBar, ScrollableSize.X))
            {
                DrawBar(drawList, theme, vertical: false);
            }
        }

        private static bool ShouldShowBar(ScrollBarVisibility visibility, float scrollable)
        {
            return visibility switch
            {
                ScrollBarVisibility.Visible => true,
                ScrollBarVisibility.Auto => scrollable > 0.0f,
                _ => false
            };
        }

        private void DrawBar(ManaUiDrawList drawList, UiTheme theme, bool vertical)
        {
            var track = TrackRect(theme, vertical);
            var corners = new CornerRadius(theme.ScrollBarThickness * 0.5f);

            drawList.FillRect(track, theme.SurfaceAlt.WithAlpha(0.5f), corners);

            var thumb = ThumbRect(theme, vertical);
            if (thumb.IsEmpty)
            {
                return;
            }

            var dragging = vertical ? _draggingVertical : _draggingHorizontal;
            var color = dragging
                ? theme.Accent
                : IsPointerOver ? theme.BorderStrong : theme.Border;

            drawList.FillRect(thumb, color, corners);
        }

        private UiRect TrackRect(UiTheme theme, bool vertical)
        {
            var thickness = theme.ScrollBarThickness;

            return vertical
                ? new UiRect(Bounds.Right - thickness, Bounds.Y, thickness, Bounds.Height)
                : new UiRect(Bounds.X, Bounds.Bottom - thickness, Bounds.Width, thickness);
        }

        private UiRect ThumbRect(UiTheme theme, bool vertical)
        {
            var track = TrackRect(theme, vertical);
            var trackLength = vertical ? track.Height : track.Width;
            var viewportLength = vertical ? _viewport.Y : _viewport.X;
            var extentLength = vertical ? _extent.Y : _extent.X;
            var scrollable = vertical ? ScrollableSize.Y : ScrollableSize.X;

            if (extentLength <= 0.0f || trackLength <= 0.0f)
            {
                return UiRect.Empty;
            }

            // Floored at a usable size so a very long list still leaves something grabbable.
            var thumbLength = Math.Clamp(
                trackLength * (viewportLength / extentLength),
                MathF.Min(MinimumThumbLength, trackLength),
                trackLength);

            var progress = scrollable <= 0.0f
                ? 0.0f
                : (vertical ? _offset.Y : _offset.X) / scrollable;

            var position = (trackLength - thumbLength) * progress;

            return vertical
                ? new UiRect(track.X, track.Y + position, track.Width, thumbLength)
                : new UiRect(track.X + position, track.Y, thumbLength, track.Height);
        }

        // ----- input ----------------------------------------------------------------------------------

        protected override void OnPointerWheel(UiPointerWheelEventArgs e)
        {
            var scrollable = ScrollableSize;
            if (scrollable.Y <= 0.0f && scrollable.X <= 0.0f)
            {
                return;
            }

            var before = _offset;

            // Shift turns a vertical wheel into horizontal scrolling, and a pane that only scrolls
            // horizontally still responds to a plain wheel.
            if (e.Modifiers.HasFlag(UiModifiers.Shift) || (scrollable.Y <= 0.0f && scrollable.X > 0.0f))
            {
                Offset = new Vector2(_offset.X - e.Delta.Y * WheelStep, _offset.Y);
            }
            else
            {
                Offset = new Vector2(_offset.X, _offset.Y - e.Delta.Y * WheelStep);
            }

            // Only claimed if it actually moved, so a nested viewer at its limit hands the wheel to its
            // parent instead of swallowing it.
            if (!LayoutHelper.AreClose(before, _offset))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Preview, not bubble. The bar is drawn in the overlay pass on top of the content, but the content
        /// is what the hit test finds underneath it -- so on the way back up a tree row or a list item has
        /// already claimed the press and the thumb never sees it. Tunnelling means the viewer gets first
        /// refusal on any press that lands on a visible bar, and passes everything else straight through.
        /// </summary>
        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            var theme = Root?.Theme;
            if (theme == null || e.Button != PointerButton.Left)
            {
                return;
            }

            if (TryBeginDrag(theme, e.Position, vertical: true) || TryBeginDrag(theme, e.Position, vertical: false))
            {
                CapturePointer();
                e.Handled = true;
            }
        }

        /// <summary>
        /// An arrow over the bar, whatever the content underneath would have asked for -- a text caret over
        /// a scroll thumb is the same lie as the click going through to the row behind it.
        /// </summary>
        public override UiCursor GetCursor(Vector2 point)
        {
            var theme = Root?.Theme;
            if (theme != null && (IsOverBar(theme, point, vertical: true) || IsOverBar(theme, point, vertical: false)))
            {
                return UiCursor.Arrow;
            }

            return base.GetCursor(point);
        }

        private bool IsOverBar(UiTheme theme, Vector2 point, bool vertical)
        {
            return ShouldShowBar(
                       vertical ? VerticalScrollBar : HorizontalScrollBar,
                       vertical ? ScrollableSize.Y : ScrollableSize.X)
                   && TrackRect(theme, vertical).Contains(point);
        }

        /// <summary>
        /// Starts a thumb drag, or page-jumps when the track is clicked away from the thumb.
        /// </summary>
        private bool TryBeginDrag(UiTheme theme, Vector2 position, bool vertical)
        {
            if (!ShouldShowBar(vertical ? VerticalScrollBar : HorizontalScrollBar,
                    vertical ? ScrollableSize.Y : ScrollableSize.X))
            {
                return false;
            }

            if (!TrackRect(theme, vertical).Contains(position))
            {
                return false;
            }

            var thumb = ThumbRect(theme, vertical);

            if (thumb.Contains(position))
            {
                if (vertical)
                {
                    _draggingVertical = true;
                    _dragStartPointer = position.Y;
                    _dragStartOffset = _offset.Y;
                }
                else
                {
                    _draggingHorizontal = true;
                    _dragStartPointer = position.X;
                    _dragStartOffset = _offset.X;
                }

                InvalidateVisual();
                return true;
            }

            var page = vertical ? _viewport.Y : _viewport.X;
            var forward = vertical ? position.Y > thumb.Bottom : position.X > thumb.Right;
            var delta = forward ? page : -page;

            Offset = vertical
                ? new Vector2(_offset.X, _offset.Y + delta)
                : new Vector2(_offset.X + delta, _offset.Y);

            return true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_draggingVertical && !_draggingHorizontal)
            {
                return;
            }

            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var vertical = _draggingVertical;
            var track = TrackRect(theme, vertical);
            var thumb = ThumbRect(theme, vertical);

            var trackLength = vertical ? track.Height : track.Width;
            var thumbLength = vertical ? thumb.Height : thumb.Width;
            var travel = trackLength - thumbLength;

            if (travel <= 0.0f)
            {
                return;
            }

            // Pointer travel is scaled into content space by the ratio of scrollable range to thumb travel,
            // so the content keeps pace with the thumb exactly.
            var pointer = vertical ? e.Position.Y : e.Position.X;
            var scrollable = vertical ? ScrollableSize.Y : ScrollableSize.X;
            var moved = (pointer - _dragStartPointer) / travel * scrollable;

            Offset = vertical
                ? new Vector2(_offset.X, _dragStartOffset + moved)
                : new Vector2(_dragStartOffset + moved, _offset.Y);

            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_draggingVertical || _draggingHorizontal)
            {
                _draggingVertical = false;
                _draggingHorizontal = false;
                ReleasePointerCapture();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            var scrollable = ScrollableSize;
            if (scrollable.Y <= 0.0f)
            {
                return;
            }

            switch (e.Key)
            {
                case UiKey.Up:
                    Offset = new Vector2(_offset.X, _offset.Y - WheelStep);
                    e.Handled = true;
                    break;
                case UiKey.Down:
                    Offset = new Vector2(_offset.X, _offset.Y + WheelStep);
                    e.Handled = true;
                    break;
                case UiKey.PageUp:
                    Offset = new Vector2(_offset.X, _offset.Y - _viewport.Y);
                    e.Handled = true;
                    break;
                case UiKey.PageDown:
                    Offset = new Vector2(_offset.X, _offset.Y + _viewport.Y);
                    e.Handled = true;
                    break;
                case UiKey.Home:
                    ScrollToTop();
                    e.Handled = true;
                    break;
                case UiKey.End:
                    ScrollToBottom();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>A scroll viewer is a hit target across its whole area so the wheel works over empty space.</summary>
        protected override bool IsSelfHitTestVisible => true;
    }
}
