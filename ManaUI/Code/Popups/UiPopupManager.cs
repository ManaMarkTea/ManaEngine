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

            // A popup closed through its own Close() -- which is what a command handler naturally calls,
            // and what ComboBox and ColorField do -- would otherwise leave a detached element in this list
            // forever, so Topmost and light dismissal would work off something no longer on screen.
            popup.Closed += OnPopupClosed;

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
                popup.Closed -= OnPopupClosed;
                popup.Close();
            }
        }

        /// <summary>Deregisters a popup that closed itself, so the open list never holds a detached one.</summary>
        private void OnPopupClosed(object? sender, EventArgs e)
        {
            if (sender is not UiPopup popup)
            {
                return;
            }

            popup.Closed -= OnPopupClosed;
            _open.Remove(popup);

            if (ReferenceEquals(popup, _tooltipPopup))
            {
                _tooltipPopup = null;
            }
        }

        /// <summary>Closes every popup that does not opt out of light dismissal.</summary>
        public void CloseAll()
        {
            CancelPendingClose();

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

        private UiPopup? _pendingCloseAfter;
        private double _pendingCloseRemaining;

        /// <summary>
        /// Grace period before a submenu closes when the pointer moves onto a sibling row.
        /// <para>
        /// Without it, travelling diagonally from a parent row to its submenu closes the submenu on the
        /// way: the direct path crosses the sibling rows in between, and each crossing would dismiss it.
        /// The delay lets the pointer reach the submenu, which cancels the close.
        /// </para>
        /// </summary>
        public double SubmenuCloseDelaySeconds { get; set; } = 0.35;

        /// <summary>Schedules a deferred <see cref="CloseDeeperThan"/>, replacing any pending one.</summary>
        public void RequestCloseDeeperThan(UiPopup popup)
        {
            if (_open.IndexOf(popup) < 0 || _open.IndexOf(popup) >= _open.Count - 1)
            {
                return;
            }

            _pendingCloseAfter = popup;
            _pendingCloseRemaining = SubmenuCloseDelaySeconds;
        }

        /// <summary>Cancels a pending close -- called when the pointer reaches the submenu after all.</summary>
        public void CancelPendingClose()
        {
            _pendingCloseAfter = null;
            _pendingCloseRemaining = 0.0;
        }

        /// <summary>Advances the deferred-close timer. Call once per frame.</summary>
        public void Tick(double deltaSeconds)
        {
            TickTooltip(deltaSeconds);

            if (_pendingCloseAfter == null || deltaSeconds <= 0.0)
            {
                return;
            }

            _pendingCloseRemaining -= deltaSeconds;
            if (_pendingCloseRemaining > 0.0)
            {
                return;
            }

            var target = _pendingCloseAfter;
            CancelPendingClose();
            CloseDeeperThan(target);
        }

        // ----- tooltips -------------------------------------------------------------------------------

        /// <summary>How long the pointer must rest on something before its tooltip appears.</summary>
        public double TooltipDelaySeconds { get; set; } = 0.15;

        private UiElement? _tooltipCandidate;
        private UiPopup? _tooltipPopup;
        private double _tooltipHoverTime;
        private bool _tooltipSuppressed;

        /// <summary>Called by the router on every press, including one arriving mid-drag.</summary>
        internal void NotifyPointerPressed()
        {
            SuppressTooltip();
        }

        /// <summary>Hides any tooltip and keeps one from returning until the pointer moves elsewhere.</summary>
        private void SuppressTooltip()
        {
            _tooltipSuppressed = true;
            CloseTooltip();
        }

        /// <summary>
        /// Shows the hovered element's tooltip once the pointer has rested on it.
        /// <para>
        /// Driven from the tick rather than from the hover event, because a tooltip is about dwell time and
        /// an event only says the pointer arrived. The nearest ancestor carrying one wins, so wrapping a
        /// label in a border does not lose its tooltip.
        /// </para>
        /// </summary>
        private void TickTooltip(double deltaSeconds)
        {
            var hovered = NearestTooltip(_root.Input.HoveredElement);

            if (!ReferenceEquals(hovered, _tooltipCandidate))
            {
                _tooltipCandidate = hovered;
                _tooltipHoverTime = 0.0;
                _tooltipSuppressed = false;
                CloseTooltip();
                return;
            }

            if (hovered == null || _tooltipPopup != null || _tooltipSuppressed || deltaSeconds <= 0.0)
            {
                return;
            }

            _tooltipHoverTime += deltaSeconds;
            if (_tooltipHoverTime < TooltipDelaySeconds)
            {
                return;
            }

            ShowTooltip(hovered);
        }

        private UiElement? NearestTooltip(UiElement? element)
        {
            while (element != null)
            {
                if (!string.IsNullOrEmpty(element.Tooltip))
                {
                    return element;
                }

                element = element.Parent;
            }

            return null;
        }

        private void ShowTooltip(UiElement target)
        {
            var theme = _root.Theme;

            // StaysOpen, because a tooltip is not something to dismiss -- it goes away when the pointer
            // does, and closing it on the next click would fight whatever that click was for.
            _tooltipPopup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                StaysOpen = true,

                // Transparent to the pointer. A popup is hit-testable by default, and one that appeared
                // under the cursor would take the hover away from the thing being described -- which closes
                // the tooltip, which gives the hover back, forever -- and swallow the click as well.
                IsHitTestVisible = false,
                Child = new Border()
                    .Fill(theme.SurfaceRaised)
                    .Outline(theme.BorderStrong, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Shadow(10.0f)
                    .PaddingOf(8.0f, 5.0f)
                    .Content(new TextBlock()
                        .Text(target.Tooltip!)
                        .Foreground(theme.Text)
                        .FontSize(theme.FontSizeSmall)
                        .Wrap()
                        .MaxSize(420.0f, 200.0f))
            };

            Show(_tooltipPopup, target);
        }

        private void CloseTooltip()
        {
            if (_tooltipPopup == null)
            {
                return;
            }

            var popup = _tooltipPopup;
            _tooltipPopup = null;
            Close(popup);
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
            // A press means the pointer is being used rather than rested, so any tooltip has said its
            // piece. It stays away until the pointer moves to something else, instead of reappearing the
            // moment the dwell timer is next consulted.
            SuppressTooltip();

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

            // A popup that takes no input takes no part in dismissal either. Without this a tooltip could
            // swallow a click on the very control it describes: it is anchored to that control, and the
            // rule below deliberately eats presses that land on an open popup's anchor.
            var onAnchor = false;
            foreach (var popup in _open)
            {
                if (!popup.IsHitTestVisible)
                {
                    continue;
                }

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
