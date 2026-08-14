namespace ManaUI
{
    /// <summary>
    /// A translucent copy of what is being dragged, following the cursor.
    /// <para>
    /// Dragging without one asks the user to remember what they picked up. That is fine over a list where
    /// the row is still visible and highlighted, and not fine at all when the drag leaves the panel
    /// entirely -- which is the case this exists for: a mesh dragged out of the import list and across the
    /// viewport has nothing on screen saying which mesh it is.
    /// </para>
    /// <para>
    /// Transparent to the pointer, and offset from it, so it never becomes what the drop is tested against.
    /// A ghost that could be hit would be under the cursor for the whole gesture.
    /// </para>
    /// </summary>
    public sealed class UiDragGhost
    {
        /// <summary>
        /// Down and right of the cursor, clear of it. Up and left would put the ghost over the thing being
        /// dragged towards, which is the one place it must not hide.
        /// </summary>
        private static readonly Vector2 CursorOffset = new(14.0f, 12.0f);

        private readonly UiRoot _root;

        private UiPopup? _popup;

        public UiDragGhost(UiRoot root)
        {
            _root = root;
        }

        public bool IsShowing => _popup != null;

        /// <summary>Starts following the cursor, carrying <paramref name="content"/>.</summary>
        public void Show(UiElement content, Vector2 position)
        {
            Hide();

            // Detached first: a ghost is usually built from something already in a tree, and an element
            // cannot have two parents.
            content.RemoveFromParent();

            _popup = new UiPopup
            {
                Placement = PopupPlacement.Absolute,
                PlacementPoint = position + CursorOffset,
                StaysOpen = true,
                IsHitTestVisible = false,
                Opacity = 0.85f,
                Child = content
            };

            _root.Popups.Show(_popup, null);
            _root.Popups.UpdatePlacements(_root.Size);
        }

        public void Move(Vector2 position)
        {
            if (_popup == null)
            {
                return;
            }

            _popup.PlacementPoint = position + CursorOffset;

            // Placement is resolved once when a popup opens, because nothing else moves after that. A ghost
            // is the exception, so it asks for it again.
            _root.Popups.UpdatePlacements(_root.Size);
        }

        public void Hide()
        {
            if (_popup == null)
            {
                return;
            }

            var popup = _popup;
            _popup = null;
            _root.Popups.Close(popup);
        }
    }
}
