namespace ManaUI
{
    /// <summary>
    /// Turns raw pointer and keyboard input into routed events on the element tree.
    /// <para>
    /// The host calls the Inject* methods with platform input; everything else -- hit testing, hover
    /// enter/exit chains, pointer capture, click counting and focus changes -- happens here, so no
    /// windowing library ever reaches the widgets.
    /// </para>
    /// </summary>
    public sealed class UiInputRouter
    {
        /// <summary>Movement beyond this many logical pixels between clicks breaks a double-click.</summary>
        private const float DoubleClickSlopPixels = 4.0f;

        private readonly UiRoot _root;
        private readonly List<UiElement> _route = new();
        private readonly List<UiElement> _previousHoverChain = new();
        private readonly List<UiElement> _hoverChain = new();

        private Vector2 _pointerPosition;
        private double _lastClickTime = double.NegativeInfinity;
        private Vector2 _lastClickPosition;
        private PointerButton _lastClickButton;
        private int _clickCount;

        public UiInputRouter(UiRoot root)
        {
            _root = root;
        }

        /// <summary>Current pointer position in logical root coordinates.</summary>
        public Vector2 PointerPosition => _pointerPosition;

        /// <summary>Deepest hit-testable element under the pointer, or null.</summary>
        public UiElement? HoveredElement { get; private set; }

        /// <summary>Element receiving all pointer input regardless of position, or null.</summary>
        public UiElement? PointerCaptureTarget { get; private set; }

        /// <summary>Maximum gap between two clicks that still counts as a double-click.</summary>
        public double DoubleClickSeconds { get; set; } = 0.4;

        /// <summary>
        /// The cursor the UI currently wants. During a capture it comes from the captured element, not
        /// whatever happens to be under the pointer -- otherwise a resize cursor would flicker back to an
        /// arrow the moment a drag left the window being resized.
        /// </summary>
        public UiCursor RequestedCursor
        {
            get
            {
                var target = PointerCaptureTarget ?? HoveredElement;
                return target?.GetCursor(_pointerPosition) ?? UiCursor.Arrow;
            }
        }

        /// <summary>
        /// True when the pointer is over UI that would consume a click. The host uses this to stop a click
        /// meant for a button from also placing a voxel in the scene behind it.
        /// </summary>
        public bool IsPointerOverUi => HoveredElement != null || PointerCaptureTarget != null;

        /// <summary>True when a focused element wants raw keystrokes, so the host should suppress shortcuts.</summary>
        public bool IsKeyboardCaptured => _root.FocusManager.Focused is { Focusable: true } focused
            && focused.WantsAllKeyboardInput;

        public void CapturePointer(UiElement element)
        {
            PointerCaptureTarget = element;
        }

        public void ReleasePointerCapture()
        {
            PointerCaptureTarget = null;

            // The pointer may have travelled somewhere else entirely during the drag, so hover has to be
            // recomputed rather than assumed unchanged.
            UpdateHover(_pointerPosition, UiModifiers.None);
        }

        // ----- injection -----------------------------------------------------------------------------

        public void InjectPointerMoved(Vector2 position, UiModifiers modifiers = UiModifiers.None)
        {
            _pointerPosition = position;
            UpdateHover(position, modifiers);

            var target = PointerCaptureTarget ?? HoveredElement;
            if (target == null)
            {
                return;
            }

            var args = new UiPointerEventArgs
            {
                Position = position,
                Modifiers = modifiers
            };

            Dispatch(target, UiRoutedEventKind.PointerMoved, args);
        }

        public void InjectPointerPressed(
            Vector2 position,
            PointerButton button,
            double timeSeconds,
            UiModifiers modifiers = UiModifiers.None)
        {
            _pointerPosition = position;
            UpdateHover(position, modifiers);

            var target = PointerCaptureTarget ?? HoveredElement;

            // A press on empty space drops focus, matching every desktop toolkit -- otherwise a text box
            // would keep swallowing keystrokes after the user clicked away from it.
            if (target == null)
            {
                _root.FocusManager.Focus(null);
                return;
            }

            UpdateClickCount(position, button, timeSeconds);

            var args = new UiPointerEventArgs
            {
                Position = position,
                Button = button,
                Modifiers = modifiers,
                ClickCount = _clickCount
            };

            Dispatch(target, UiRoutedEventKind.PointerPressed, args);

            if (args.Handled)
            {
                return;
            }

            // Nothing claimed the press, so focus the nearest focusable ancestor of whatever was hit; a
            // click on a button's label should still focus the button.
            _root.FocusManager.Focus(FindFocusableAncestor(target));
        }

        public void InjectPointerReleased(
            Vector2 position,
            PointerButton button,
            UiModifiers modifiers = UiModifiers.None)
        {
            _pointerPosition = position;

            var target = PointerCaptureTarget ?? HoveredElement;
            if (target == null)
            {
                UpdateHover(position, modifiers);
                return;
            }

            var args = new UiPointerEventArgs
            {
                Position = position,
                Button = button,
                Modifiers = modifiers,
                ClickCount = _clickCount
            };

            Dispatch(target, UiRoutedEventKind.PointerReleased, args);

            if (PointerCaptureTarget == null)
            {
                UpdateHover(position, modifiers);
            }
        }

        public void InjectPointerWheel(Vector2 delta, UiModifiers modifiers = UiModifiers.None)
        {
            var target = PointerCaptureTarget ?? HoveredElement;
            if (target == null)
            {
                return;
            }

            var args = new UiPointerWheelEventArgs
            {
                Position = _pointerPosition,
                Delta = delta,
                Modifiers = modifiers
            };

            Dispatch(target, UiRoutedEventKind.PointerWheel, args);
        }

        /// <summary>Returns true when the UI consumed the key, so the host should not treat it as a shortcut.</summary>
        public bool InjectKeyDown(UiKey key, UiModifiers modifiers = UiModifiers.None, bool isRepeat = false)
        {
            var focused = _root.FocusManager.Focused;

            // Tab is navigation unless the focused element asked for raw keys (a multi-line editor might).
            if (key == UiKey.Tab && (focused == null || !focused.WantsAllKeyboardInput))
            {
                return _root.FocusManager.MoveFocus(!modifiers.HasFlag(UiModifiers.Shift));
            }

            if (focused == null)
            {
                return false;
            }

            var args = new UiKeyEventArgs
            {
                Key = key,
                Modifiers = modifiers,
                IsRepeat = isRepeat
            };

            Dispatch(focused, UiRoutedEventKind.KeyDown, args);
            return args.Handled;
        }

        public bool InjectKeyUp(UiKey key, UiModifiers modifiers = UiModifiers.None)
        {
            var focused = _root.FocusManager.Focused;
            if (focused == null)
            {
                return false;
            }

            var args = new UiKeyEventArgs { Key = key, Modifiers = modifiers };
            Dispatch(focused, UiRoutedEventKind.KeyUp, args);
            return args.Handled;
        }

        public bool InjectTextInput(string text)
        {
            var focused = _root.FocusManager.Focused;
            if (focused == null || string.IsNullOrEmpty(text))
            {
                return false;
            }

            var args = new UiTextInputEventArgs { Text = text };
            Dispatch(focused, UiRoutedEventKind.TextInput, args);
            return args.Handled;
        }

        // ----- routing -------------------------------------------------------------------------------

        /// <summary>
        /// Tunnels root-to-target, then bubbles target-to-root, stopping either walk as soon as something
        /// marks the event handled.
        /// </summary>
        private void Dispatch(UiElement target, UiRoutedEventKind kind, UiEventArgs args)
        {
            args.Source = target;

            _route.Clear();
            for (var current = target; current != null; current = current.Parent)
            {
                _route.Add(current);
            }

            for (var i = _route.Count - 1; i >= 0; i--)
            {
                _route[i].RaiseRouted(kind, args, isPreview: true);
                if (args.Handled)
                {
                    return;
                }
            }

            foreach (var element in _route)
            {
                element.RaiseRouted(kind, args, isPreview: false);
                if (args.Handled)
                {
                    return;
                }
            }
        }

        // ----- hover ---------------------------------------------------------------------------------

        /// <summary>
        /// Recomputes the hovered element and fires enter/exit only for the parts of the ancestor chain
        /// that actually changed -- moving between two children of one panel must not exit the panel.
        /// </summary>
        private void UpdateHover(Vector2 position, UiModifiers modifiers)
        {
            // During a capture the hover chain is frozen; the captured element owns the pointer.
            if (PointerCaptureTarget != null)
            {
                return;
            }

            var hit = _root.HitTest(position);
            if (ReferenceEquals(hit, HoveredElement))
            {
                return;
            }

            _previousHoverChain.Clear();
            _previousHoverChain.AddRange(_hoverChain);

            _hoverChain.Clear();
            for (var current = hit; current != null; current = current.Parent)
            {
                _hoverChain.Add(current);
            }

            HoveredElement = hit;

            var args = new UiPointerEventArgs { Position = position, Modifiers = modifiers };

            foreach (var element in _previousHoverChain)
            {
                if (!_hoverChain.Contains(element))
                {
                    element.RaisePointerExited(args);
                }
            }

            foreach (var element in _hoverChain)
            {
                if (!_previousHoverChain.Contains(element))
                {
                    element.RaisePointerEntered(args);
                }
            }
        }

        /// <summary>Clears hover state, e.g. when the window loses the cursor.</summary>
        public void ClearHover()
        {
            if (PointerCaptureTarget != null || HoveredElement == null)
            {
                return;
            }

            var args = new UiPointerEventArgs { Position = _pointerPosition };
            foreach (var element in _hoverChain)
            {
                element.RaisePointerExited(args);
            }

            _hoverChain.Clear();
            _previousHoverChain.Clear();
            HoveredElement = null;
        }

        // ----- helpers -------------------------------------------------------------------------------

        private void UpdateClickCount(Vector2 position, PointerButton button, double timeSeconds)
        {
            var withinTime = timeSeconds - _lastClickTime <= DoubleClickSeconds;
            var withinSlop = Vector2.Distance(position, _lastClickPosition) <= DoubleClickSlopPixels;

            _clickCount = withinTime && withinSlop && button == _lastClickButton ? _clickCount + 1 : 1;
            _lastClickTime = timeSeconds;
            _lastClickPosition = position;
            _lastClickButton = button;
        }

        private static UiElement? FindFocusableAncestor(UiElement element)
        {
            for (var current = element; current != null; current = current.Parent)
            {
                if (current.Focusable && current.IsEffectivelyEnabled)
                {
                    return current;
                }
            }

            return null;
        }
    }
}
