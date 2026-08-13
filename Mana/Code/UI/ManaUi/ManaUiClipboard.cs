using ManaUI;
using Silk.NET.Input;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Connects ManaUI's clipboard to the operating system's.
    /// <para>
    /// <see cref="TextBox.Clipboard"/> is a static string, because ManaUI has no windowing dependency and
    /// therefore no way to reach a real clipboard. That is the right trade for the library -- it keeps the
    /// text controls testable headlessly -- but it means copy and paste only worked between two ManaUI
    /// fields until something bridged it, which is what this does.
    /// </para>
    /// </summary>
    public static class ManaUiClipboard
    {
        private static IKeyboard? _keyboard;

        /// <summary>
        /// Redirects the library's clipboard at the platform's. Idempotent, so a second input context
        /// replaces the first rather than stacking.
        /// </summary>
        public static void Attach(IKeyboard keyboard)
        {
            _keyboard = keyboard;

            // Seeded from the OS so the first paste in a session has something to paste, rather than
            // whatever the static happened to be initialised with.
            TextBox.Clipboard = Read();
            TextBox.ClipboardRead = Read;
            TextBox.ClipboardWrite = Write;
        }

        private static string Read()
        {
            try
            {
                return _keyboard?.ClipboardText ?? string.Empty;
            }
            catch (Exception)
            {
                // A clipboard held open by another application throws rather than returning empty on
                // some platforms, and a failed paste is not worth an exception dialog.
                return string.Empty;
            }
        }

        private static void Write(string text)
        {
            try
            {
                if (_keyboard != null)
                {
                    _keyboard.ClipboardText = text;
                }
            }
            catch (Exception)
            {
                // As above: a copy that does not reach the OS clipboard still reached ManaUI's own.
            }
        }
    }
}
