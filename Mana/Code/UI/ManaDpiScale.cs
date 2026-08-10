using Silk.NET.GLFW;
using Silk.NET.Windowing;

namespace Mana
{
    /// <summary>
    /// The OS's per-monitor content scale (the "150%" in Windows display settings). Silk.NET's
    /// FramebufferSize/Size ratio can't supply this -- on Windows it's pinned at 1.0 regardless of
    /// scaling, so it only ever detects macOS Retina.
    /// </summary>
    public static class ManaDpiScale
    {
        private static Glfw? _glfw;

        /// <summary>
        /// The scale the UI must apply itself, i.e. content scale minus what ImGui's
        /// DisplayFramebufferScale already covers. 1.5 on a 150% Windows monitor, 1.0 on macOS Retina.
        /// </summary>
        public static float QueryUiScale(IWindow? window)
        {
            if (window == null)
            {
                return 1.0f;
            }

            var contentScale = QueryContentScale(window);

            var framebufferScale = window.Size.X > 0
                ? (float)window.FramebufferSize.X / window.Size.X
                : 1.0f;

            // Clamped to >= 1 deliberately, and this matters more than it looks. A framebuffer SMALLER
            // than the window is not a thing DPI ever produces -- but the two sizes do briefly disagree
            // while a window is being dragged across a DPI boundary, and a transient ratio below 1 would
            // divide the content scale UP instead of leaving it alone. That is exactly how a 150% monitor
            // once resolved to 2.0 here (1.5 / 0.75), latching an "imgui.200.ini" that then stuck.
            if (!float.IsFinite(framebufferScale) || framebufferScale < 1.0f)
            {
                framebufferScale = 1.0f;
            }

            // Equally, the ratio can never legitimately exceed the content scale -- at most it accounts
            // for ALL of it (macOS Retina, where contentScale and framebufferScale are both 2). Capping
            // here keeps a mid-transition reading from driving the result below 1.
            framebufferScale = MathF.Min(framebufferScale, MathF.Max(contentScale, 1.0f));

            var uiScale = contentScale / framebufferScale;
            return float.IsFinite(uiScale) && uiScale > 0f ? uiScale : 1.0f;
        }

        /// <summary>The raw OS content scale for the monitor the window is currently on, or 1.0 if it can't be determined.</summary>
        public static unsafe float QueryContentScale(IWindow window)
        {
            try
            {
                // Queried per-MONITOR rather than per-window: Silk.NET 2.23 binds glfwGetMonitorContentScale
                // but not glfwGetWindowContentScale. Equivalent for this purpose -- IWindow.Monitor already
                // resolves to whichever display the window currently sits on, and updates as it's dragged
                // between them, which is exactly the signal the window-level call would have given.
                var monitorIndex = window.Monitor?.Index ?? 0;

                _glfw ??= Glfw.GetApi();
                var monitors = _glfw.GetMonitors(out var monitorCount);
                if (monitors == null || monitorCount <= 0)
                {
                    return 1.0f;
                }

                var monitor = monitors[Math.Clamp(monitorIndex, 0, monitorCount - 1)];
                if (monitor == null)
                {
                    return 1.0f;
                }

                _glfw.GetMonitorContentScale(monitor, out var xScale, out var yScale);

                // X and Y scale differ only on exotic setups; the UI has one size knob, so take the
                // larger of the two -- undersized text is worse than slightly oversized.
                var scale = MathF.Max(xScale, yScale);
                return float.IsFinite(scale) && scale > 0f ? scale : 1.0f;
            }
            catch (Exception)
            {
                // Non-GLFW backend, a window that isn't realized yet, or a GLFW build without the entry
                // point. Falling back to 1.0 just means the UI stays at its unscaled size -- the same
                // behaviour as before any of this existed, never a crash.
                return 1.0f;
            }
        }
    }
}
