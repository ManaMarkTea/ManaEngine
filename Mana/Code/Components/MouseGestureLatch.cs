namespace Mana
{
    /// <summary>
    /// Keeps a held-button drag going once it starts, regardless of what the cursor crosses on the way.
    /// <para>
    /// Without this, gating a drag on "does the UI want the mouse right now" re-reads that answer every
    /// frame -- and a camera orbit or pan sweeps the cursor across whatever the docked columns are covering
    /// as a normal part of looking around. Each frame the cursor happens to be over one, the gate reads
    /// true, the gesture's input for that frame gets zeroed, and the camera reads as having briefly let go
    /// mid-drag. An OS-level window drag is not cancelled by the cursor crossing another window; this
    /// isn't either. The gate that should matter is only whether the UI wanted the mouse at the moment the
    /// button first went down.
    /// </para>
    /// </summary>
    public struct MouseGestureLatch
    {
        private bool _active;
        private bool _wasDown;

        /// <summary>
        /// Advances the latch by one frame and returns whether the gesture counts as active now.
        /// <para>
        /// False whenever the button is up, which is also what resets the latch for the next press. While
        /// held, the decision is made exactly once -- on the frame the button first goes down -- and holds
        /// for the rest of the press regardless of what the UI reports afterwards. A press that begins while
        /// the UI wants the mouse never starts a drag even if the cursor later moves off whatever it was
        /// over, the same way a UI control keeps a capture it already has wherever the cursor goes next; and
        /// a press that begins clear of the UI keeps going even if the cursor later sweeps across it.
        /// </para>
        /// </summary>
        public bool Update(bool buttonDown, bool uiCapturesMouse)
        {
            if (!buttonDown)
            {
                _active = false;
                _wasDown = false;
                return false;
            }

            if (!_wasDown)
            {
                _active = !uiCapturesMouse;
            }

            _wasDown = true;
            return _active;
        }
    }
}
