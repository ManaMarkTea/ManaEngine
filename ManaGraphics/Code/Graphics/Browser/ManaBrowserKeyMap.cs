#if MANA_BROWSER
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics.Browser
{
    /// <summary>
    /// Maps a DOM <c>KeyboardEvent.code</c> to Silk.NET's <see cref="Key"/>.
    /// <para>
    /// Deliberately keyed on <c>code</c> (physical key) rather than <c>key</c> (produced character):
    /// WASD movement has to stay on the same three-across-plus-one-above physical keys regardless of
    /// the user's keyboard layout, and <c>key</c> would report "z" for the same physical key a
    /// French AZERTY user presses to mean "w".
    /// </para>
    /// </summary>
    internal static class ManaBrowserKeyMap
    {
        public static Key FromDomCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return Key.Unknown;
            }

            // "KeyA".."KeyZ" -> Key.A..Key.Z, and "Digit0".."Digit9" -> Key.Number0..Number9.
            // Handled arithmetically rather than as 36 more switch arms; both Silk.NET ranges are
            // contiguous and ordered the same way as the DOM's.
            if (code.Length == 4 && code.StartsWith("Key", System.StringComparison.Ordinal))
            {
                var letter = code[3];
                if (letter >= 'A' && letter <= 'Z')
                {
                    return Key.A + (letter - 'A');
                }
            }

            if (code.Length == 6 && code.StartsWith("Digit", System.StringComparison.Ordinal))
            {
                var digit = code[5];
                if (digit >= '0' && digit <= '9')
                {
                    return Key.Number0 + (digit - '0');
                }
            }

            if (code.Length == 7 && code.StartsWith("Numpad", System.StringComparison.Ordinal))
            {
                var digit = code[6];
                if (digit >= '0' && digit <= '9')
                {
                    return Key.Keypad0 + (digit - '0');
                }
            }

            // F1..F12. Not folded into the arithmetic above because Silk.NET's F-key block is not
            // adjacent to anything else useful here, and 12 explicit arms read better than
            // parsing a one-or-two-digit suffix.
            return code switch
            {
                "Space" => Key.Space,
                "Enter" or "NumpadEnter" => Key.Enter,
                "Escape" => Key.Escape,
                "Tab" => Key.Tab,
                "Backspace" => Key.Backspace,
                "Delete" => Key.Delete,
                "Insert" => Key.Insert,
                "Home" => Key.Home,
                "End" => Key.End,
                "PageUp" => Key.PageUp,
                "PageDown" => Key.PageDown,

                "ArrowUp" => Key.Up,
                "ArrowDown" => Key.Down,
                "ArrowLeft" => Key.Left,
                "ArrowRight" => Key.Right,

                "ShiftLeft" => Key.ShiftLeft,
                "ShiftRight" => Key.ShiftRight,
                "ControlLeft" => Key.ControlLeft,
                "ControlRight" => Key.ControlRight,
                "AltLeft" => Key.AltLeft,
                "AltRight" => Key.AltRight,
                // The Command key on macOS and the Windows key elsewhere. Silk.NET calls it Super.
                "MetaLeft" => Key.SuperLeft,
                "MetaRight" => Key.SuperRight,

                "Minus" => Key.Minus,
                "Equal" => Key.Equal,
                "BracketLeft" => Key.LeftBracket,
                "BracketRight" => Key.RightBracket,
                "Backslash" => Key.BackSlash,
                "Semicolon" => Key.Semicolon,
                "Quote" => Key.Apostrophe,
                "Comma" => Key.Comma,
                "Period" => Key.Period,
                "Slash" => Key.Slash,
                "Backquote" => Key.GraveAccent,

                "NumpadAdd" => Key.KeypadAdd,
                "NumpadSubtract" => Key.KeypadSubtract,
                "NumpadMultiply" => Key.KeypadMultiply,
                "NumpadDivide" => Key.KeypadDivide,
                "NumpadDecimal" => Key.KeypadDecimal,

                "F1" => Key.F1,
                "F2" => Key.F2,
                "F3" => Key.F3,
                "F4" => Key.F4,
                "F5" => Key.F5,
                "F6" => Key.F6,
                "F7" => Key.F7,
                "F8" => Key.F8,
                "F9" => Key.F9,
                "F10" => Key.F10,
                "F11" => Key.F11,
                "F12" => Key.F12,

                "CapsLock" => Key.CapsLock,

                _ => Key.Unknown
            };
        }
    }
}
#endif
