#if !MANA_BROWSER
using System.Runtime.InteropServices;
using Silk.NET.Windowing;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Keeps torn-off panel windows with the editor's main window, so clicking the main window never buries them. On
    /// Windows a panel window is owned by the main one -- the OS then keeps it above its owner, brings it back whenever
    /// the owner is activated, and minimises and restores it along with it. macOS has no ownership that leaves a window
    /// free to move on its own, so there the panel windows are put just above the main one each time it gains focus.
    /// </summary>
    internal static class ManaWindowOwnership
    {
        private const int GwlpHwndParent = -8;
        private const uint GwOwner = 4;

        /// <summary>Ties <paramref name="panel"/> to <paramref name="main"/> as far as the platform allows.</summary>
        public static void Own(IWindow panel, IWindow main)
        {
            try
            {
                if (OperatingSystem.IsWindows() && panel.Native?.Win32 is { } child && main.Native?.Win32 is { } parent)
                {
                    SetWindowLongPtrW(child.Hwnd, GwlpHwndParent, parent.Hwnd);
                    // Read back from the OS, so a log shows whether the tie took.
                    var owned = GetWindow(child.Hwnd, GwOwner) == parent.Hwnd;
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"[Windows] Panel window owned by the main window: {owned}.");
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                // Not fatal: the panel simply behaves as a free window.
            }
        }

        /// <summary>
        /// Puts the panel windows just above the main one without taking focus from it -- for macOS, called when the main
        /// window gains focus. Nothing on Windows, where ownership already does it.
        /// </summary>
        public static void RaiseAbove(IEnumerable<IWindow> panels, IWindow main)
        {
            if (!OperatingSystem.IsMacOS() || main.Native?.Cocoa is not { } mainWindow)
            {
                return;
            }

            try
            {
                var windowNumber = objc_msgSend_long(mainWindow, sel_registerName("windowNumber"));
                var orderWindow = sel_registerName("orderWindow:relativeTo:");
                foreach (var panel in panels)
                {
                    if (panel.Native?.Cocoa is { } panelWindow)
                    {
                        // NSWindowAbove = 1: ordered just above the main window, which keeps its focus.
                        objc_msgSend_order(panelWindow, orderWindow, 1, windowNumber);
                    }
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

        [DllImport("user32.dll")]
        private static extern nint GetWindow(nint hwnd, uint command);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern nint sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern long objc_msgSend_long(nint receiver, nint selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_order(nint receiver, nint selector, long place, long relativeTo);
    }
}
#endif
