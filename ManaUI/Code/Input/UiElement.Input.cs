namespace ManaUI
{
    /// <summary>The routed events an element can observe. Used to drive one dispatch path instead of a dozen.</summary>
    public enum UiRoutedEventKind
    {
        PointerMoved,
        PointerPressed,
        PointerReleased,
        PointerWheel,
        KeyDown,
        KeyUp,
        TextInput
    }

    public abstract partial class UiElement
    {
        // ----- events --------------------------------------------------------------------------------
        //
        // Each routed event comes in two flavours. Preview ("tunnelling") fires root -> target, letting an
        // ancestor see -- and if it wants, swallow -- an event before the child does; this is how a
        // ScrollViewer or a modal layer intercepts input. The normal event then fires target -> root
        // ("bubbling"), which is what application code almost always wants.

        public event EventHandler<UiPointerEventArgs>? PreviewPointerPressed;
        public event EventHandler<UiPointerEventArgs>? PointerPressed;
        public event EventHandler<UiPointerEventArgs>? PreviewPointerReleased;
        public event EventHandler<UiPointerEventArgs>? PointerReleased;
        public event EventHandler<UiPointerEventArgs>? PointerMoved;
        public event EventHandler<UiPointerWheelEventArgs>? PointerWheel;

        /// <summary>Direct, not routed -- fires only on the element the pointer actually entered or left.</summary>
        public event EventHandler<UiPointerEventArgs>? PointerEntered;

        public event EventHandler<UiPointerEventArgs>? PointerExited;

        public event EventHandler<UiKeyEventArgs>? PreviewKeyDown;
        public event EventHandler<UiKeyEventArgs>? KeyDown;
        public event EventHandler<UiKeyEventArgs>? KeyUp;
        public event EventHandler<UiTextInputEventArgs>? TextInput;

        public event EventHandler? GotFocus;
        public event EventHandler? LostFocus;

        // ----- focus ---------------------------------------------------------------------------------

        /// <summary>Whether this element can take keyboard focus. Buttons and inputs set it true.</summary>
        public bool Focusable { get; set; }

        /// <summary>Order for Tab navigation; ties fall back to tree order. Negative removes it from the cycle.</summary>
        public int TabIndex { get; set; }

        /// <summary>
        /// True for controls that consume raw keystrokes, such as a text box. Tab reaches the control
        /// instead of moving focus, and the host suppresses its own shortcuts so typing "V" into a field
        /// does not also switch tools.
        /// </summary>
        public virtual bool WantsAllKeyboardInput => false;

        /// <summary>Requests keyboard focus. Returns false when detached, disabled or not focusable.</summary>
        public bool Focus()
        {
            return Root?.FocusManager.Focus(this) ?? false;
        }

        // ----- pointer helpers -----------------------------------------------------------------------

        /// <summary>
        /// Routes every subsequent pointer event here until released, regardless of what is under the
        /// cursor. Required for anything drag-based -- without it, dragging a slider thumb past the edge of
        /// its track would silently stop tracking.
        /// </summary>
        public void CapturePointer()
        {
            Root?.CapturePointer(this);
        }

        public void ReleasePointerCapture()
        {
            var root = Root;
            if (root != null && ReferenceEquals(root.PointerCaptureTarget, this))
            {
                root.ReleasePointerCapture();
            }
        }

        public bool HasPointerCapture => Root?.PointerCaptureTarget == this;

        // ----- overridable handlers ------------------------------------------------------------------

        protected virtual void OnPreviewPointerPressed(UiPointerEventArgs e) { }
        protected virtual void OnPointerPressed(UiPointerEventArgs e) { }
        protected virtual void OnPreviewPointerReleased(UiPointerEventArgs e) { }
        protected virtual void OnPointerReleased(UiPointerEventArgs e) { }
        protected virtual void OnPointerMoved(UiPointerEventArgs e) { }
        protected virtual void OnPointerWheel(UiPointerWheelEventArgs e) { }
        protected virtual void OnPointerEntered(UiPointerEventArgs e) { }
        protected virtual void OnPointerExited(UiPointerEventArgs e) { }
        protected virtual void OnPreviewKeyDown(UiKeyEventArgs e) { }
        protected virtual void OnKeyDown(UiKeyEventArgs e) { }
        protected virtual void OnKeyUp(UiKeyEventArgs e) { }
        protected virtual void OnTextInput(UiTextInputEventArgs e) { }
        protected virtual void OnGotFocus() { }
        protected virtual void OnLostFocus() { }

        // ----- dispatch ------------------------------------------------------------------------------

        /// <summary>
        /// Invokes the handler pair for one event on this element. The virtual runs before the public
        /// event, so a control's own behaviour is in place before application handlers observe it.
        /// </summary>
        internal void RaiseRouted(UiRoutedEventKind kind, UiEventArgs args, bool isPreview)
        {
            switch (kind)
            {
                case UiRoutedEventKind.PointerPressed:
                {
                    var e = (UiPointerEventArgs)args;
                    if (isPreview)
                    {
                        OnPreviewPointerPressed(e);
                        PreviewPointerPressed?.Invoke(this, e);
                    }
                    else
                    {
                        OnPointerPressed(e);
                        PointerPressed?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.PointerReleased:
                {
                    var e = (UiPointerEventArgs)args;
                    if (isPreview)
                    {
                        OnPreviewPointerReleased(e);
                        PreviewPointerReleased?.Invoke(this, e);
                    }
                    else
                    {
                        OnPointerReleased(e);
                        PointerReleased?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.PointerMoved:
                {
                    if (!isPreview)
                    {
                        var e = (UiPointerEventArgs)args;
                        OnPointerMoved(e);
                        PointerMoved?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.PointerWheel:
                {
                    if (!isPreview)
                    {
                        var e = (UiPointerWheelEventArgs)args;
                        OnPointerWheel(e);
                        PointerWheel?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.KeyDown:
                {
                    var e = (UiKeyEventArgs)args;
                    if (isPreview)
                    {
                        OnPreviewKeyDown(e);
                        PreviewKeyDown?.Invoke(this, e);
                    }
                    else
                    {
                        OnKeyDown(e);
                        KeyDown?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.KeyUp:
                {
                    if (!isPreview)
                    {
                        var e = (UiKeyEventArgs)args;
                        OnKeyUp(e);
                        KeyUp?.Invoke(this, e);
                    }

                    break;
                }
                case UiRoutedEventKind.TextInput:
                {
                    if (!isPreview)
                    {
                        var e = (UiTextInputEventArgs)args;
                        OnTextInput(e);
                        TextInput?.Invoke(this, e);
                    }

                    break;
                }
            }
        }

        internal void RaisePointerEntered(UiPointerEventArgs e)
        {
            IsPointerOver = true;
            OnPointerEntered(e);
            PointerEntered?.Invoke(this, e);
            InvalidateVisual();
        }

        internal void RaisePointerExited(UiPointerEventArgs e)
        {
            IsPointerOver = false;
            OnPointerExited(e);
            PointerExited?.Invoke(this, e);
            InvalidateVisual();
        }

        internal void RaiseGotFocus()
        {
            IsFocused = true;
            OnGotFocus();
            GotFocus?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        internal void RaiseLostFocus()
        {
            IsFocused = false;
            OnLostFocus();
            LostFocus?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }
}
