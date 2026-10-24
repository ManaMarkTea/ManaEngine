namespace ManaUI
{
    /// <summary>
    /// Carries a drag on after the element that started it is gone: a floating panel promoted to a native window
    /// mid-drag closes its in-app window, and with it the pointer capture. This invisible element takes the capture
    /// over in the main window (the OS still sends it the pointer while the button is down) and reports each move
    /// and the release, as <see cref="DesktopWindowFrame.BeginExternalDrag"/> does for a native window.
    /// </summary>
    internal sealed class DockDragCarrier : UiElement
    {
        private Action<Vector2>? _moved;
        private Action<Vector2>? _released;

        public DockDragCarrier()
        {
            IsHitTestVisible = false;
        }

        public bool IsCarrying => _moved != null;

        /// <summary>Takes the pointer and reports where it goes (root coordinates) until the button comes up.</summary>
        public void Begin(Action<Vector2> moved, Action<Vector2> released)
        {
            _moved = moved;
            _released = released;
            CapturePointer();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (_moved != null)
            {
                _moved(e.Position);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_released == null)
            {
                return;
            }

            var released = _released;
            _moved = null;
            _released = null;
            ReleasePointerCapture();
            released(e.Position);
            e.Handled = true;
        }
    }
}
