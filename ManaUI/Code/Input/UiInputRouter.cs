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

        /// <summary>
        /// Keys that auto-repeat when held.
        /// <para>
        /// Deliberately excludes anything that produces a character. The platform's own character callback
        /// already repeats, so repeating those here as well would insert every held letter twice. It also
        /// excludes keys where repeating means nothing (Home, End) or would be hostile (Enter, Escape, Tab).
        /// </para>
        /// </summary>
        private static readonly UiKey[] RepeatableKeys =
        {
            UiKey.Left, UiKey.Right, UiKey.Up, UiKey.Down,
            UiKey.Backspace, UiKey.Delete,
            UiKey.PageUp, UiKey.PageDown
        };

        private UiKey _heldKey = UiKey.None;
        private UiModifiers _heldModifiers;
        private double _heldElapsed;
        private double _nextRepeatAt;

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

        /// <summary>
        /// Deepest hit-testable element under the pointer, or null.
        /// <para>
        /// Validated on read for the same reason the capture target is: hover is only recomputed when the
        /// pointer moves, so an element destroyed while the pointer sits still would otherwise keep the
        /// router reporting that the UI is under the cursor -- and the layers underneath suppressed --
        /// until the user happened to move the mouse.
        /// </para>
        /// </summary>
        public UiElement? HoveredElement
        {
            get
            {
                if (_hoveredElement != null && !IsInTree(_hoveredElement))
                {
                    _hoveredElement = null;
                }

                return _hoveredElement;
            }

            private set => _hoveredElement = value;
        }

        private UiElement? _hoveredElement;

        /// <summary>
        /// Element receiving all pointer input regardless of position, or null.
        /// <para>
        /// Self-healing: an element can be torn out of the tree while it holds the capture -- a panel that
        /// rebuilds mid-drag does exactly that -- and a capture pointing at something detached is
        /// unrecoverable. Every subsequent event routes to an element nobody can see, the release never
        /// arrives to clear it, and because the router keeps reporting that the UI owns the pointer, the
        /// layers underneath stay suppressed for the rest of the session. So the target is validated on
        /// read rather than trusted.
        /// </para>
        /// </summary>
        public UiElement? PointerCaptureTarget
        {
            get
            {
                if (_captureTarget != null && !IsInTree(_captureTarget))
                {
                    _captureTarget = null;
                }

                return _captureTarget;
            }

            private set => _captureTarget = value;
        }

        private UiElement? _captureTarget;

        /// <summary>Whether an element is still connected to the root this router serves.</summary>
        private bool IsInTree(UiElement element)
        {
            var current = element;
            while (current != null)
            {
                if (ReferenceEquals(current, _root))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        /// <summary>Maximum gap between two clicks that still counts as a double-click.</summary>
        public double DoubleClickSeconds { get; set; } = 0.4;

        /// <summary>
        /// How long a key must be held before it starts repeating. Hosts should overwrite this with the
        /// operating system's own setting; the default is only what applies when nobody asks the OS.
        /// </summary>
        public double KeyRepeatDelaySeconds { get; set; } = 0.35;

        /// <summary>Interval between repeats once they start. Also expected to come from the OS.</summary>
        public double KeyRepeatIntervalSeconds { get; set; } = 0.03;

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

            // Light dismissal runs before dispatch so a press outside an open menu closes it. It only
            // swallows the press when it landed on the popup's own anchor, which is what stops a dropdown
            // button from closing and instantly reopening.
            if (PointerCaptureTarget == null && _root.Popups.HandlePointerPressed(target))
            {
                return;
            }

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

        /// <summary>
        /// Advances auto-repeat. Must be called once per frame; without it a held key fires exactly once.
        /// <para>
        /// Repeat is synthesised here because Silk.NET's GLFW backend maps only Press and Release onto its
        /// KeyDown/KeyUp events and discards the platform's repeat action entirely. Its character callback
        /// does repeat, which is why held letters used to work while Backspace and the arrows did not.
        /// </para>
        /// </summary>
        public void TickKeyRepeat(double deltaSeconds)
        {
            if (_heldKey == UiKey.None || deltaSeconds <= 0.0)
            {
                return;
            }

            // Nothing to repeat into. Also guards a key left stuck down when the window lost focus
            // mid-hold and its release never arrived.
            if (_root.FocusManager.Focused == null)
            {
                ClearHeldKey();
                return;
            }

            _heldElapsed += deltaSeconds;

            // A loop rather than a single fire, so a long frame still delivers the repeats it owed
            // instead of silently dropping them.
            var guard = 0;
            while (_heldElapsed >= _nextRepeatAt && guard++ < 32)
            {
                InjectKeyDown(_heldKey, _heldModifiers, isRepeat: true);
                _nextRepeatAt += KeyRepeatIntervalSeconds;
            }
        }

        private void ClearHeldKey()
        {
            _heldKey = UiKey.None;
            _heldModifiers = UiModifiers.None;
            _heldElapsed = 0.0;
            _nextRepeatAt = 0.0;
        }

        /// <summary>Drops any held key, e.g. when the window loses focus.</summary>
        public void ClearKeyboardState()
        {
            ClearHeldKey();
        }

        /// <summary>Returns true when the UI consumed the key, so the host should not treat it as a shortcut.</summary>
        public bool InjectKeyDown(UiKey key, UiModifiers modifiers = UiModifiers.None, bool isRepeat = false)
        {
            // Only a genuine press starts a repeat; the synthesised ones must not restart their own timer.
            if (!isRepeat)
            {
                if (Array.IndexOf(RepeatableKeys, key) >= 0)
                {
                    _heldKey = key;
                    _heldModifiers = modifiers;
                    _heldElapsed = 0.0;
                    _nextRepeatAt = KeyRepeatDelaySeconds;
                }
                else
                {
                    // Pressing a non-repeating key cancels the previous hold, matching the platform.
                    ClearHeldKey();
                }
            }

            var focused = _root.FocusManager.Focused;

            // Escape closes the topmost popup before anything else sees it, so a menu can be dismissed
            // even when focus is somewhere behind it.
            if (key == UiKey.Escape && !isRepeat && _root.Popups.HandleEscape())
            {
                return true;
            }

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
            if (_heldKey == key)
            {
                ClearHeldKey();
            }

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
