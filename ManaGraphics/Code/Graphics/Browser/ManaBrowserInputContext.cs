#if MANA_BROWSER
using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics.Browser
{
    // IInputContext for browser-wasm, backed by real DOM events (see mana_interop.js's installInput).
    //
    // The engine consumes input by POLLING, not by subscribing: CameraComponent and the pawn
    // controllers ask IsKeyPressed(Key.W)/IsButtonPressed(MouseButton.Right)/Position/ScrollWheels
    // every frame. So held-key and held-button state is the part that actually has to be right --
    // the Silk.NET events are raised too (ManaUI's input bridge wants them for clicks and typing),
    // but nothing about movement or camera control depends on them.
    public sealed class ManaBrowserInputContext : IInputContext
    {
        public static ManaBrowserInputContext? Current { get; private set; }

        private readonly ManaBrowserKeyboard _keyboard = new();
        private readonly ManaBrowserMouse _mouse = new();

        public ManaBrowserInputContext()
        {
            Current = this;
            Keyboards = new List<IKeyboard> { _keyboard };
            Mice = new List<IMouse> { _mouse };
        }

        internal ManaBrowserKeyboard Keyboard => _keyboard;
        internal ManaBrowserMouse Mouse => _mouse;

        public nint Handle => 0;
        public IReadOnlyList<IGamepad> Gamepads { get; } = Array.Empty<IGamepad>();
        public IReadOnlyList<IJoystick> Joysticks { get; } = Array.Empty<IJoystick>();
        public IReadOnlyList<IKeyboard> Keyboards { get; }
        public IReadOnlyList<IMouse> Mice { get; }
        public IReadOnlyList<IInputDevice> OtherDevices { get; } = Array.Empty<IInputDevice>();

        // Required by IInputContext; the browser's one keyboard and mouse never connect or disconnect.
        public event Action<IInputDevice, bool>? ConnectionChanged { add { } remove { } }

        /// <summary>
        /// Called once per frame after the frame's update has run. Scroll is a per-frame delta in
        /// Silk.NET's model (CameraComponent reads <c>ScrollWheels[0].Y</c> and expects 0 on a frame
        /// with no wheel movement), so it has to be zeroed after every frame that got to observe it.
        /// </summary>
        internal void EndFrame()
        {
            _mouse.EndFrame();
        }

        /// <summary>Drops all held keys/buttons -- see mana_interop.js's window blur handler.</summary>
        internal void ReleaseAll()
        {
            _keyboard.ReleaseAll();
            _mouse.ReleaseAll();
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }
    }

    internal sealed class ManaBrowserKeyboard : IKeyboard
    {
        private readonly HashSet<Key> _pressed = new();

        public string Name => "Browser Keyboard";
        public int Index => 0;
        public bool IsConnected => true;

        public IReadOnlyList<Key> SupportedKeys { get; } = Array.Empty<Key>();
        public string ClipboardText { get; set; } = string.Empty;

        public event Action<IKeyboard, Key, int>? KeyDown;
        public event Action<IKeyboard, Key, int>? KeyUp;
        public event Action<IKeyboard, char>? KeyChar;

        public bool IsKeyPressed(Key key) => _pressed.Contains(key);
        public bool IsScancodePressed(int scancode) => false;

        internal void HandleKeyDown(Key key, bool isRepeat)
        {
            if (key == Key.Unknown)
            {
                return;
            }

            _pressed.Add(key);

            // Auto-repeat is the OS typing the same key over and over; a game's "was this just
            // pressed" edge detection would fire on every repeat and read as a machine-gun tap.
            if (!isRepeat)
            {
                KeyDown?.Invoke(this, key, 0);
            }
        }

        internal void HandleKeyUp(Key key)
        {
            if (key == Key.Unknown)
            {
                return;
            }

            _pressed.Remove(key);
            KeyUp?.Invoke(this, key, 0);
        }

        internal void HandleKeyChar(char character)
        {
            KeyChar?.Invoke(this, character);
        }

        internal void ReleaseAll()
        {
            if (_pressed.Count == 0)
            {
                return;
            }

            // Copy first: the KeyUp handlers below are free to poll IsKeyPressed, and mutating the
            // set while enumerating it would throw.
            var held = new Key[_pressed.Count];
            _pressed.CopyTo(held);
            _pressed.Clear();
            foreach (var key in held)
            {
                KeyUp?.Invoke(this, key, 0);
            }
        }

        public void BeginInput()
        {
        }

        public void EndInput()
        {
        }
    }

    internal sealed class ManaBrowserMouse : IMouse
    {
        private readonly HashSet<MouseButton> _pressed = new();
        private readonly ScrollWheel[] _scrollWheels = { new ScrollWheel(0.0f, 0.0f) };

        public string Name => "Browser Mouse";
        public int Index => 0;
        public bool IsConnected => true;

        public IReadOnlyList<MouseButton> SupportedButtons { get; } = new[]
        {
            MouseButton.Left, MouseButton.Middle, MouseButton.Right
        };

        public IReadOnlyList<ScrollWheel> ScrollWheels => _scrollWheels;
        public Vector2 Position { get; set; }
        public ICursor Cursor { get; } = new ManaBrowserCursor();
        public int DoubleClickTime { get; set; } = 500;
        public int DoubleClickRange { get; set; } = 4;

        public event Action<IMouse, MouseButton>? MouseDown;
        public event Action<IMouse, MouseButton>? MouseUp;
        public event Action<IMouse, MouseButton, Vector2>? Click;
        public event Action<IMouse, MouseButton, Vector2>? DoubleClick { add { } remove { } }
        public event Action<IMouse, Vector2>? MouseMove;
        public event Action<IMouse, ScrollWheel>? Scroll;

        public bool IsButtonPressed(MouseButton button) => _pressed.Contains(button);

        internal void HandleMove(float x, float y)
        {
            Position = new Vector2(x, y);
            MouseMove?.Invoke(this, Position);
        }

        /// <summary>
        /// Pointer-locked movement, which has no page position at all -- only deltas. Folded into
        /// Position anyway so the look-around code (which works from frame-to-frame Position
        /// differences) behaves identically whether or not the pointer happens to be locked.
        /// </summary>
        internal void HandleDelta(float deltaX, float deltaY)
        {
            Position += new Vector2(deltaX, deltaY);
            MouseMove?.Invoke(this, Position);
        }

        internal void HandleButtonDown(int button)
        {
            var mapped = MapButton(button);
            if (mapped == MouseButton.Unknown)
            {
                return;
            }

            _pressed.Add(mapped);
            MouseDown?.Invoke(this, mapped);
        }

        internal void HandleButtonUp(int button)
        {
            var mapped = MapButton(button);
            if (mapped == MouseButton.Unknown)
            {
                return;
            }

            // Only a press that this surface actually saw go down counts as a click -- a mouseup
            // arriving from a drag that began elsewhere isn't one.
            var wasPressed = _pressed.Remove(mapped);
            MouseUp?.Invoke(this, mapped);
            if (wasPressed)
            {
                Click?.Invoke(this, mapped, Position);
            }
        }

        internal void HandleWheel(float x, float y)
        {
            var accumulated = new ScrollWheel(_scrollWheels[0].X + x, _scrollWheels[0].Y + y);
            _scrollWheels[0] = accumulated;
            Scroll?.Invoke(this, accumulated);
        }

        internal void EndFrame()
        {
            if (_scrollWheels[0].X != 0.0f || _scrollWheels[0].Y != 0.0f)
            {
                _scrollWheels[0] = new ScrollWheel(0.0f, 0.0f);
            }
        }

        internal void ReleaseAll()
        {
            if (_pressed.Count == 0)
            {
                return;
            }

            var held = new MouseButton[_pressed.Count];
            _pressed.CopyTo(held);
            _pressed.Clear();
            foreach (var button in held)
            {
                MouseUp?.Invoke(this, button);
            }
        }

        // DOM MouseEvent.button ordering, which is not the same as Silk.NET's enum ordering:
        // the browser calls the middle button 1 and the right button 2.
        private static MouseButton MapButton(int button) => button switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            3 => MouseButton.Button4,
            4 => MouseButton.Button5,
            _ => MouseButton.Unknown
        };
    }

    // Disabled uses DOM pointer lock; raw motion and custom cursor images are not supported.
    internal sealed class ManaBrowserCursor : ICursor
    {
        public CursorType Type { get; set; }
        public StandardCursor StandardCursor { get; set; }
        private CursorMode _cursorMode;
        public CursorMode CursorMode
        {
            get => _cursorMode;
            set
            {
                if (!IsSupported(value))
                {
                    return;
                }
                _cursorMode = value;
                if (value == CursorMode.Disabled)
                {
                    ManaBrowserInterop.RequestPointerLock();
                }
                else
                {
                    ManaBrowserInterop.ExitPointerLock();
                }
            }
        }
        public bool IsConfined { get; set; }
        public int HotspotX { get; set; }
        public int HotspotY { get; set; }
        public Silk.NET.Core.RawImage Image { get; set; }

        public bool IsSupported(CursorMode mode) => mode is CursorMode.Normal or CursorMode.Disabled;
        public bool IsSupported(StandardCursor standardCursor) => false;
    }
}
#endif
