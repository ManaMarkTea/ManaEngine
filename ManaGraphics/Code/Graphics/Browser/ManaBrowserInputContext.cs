#if MANA_BROWSER
using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics.Browser
{
    // Minimal IInputContext for browser-wasm (M1). CameraComponent.cs calls
    // Context.Input.Mice.First()/.Keyboards.First() directly (not FirstOrDefault), so this needs
    // at least one real IKeyboard/IMouse instance even before real DOM event wiring exists --
    // without that, play mode's camera setup throws InvalidOperationException on an empty
    // sequence the moment it runs. Deliberately NOT wired to real keyboard/mouse DOM events yet:
    // that's follow-up work once the render path itself is confirmed working (see
    // WebPort-Plan.md M1); camera controls simply won't respond to input until then.
    public sealed class ManaBrowserInputContext : IInputContext
    {
        public static ManaBrowserInputContext? Current { get; private set; }

        public ManaBrowserInputContext()
        {
            Current = this;
            Keyboards = new List<IKeyboard> { new ManaBrowserKeyboard() };
            Mice = new List<IMouse> { new ManaBrowserMouse() };
        }

        public nint Handle => 0;
        public IReadOnlyList<IGamepad> Gamepads { get; } = Array.Empty<IGamepad>();
        public IReadOnlyList<IJoystick> Joysticks { get; } = Array.Empty<IJoystick>();
        public IReadOnlyList<IKeyboard> Keyboards { get; }
        public IReadOnlyList<IMouse> Mice { get; }
        public IReadOnlyList<IInputDevice> OtherDevices { get; } = Array.Empty<IInputDevice>();

        public event Action<IInputDevice, bool>? ConnectionChanged;

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
        public string Name => "Browser Keyboard";
        public int Index => 0;
        public bool IsConnected => true;

        public IReadOnlyList<Key> SupportedKeys { get; } = Array.Empty<Key>();
        public string ClipboardText { get; set; } = string.Empty;

        public event Action<IKeyboard, Key, int>? KeyDown;
        public event Action<IKeyboard, Key, int>? KeyUp;
        public event Action<IKeyboard, char>? KeyChar;

        public bool IsKeyPressed(Key key) => false;
        public bool IsScancodePressed(int scancode) => false;

        public void BeginInput()
        {
        }

        public void EndInput()
        {
        }
    }

    internal sealed class ManaBrowserMouse : IMouse
    {
        public string Name => "Browser Mouse";
        public int Index => 0;
        public bool IsConnected => true;

        public IReadOnlyList<MouseButton> SupportedButtons { get; } = Array.Empty<MouseButton>();
        public IReadOnlyList<ScrollWheel> ScrollWheels { get; } = new[] { new ScrollWheel(0, 0) };
        public Vector2 Position { get; set; }
        public ICursor? Cursor { get; } = null;
        public int DoubleClickTime { get; set; } = 500;
        public int DoubleClickRange { get; set; } = 4;

        public event Action<IMouse, MouseButton>? MouseDown;
        public event Action<IMouse, MouseButton>? MouseUp;
        public event Action<IMouse, MouseButton, Vector2>? Click;
        public event Action<IMouse, MouseButton, Vector2>? DoubleClick;
        public event Action<IMouse, Vector2>? MouseMove;
        public event Action<IMouse, ScrollWheel>? Scroll;

        public bool IsButtonPressed(MouseButton button) => false;
    }
}
#endif
