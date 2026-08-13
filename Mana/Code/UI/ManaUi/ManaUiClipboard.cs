using ManaUI;
using Silk.NET.Input;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// ManaUI's clipboard, backed by the operating system's.
    /// <para>
    /// The library copies and pastes through <see cref="IUiClipboard"/> because it has no windowing
    /// dependency and so no way to reach a real clipboard. That is the right trade -- it keeps the text
    /// controls testable headlessly -- but it means copy and paste only worked between two ManaUI fields
    /// until something bridged it, which is what this does.
    /// </para>
    /// </summary>
    public sealed class ManaUiClipboard : IUiClipboard
    {
        private readonly IKeyboard _keyboard;

        public ManaUiClipboard(IKeyboard keyboard)
        {
            _keyboard = keyboard;
        }

        /// <summary>
        /// Points ManaUI at the platform clipboard. Idempotent, so a second input context replaces the
        /// first rather than stacking.
        /// </summary>
        public static void Attach(IKeyboard keyboard)
        {
            TextBox.Clipboard = new ManaUiClipboard(keyboard);
        }

        public string Text
        {
            get
            {
                try
                {
                    return _keyboard.ClipboardText ?? string.Empty;
                }
                catch (Exception)
                {
                    // A clipboard held open by another application throws rather than returning empty on
                    // some platforms, and a failed paste is not worth an exception dialog.
                    return string.Empty;
                }
            }

            set
            {
                try
                {
                    _keyboard.ClipboardText = value;
                }
                catch (Exception)
                {
                    // As above: a copy that does not reach the OS clipboard is not worth a crash.
                }
            }
        }
    }
}
