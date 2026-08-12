namespace ManaUI
{
    public enum PopupPlacement
    {
        /// <summary>Below the anchor, left edges aligned. Dropdowns.</summary>
        Bottom,

        /// <summary>Above the anchor, left edges aligned.</summary>
        Top,

        /// <summary>To the right, top edges aligned. Submenus.</summary>
        Right,

        /// <summary>To the left, top edges aligned.</summary>
        Left,

        /// <summary>At an explicit point, ignoring the anchor. Context menus.</summary>
        Absolute
    }

    /// <summary>
    /// Floating content positioned against an anchor element: dropdowns, menus, tooltips.
    /// <para>
    /// Lives in <see cref="UiRoot.PopupLayer"/> rather than beside its anchor in the tree. A dropdown
    /// nested under the control that opened it would be clipped by every scroll viewer and panel between
    /// them, and would fight for z-order with its own siblings.
    /// </para>
    /// </summary>
    public class UiPopup : Decorator
    {
        /// <summary>Gap between the anchor and the popup.</summary>
        private const float AnchorGap = 2.0f;

        public UiPopup()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        /// <summary>Raised after the popup leaves the popup layer.</summary>
        public event EventHandler? Closed;

        /// <summary>The element to position against. Null with <see cref="PopupPlacement.Absolute"/>.</summary>
        public UiElement? PlacementTarget { get; set; }

        public PopupPlacement Placement { get; set; } = PopupPlacement.Bottom;

        /// <summary>Used when <see cref="Placement"/> is Absolute.</summary>
        public Vector2 PlacementPoint { get; set; }

        /// <summary>
        /// True to keep the popup open when the pointer is pressed elsewhere. Menus and dropdowns want
        /// false; a docked palette that happens to float might want true.
        /// </summary>
        public bool StaysOpen { get; set; }

        /// <summary>Stretches the popup to its anchor's width, which is what a dropdown should do.</summary>
        public bool MatchAnchorWidth { get; set; }

        public bool IsOpen => Parent != null;

        public void Close()
        {
            if (Parent is Panel panel)
            {
                panel.Children.Remove(this);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Positions the popup, flipping to the opposite side when it would leave the screen and clamping
        /// so it never lands partly off-screen. Called each layout pass because the anchor can move.
        /// </summary>
        internal void UpdatePlacement(Vector2 rootSize)
        {
            var size = DesiredSize;
            var anchor = PlacementTarget?.Bounds ?? UiRect.Empty;

            if (MatchAnchorWidth && !anchor.IsEmpty)
            {
                Width = anchor.Width;
                size.X = anchor.Width;
            }

            var position = Placement switch
            {
                PopupPlacement.Absolute => PlacementPoint,
                PopupPlacement.Top => new Vector2(anchor.X, anchor.Y - size.Y - AnchorGap),
                PopupPlacement.Right => new Vector2(anchor.Right + AnchorGap, anchor.Y),
                PopupPlacement.Left => new Vector2(anchor.X - size.X - AnchorGap, anchor.Y),
                _ => new Vector2(anchor.X, anchor.Bottom + AnchorGap)
            };

            // Flip rather than clamp when there is no room on the preferred side: a dropdown pinned to the
            // bottom edge would otherwise cover the control that opened it.
            if (Placement == PopupPlacement.Bottom && position.Y + size.Y > rootSize.Y && anchor.Y - size.Y >= 0.0f)
            {
                position.Y = anchor.Y - size.Y - AnchorGap;
            }
            else if (Placement == PopupPlacement.Top && position.Y < 0.0f)
            {
                position.Y = anchor.Bottom + AnchorGap;
            }

            if (Placement == PopupPlacement.Right && position.X + size.X > rootSize.X && anchor.X - size.X >= 0.0f)
            {
                position.X = anchor.X - size.X - AnchorGap;
            }
            else if (Placement == PopupPlacement.Left && position.X < 0.0f)
            {
                position.X = anchor.Right + AnchorGap;
            }

            position.X = Math.Clamp(position.X, 0.0f, MathF.Max(0.0f, rootSize.X - size.X));
            position.Y = Math.Clamp(position.Y, 0.0f, MathF.Max(0.0f, rootSize.Y - size.Y));

            var slot = this.EnsureLayoutSlot();
            slot.CanvasLeft = position.X;
            slot.CanvasTop = position.Y;
        }

        protected override bool IsSelfHitTestVisible => true;
    }
}
