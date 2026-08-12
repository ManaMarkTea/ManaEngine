using System.Diagnostics;
using System.Threading;
using ManaUI;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Translates Silk.NET input into <see cref="UiInputRouter"/> calls.
    /// <para>
    /// This adapter is the only place in the codebase that knows both vocabularies. ManaUI deliberately
    /// defines its own <see cref="UiKey"/> rather than referencing Silk, so the framework stays free of any
    /// windowing dependency and can be driven headlessly in tests.
    /// </para>
    /// </summary>
    public sealed class ManaUiInputBridge : IDisposable
    {
        private readonly UiRoot _root;
        private readonly IInputContext _input;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private static int _instanceCount;

        private IMouse? _mouse;
        private IKeyboard? _keyboard;
        private bool _disposed;

        public ManaUiInputBridge(UiRoot root, IInputContext input)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(input);

            _root = root;
            _input = input;

            // One line, once. Double-subscription would silently multiply every click, and this is the
            // cheapest way to rule that out when input misbehaves.
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                $"[ManaUI] input bridge #{Interlocked.Increment(ref _instanceCount)} created; "
                + $"mice={input.Mice.Count} keyboards={input.Keyboards.Count}");

            _mouse = _input.Mice.FirstOrDefault();
            if (_mouse != null)
            {
                _mouse.MouseMove += OnMouseMove;
                _mouse.MouseDown += OnMouseDown;
                _mouse.MouseUp += OnMouseUp;
                _mouse.Scroll += OnScroll;
            }

            _keyboard = _input.Keyboards.FirstOrDefault();
            if (_keyboard != null)
            {
                _keyboard.KeyDown += OnKeyDown;
                _keyboard.KeyUp += OnKeyUp;
                _keyboard.KeyChar += OnKeyChar;
            }
        }

        /// <summary>
        /// True when the pointer is over ManaUI. Game modes should check this before acting on a click, so
        /// pressing a button does not also place a voxel in the scene behind it.
        /// </summary>
        public bool IsPointerOverUi => _root.Input.IsPointerOverUi;

        /// <summary>True when a focused control wants raw keystrokes, so editor shortcuts should stand down.</summary>
        public bool IsKeyboardCaptured => _root.Input.IsKeyboardCaptured;

        /// <summary>
        /// Applies the cursor ManaUI is asking for. Only while the pointer is actually over ManaUI --
        /// ImGui manages the cursor too, and fighting it every frame would make both flicker.
        /// </summary>
        public void ApplyCursor()
        {
            if (_mouse == null || !_root.Input.IsPointerOverUi)
            {
                return;
            }

            var requested = _root.RequestedCursor switch
            {
                UiCursor.IBeam => StandardCursor.IBeam,
                UiCursor.Hand => StandardCursor.Hand,
                UiCursor.SizeWE => StandardCursor.HResize,
                UiCursor.SizeNS => StandardCursor.VResize,
                UiCursor.SizeNWSE => StandardCursor.NwseResize,
                UiCursor.SizeNESW => StandardCursor.NeswResize,
                UiCursor.SizeAll => StandardCursor.ResizeAll,
                UiCursor.NotAllowed => StandardCursor.NotAllowed,
                _ => StandardCursor.Arrow
            };

            try
            {
                if (!_mouse.Cursor.IsSupported(requested))
                {
                    requested = StandardCursor.Arrow;
                }

                _mouse.Cursor.Type = CursorType.Standard;
                _mouse.Cursor.StandardCursor = requested;
            }
            catch
            {
                // Cursor changes are cosmetic and must never interrupt a frame.
            }
        }

        private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
        {
            _root.Input.InjectPointerMoved(position, CurrentModifiers());
        }

        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            if (TryMapButton(button, out var pointerButton))
            {
                _root.Input.InjectPointerPressed(
                    mouse.Position,
                    pointerButton,
                    _clock.Elapsed.TotalSeconds,
                    CurrentModifiers());
            }
        }

        private void OnMouseUp(IMouse mouse, MouseButton button)
        {
            if (TryMapButton(button, out var pointerButton))
            {
                _root.Input.InjectPointerReleased(mouse.Position, pointerButton, CurrentModifiers());
            }
        }

        private void OnScroll(IMouse mouse, ScrollWheel scroll)
        {
            _root.Input.InjectPointerWheel(new Vector2(scroll.X, scroll.Y), CurrentModifiers());
        }

        private void OnKeyDown(IKeyboard keyboard, SilkKey key, int keyCode)
        {
            var mapped = MapKey(key);
            if (mapped != UiKey.None)
            {
                _root.Input.InjectKeyDown(mapped, CurrentModifiers());
            }
        }

        private void OnKeyUp(IKeyboard keyboard, SilkKey key, int keyCode)
        {
            var mapped = MapKey(key);
            if (mapped != UiKey.None)
            {
                _root.Input.InjectKeyUp(mapped, CurrentModifiers());
            }
        }

        private void OnKeyChar(IKeyboard keyboard, char character)
        {
            // Control characters arrive here too (Enter, Backspace); those are key events, not text.
            if (!char.IsControl(character))
            {
                _root.Input.InjectTextInput(character.ToString());
            }
        }

        private UiModifiers CurrentModifiers()
        {
            if (_keyboard == null)
            {
                return UiModifiers.None;
            }

            var modifiers = UiModifiers.None;

            if (_keyboard.IsKeyPressed(SilkKey.ShiftLeft) || _keyboard.IsKeyPressed(SilkKey.ShiftRight))
            {
                modifiers |= UiModifiers.Shift;
            }

            if (_keyboard.IsKeyPressed(SilkKey.ControlLeft) || _keyboard.IsKeyPressed(SilkKey.ControlRight))
            {
                modifiers |= UiModifiers.Control;
            }

            if (_keyboard.IsKeyPressed(SilkKey.AltLeft) || _keyboard.IsKeyPressed(SilkKey.AltRight))
            {
                modifiers |= UiModifiers.Alt;
            }

            if (_keyboard.IsKeyPressed(SilkKey.SuperLeft) || _keyboard.IsKeyPressed(SilkKey.SuperRight))
            {
                modifiers |= UiModifiers.Meta;
            }

            return modifiers;
        }

        private static bool TryMapButton(MouseButton button, out PointerButton result)
        {
            switch (button)
            {
                case MouseButton.Left:
                    result = PointerButton.Left;
                    return true;
                case MouseButton.Right:
                    result = PointerButton.Right;
                    return true;
                case MouseButton.Middle:
                    result = PointerButton.Middle;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        /// <summary>
        /// Only the keys the UI actually acts on are mapped; everything else returns None and is left for
        /// the game mode's own shortcut handling.
        /// </summary>
        private static UiKey MapKey(SilkKey key)
        {
            return key switch
            {
                SilkKey.Left => UiKey.Left,
                SilkKey.Right => UiKey.Right,
                SilkKey.Up => UiKey.Up,
                SilkKey.Down => UiKey.Down,
                SilkKey.Home => UiKey.Home,
                SilkKey.End => UiKey.End,
                SilkKey.PageUp => UiKey.PageUp,
                SilkKey.PageDown => UiKey.PageDown,
                SilkKey.Backspace => UiKey.Backspace,
                SilkKey.Delete => UiKey.Delete,
                SilkKey.Insert => UiKey.Insert,
                SilkKey.Enter or SilkKey.KeypadEnter => UiKey.Enter,
                SilkKey.Escape => UiKey.Escape,
                SilkKey.Tab => UiKey.Tab,
                SilkKey.Space => UiKey.Space,
                SilkKey.A => UiKey.A,
                SilkKey.C => UiKey.C,
                SilkKey.V => UiKey.V,
                SilkKey.X => UiKey.X,
                SilkKey.Z => UiKey.Z,
                SilkKey.Y => UiKey.Y,
                SilkKey.F2 => UiKey.F2,
                SilkKey.F4 => UiKey.F4,
                _ => UiKey.None
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_mouse != null)
            {
                _mouse.MouseMove -= OnMouseMove;
                _mouse.MouseDown -= OnMouseDown;
                _mouse.MouseUp -= OnMouseUp;
                _mouse.Scroll -= OnScroll;
                _mouse = null;
            }

            if (_keyboard != null)
            {
                _keyboard.KeyDown -= OnKeyDown;
                _keyboard.KeyUp -= OnKeyUp;
                _keyboard.KeyChar -= OnKeyChar;
                _keyboard = null;
            }
        }
    }
}
