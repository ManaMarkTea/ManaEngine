namespace ManaUI
{
    /// <summary>
    /// Tracks which element has keyboard focus and implements Tab navigation.
    /// </summary>
    public sealed class UiFocusManager
    {
        private readonly UiRoot _root;

        public UiFocusManager(UiRoot root)
        {
            _root = root;
        }

        public UiElement? Focused { get; private set; }

        /// <summary>
        /// Moves focus. Passing null clears it. Returns false when the target cannot take focus, which
        /// leaves the previous holder untouched rather than blanking it.
        /// </summary>
        public bool Focus(UiElement? element)
        {
            if (element != null && !CanFocus(element))
            {
                return false;
            }

            if (ReferenceEquals(Focused, element))
            {
                return true;
            }

            var previous = Focused;
            Focused = element;

            previous?.RaiseLostFocus();
            element?.RaiseGotFocus();
            return true;
        }

        /// <summary>Drops focus if the holder was removed from the tree or made unfocusable.</summary>
        public void ValidateFocus()
        {
            if (Focused != null && !CanFocus(Focused))
            {
                Focus(null);
            }
        }

        /// <summary>Moves to the next (or with Shift, previous) focusable element, wrapping at the ends.</summary>
        public bool MoveFocus(bool forward)
        {
            var candidates = CollectFocusable();
            if (candidates.Count == 0)
            {
                return false;
            }

            var index = Focused == null ? -1 : candidates.IndexOf(Focused);

            int next;
            if (index < 0)
            {
                next = forward ? 0 : candidates.Count - 1;
            }
            else
            {
                next = (index + (forward ? 1 : -1) + candidates.Count) % candidates.Count;
            }

            return Focus(candidates[next]);
        }

        private static bool CanFocus(UiElement element)
        {
            if (!element.Focusable || !element.IsEffectivelyEnabled)
            {
                return false;
            }

            // Detached or hidden elements must never hold focus, or keystrokes would vanish into
            // something the user cannot see.
            for (var current = element; current != null; current = current.Parent)
            {
                if (current.Visibility != Visibility.Visible)
                {
                    return false;
                }

                if (current is UiRoot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Focusable elements in tab order: TabIndex ascending, then tree order. Negative TabIndex opts out.
        /// </summary>
        private List<UiElement> CollectFocusable()
        {
            var found = new List<(UiElement Element, int Order)>();
            var order = 0;

            void Walk(UiElement element)
            {
                if (element.Visibility != Visibility.Visible || !element.IsEnabled)
                {
                    return;
                }

                if (element.Focusable && element.TabIndex >= 0)
                {
                    found.Add((element, order));
                }

                order++;

                var count = element.VisualChildCount;
                for (var i = 0; i < count; i++)
                {
                    Walk(element.GetVisualChild(i));
                }
            }

            Walk(_root);

            return found
                .OrderBy(entry => entry.Element.TabIndex)
                .ThenBy(entry => entry.Order)
                .Select(entry => entry.Element)
                .ToList();
        }
    }
}
