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
        private readonly ManaBrowserGamepad[] _gamepads = { new(0), new(1), new(2), new(3) };

        public ManaBrowserInputContext()
        {
            Current = this;
            Keyboards = new List<IKeyboard> { _keyboard };
            Mice = new List<IMouse> { _mouse };
            Gamepads = _gamepads;
        }

        /// <summary>Reads every pad off the Gamepad API (mana_interop.js's pollGamepads); once a frame, before it runs.</summary>
        internal void PollGamepads()
        {
            double[] state;
            try
            {
                state = ManaBrowserInterop.PollGamepads();
            }
            catch (Exception)
            {
                return;
            }

            for (var slot = 0; slot < _gamepads.Length && (slot + 1) * ManaBrowserGamepad.Stride <= state.Length; slot++)
            {
                _gamepads[slot].Update(state.AsSpan(slot * ManaBrowserGamepad.Stride, ManaBrowserGamepad.Stride));
            }
        }

        internal ManaBrowserKeyboard Keyboard => _keyboard;
        internal ManaBrowserMouse Mouse => _mouse;

        public nint Handle => 0;
        public IReadOnlyList<IGamepad> Gamepads { get; }
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

    /// <summary>
    /// One Gamepad API slot, in the W3C standard mapping (an Xbox layout; most pads report it). Always listed, connected
    /// or not, so a game polls <see cref="IsConnected"/> as it does on desktop.
    /// </summary>
    internal sealed class ManaBrowserGamepad : IGamepad
    {
        public const int Stride = 1 + 17 + 4;

        // Standard-mapping button order, less the two triggers (6, 7), which are Triggers here as on desktop.
        private static readonly (int Index, ButtonName Name)[] Map =
        {
            (0, ButtonName.A), (1, ButtonName.B), (2, ButtonName.X), (3, ButtonName.Y),
            (4, ButtonName.LeftBumper), (5, ButtonName.RightBumper), (8, ButtonName.Back), (9, ButtonName.Start),
            (10, ButtonName.LeftStick), (11, ButtonName.RightStick), (12, ButtonName.DPadUp), (13, ButtonName.DPadDown),
            (14, ButtonName.DPadLeft), (15, ButtonName.DPadRight), (16, ButtonName.Home)
        };

        private readonly Button[] _buttons = new Button[Map.Length];
        private readonly Thumbstick[] _thumbsticks = { new(0, 0.0f, 0.0f), new(1, 0.0f, 0.0f) };
        private readonly Trigger[] _triggers = { new(0, 0.0f), new(1, 0.0f) };

        public ManaBrowserGamepad(int index)
        {
            Index = index;
            for (var i = 0; i < Map.Length; i++)
            {
                _buttons[i] = new Button(Map[i].Name, i, false);
            }
        }

        public string Name { get; private set; } = string.Empty;
        public int Index { get; }
        public bool IsConnected { get; private set; }
        public IReadOnlyList<Button> Buttons => _buttons;
        public IReadOnlyList<Thumbstick> Thumbsticks => _thumbsticks;
        public IReadOnlyList<Trigger> Triggers => _triggers;
        public IReadOnlyList<IMotor> VibrationMotors { get; } = Array.Empty<IMotor>();
        public Deadzone Deadzone { get; set; }

        public event Action<IGamepad, Button>? ButtonDown;
        public event Action<IGamepad, Button>? ButtonUp;
        public event Action<IGamepad, Thumbstick>? ThumbstickMoved;
        public event Action<IGamepad, Trigger>? TriggerMoved;

        internal void Update(ReadOnlySpan<double> state)
        {
            var connected = state[0] > 0.5;
            if (connected && !IsConnected)
            {
                try
                {
                    Name = ManaBrowserInterop.GamepadName(Index);
                }
                catch (Exception)
                {
                    Name = $"Gamepad {Index + 1}";
                }
            }

            IsConnected = connected;
            for (var i = 0; i < Map.Length; i++)
            {
                var pressed = connected && state[1 + Map[i].Index] > 0.5;
                if (pressed == _buttons[i].Pressed)
                {
                    continue;
                }

                _buttons[i] = new Button(Map[i].Name, i, pressed);
                (pressed ? ButtonDown : ButtonUp)?.Invoke(this, _buttons[i]);
            }

            for (var t = 0; t < 2; t++)
            {
                var position = connected ? (float)state[1 + 6 + t] : 0.0f;
                if (position != _triggers[t].Position)
                {
                    _triggers[t] = new Trigger(t, position);
                    TriggerMoved?.Invoke(this, _triggers[t]);
                }
            }

            for (var s = 0; s < 2; s++)
            {
                var x = connected ? (float)state[18 + s * 2] : 0.0f;
                var y = connected ? (float)state[19 + s * 2] : 0.0f;
                if (x != _thumbsticks[s].X || y != _thumbsticks[s].Y)
                {
                    _thumbsticks[s] = new Thumbstick(s, x, y);
                    ThumbstickMoved?.Invoke(this, _thumbsticks[s]);
                }
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
