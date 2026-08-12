namespace ManaUI
{
    /// <summary>
    /// Owns the open popups: their stacking, their placement, and dismissing them.
    /// </summary>
    public sealed class UiPopupManager
    {
        private readonly UiRoot _root;
        private readonly List<UiPopup> _open = new();

        public UiPopupManager(UiRoot root)
        {
            _root = root;
        }

        /// <summary>Open popups, oldest first. A submenu sits after the menu that opened it.</summary>
        public IReadOnlyList<UiPopup> Open => _open;

        public bool HasOpenPopups => _open.Count > 0;

        public UiPopup? Topmost => _open.Count > 0 ? _open[^1] : null;

        /// <summary>Opens a popup anchored to an element.</summary>
        public UiPopup Show(UiPopup popup, UiElement? placementTarget = null)
        {
            ArgumentNullException.ThrowIfNull(popup);

            if (_open.Contains(popup))
            {
                return popup;
            }

            if (placementTarget != null)
            {
                popup.PlacementTarget = placementTarget;
            }

            _open.Add(popup);
            _root.PopupLayer.Children.Add(popup);

            // Placed immediately so the first frame is not drawn at the origin before layout catches up.
            _root.UpdateLayout(_root.Size);
            return popup;
        }

        /// <summary>Opens a popup at an explicit point -- context menus.</summary>
        public UiPopup ShowAt(UiPopup popup, Vector2 point)
        {
            popup.Placement = PopupPlacement.Absolute;
            popup.PlacementPoint = point;
            return Show(popup);
        }

        public void Close(UiPopup popup)
        {
            if (_open.Remove(popup))
            {
                popup.Close();
            }
        }

        /// <summary>Closes every popup that does not opt out of light dismissal.</summary>
        public void CloseAll()
        {
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var popup = _open[i];
                if (popup.StaysOpen)
                {
                    continue;
                }

                _open.RemoveAt(i);
                popup.Close();
            }
        }

        /// <summary>
        /// Closes popups down to and including <paramref name="popup"/>, which is how a menu chain
        /// collapses when the pointer returns to a parent item.
        /// </summary>
        public void CloseFrom(UiPopup popup)
        {
            var index = _open.IndexOf(popup);
            if (index < 0)
            {
                return;
            }

            for (var i = _open.Count - 1; i >= index; i--)
            {
                var open = _open[i];
                _open.RemoveAt(i);
                open.Close();
            }
        }

        /// <summary>
        /// Closes every popup opened after <paramref name="popup"/>, leaving it open. This is how a menu
        /// collapses its submenus as the pointer moves down its own rows.
        /// </summary>
        public void CloseDeeperThan(UiPopup popup)
        {
            var index = _open.IndexOf(popup);
            if (index < 0)
            {
                return;
            }

            for (var i = _open.Count - 1; i > index; i--)
            {
                var open = _open[i];
                _open.RemoveAt(i);
                open.Close();
            }
        }

        /// <summary>
        /// Applies light dismissal for a pointer press.
        /// </summary>
        /// <returns>
        /// True when the press should be swallowed. Only happens when the user pressed the anchor of an
        /// open popup: without swallowing, the popup would close here and the anchor's own handler would
        /// immediately reopen it, so a dropdown could never be closed by clicking its own button. Any other
        /// outside press closes the popups and is allowed through, so dismissing a menu and clicking a
        /// button is one action rather than two.
        /// </returns>
        internal bool HandlePointerPressed(UiElement? hit)
        {
            if (_open.Count == 0)
            {
                return false;
            }

            // Inside a popup: leave it alone and let the press dispatch normally.
            foreach (var popup in _open)
            {
                if (popup.IsAncestorOf(hit))
                {
                    return false;
                }
            }

            var onAnchor = false;
            foreach (var popup in _open)
            {
                if (popup.PlacementTarget != null && popup.PlacementTarget.IsAncestorOf(hit))
                {
                    onAnchor = true;
                    break;
                }
            }

            CloseAll();
            return onAnchor;
        }

        /// <summary>Escape closes the topmost popup. Returns true when one was closed.</summary>
        internal bool HandleEscape()
        {
            var topmost = Topmost;
            if (topmost == null)
            {
                return false;
            }

            Close(topmost);
            return true;
        }

        /// <summary>
        /// Re-places every open popup. Run after arrange, because placement depends on the anchor's final
        /// bounds and on the popup's own measured size.
        /// </summary>
        internal void UpdatePlacements(Vector2 rootSize)
        {
            // Reverse iteration: a popup can close during placement if its anchor left the tree.
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var popup = _open[i];

                // An anchor that has been removed from the tree takes its popup with it, otherwise the
                // popup would hang around pointing at nothing.
                if (popup.PlacementTarget != null
                    && popup.Placement != PopupPlacement.Absolute
                    && popup.PlacementTarget.Root == null)
                {
                    _open.RemoveAt(i);
                    popup.Close();
                    continue;
                }

                popup.UpdatePlacement(rootSize);
            }
        }
    }
}
