using System;

namespace ManaCore.Graphics
{
    public interface IManaImGuiController : IDisposable
    {
        void Update(float deltaTime);
        void Render();
        void RecreateFontDeviceTexture();

        /// <summary>
        /// Returns true when a UI layer drawn on top of ImGui owns the pointer this frame, in which case
        /// ImGui must not see it at all.
        /// <para>
        /// Needed because ImGui polls the mouse rather than consuming events, so there is nothing for the
        /// layer above to mark as handled -- without this, one click lands in both, and pressing a ManaUI
        /// button also presses whatever ImGui had underneath it.
        /// </para>
        /// </summary>
        Func<bool>? IsPointerOwnedByOverlay { get; set; }

        /// <summary>
        /// Returns true when an overlay has keyboard focus, so keystrokes meant for its text field do not
        /// also reach ImGui's.
        /// </summary>
        Func<bool>? IsKeyboardOwnedByOverlay { get; set; }
    }
}
