using System.Numerics;
using Silk.NET.Input;

namespace ManaVoxel.iOS;

/// <summary>
/// The engine's input on a phone. The engine polls held keys and buttons (CameraComponent, the pawn controller), so
/// the touch controls (<see cref="TouchControlsView"/>) press the same keys a desktop player would -- W/A/S/D from the
/// thumbstick, Space to jump -- and a finger dragged over the game holds the right mouse button to look round, as on
/// the web.
/// </summary>
public sealed class IosInput : IInputContext
{
    public IosKeyboard Keyboard { get; } = new();
    public IosMouse Mouse { get; } = new();

    public IosInput()
    {
        Keyboards = new IKeyboard[] { Keyboard };
        Mice = new IMouse[] { Mouse };
    }

    public nint Handle => 0;
    public IReadOnlyList<IGamepad> Gamepads { get; } = Array.Empty<IGamepad>();
    public IReadOnlyList<IJoystick> Joysticks { get; } = Array.Empty<IJoystick>();
    public IReadOnlyList<IKeyboard> Keyboards { get; }
    public IReadOnlyList<IMouse> Mice { get; }
    public IReadOnlyList<IInputDevice> OtherDevices { get; } = Array.Empty<IInputDevice>();
    public event Action<IInputDevice, bool>? ConnectionChanged { add { } remove { } }

    /// <summary>After each frame: scrolling is a per-frame delta.</summary>
    public void EndFrame() => Mouse.EndFrame();

    /// <summary>Lets go of everything, when the app goes to the background mid-press.</summary>
    public void ReleaseAll()
    {
        Keyboard.ReleaseAll();
        Mouse.ReleaseAll();
    }

    public void Dispose()
    {
    }
}

public sealed class IosKeyboard : IKeyboard
{
    private readonly HashSet<Key> _pressed = new();

    public string Name => "Touch controls";
    public int Index => 0;
    public bool IsConnected => true;
    public IReadOnlyList<Key> SupportedKeys { get; } = Array.Empty<Key>();
    public string ClipboardText { get => UIPasteboard.General.String ?? string.Empty; set => UIPasteboard.General.String = value; }

    public event Action<IKeyboard, Key, int>? KeyDown;
    public event Action<IKeyboard, Key, int>? KeyUp;
    public event Action<IKeyboard, char>? KeyChar;

    public bool IsKeyPressed(Key key) => _pressed.Contains(key);
    public bool IsScancodePressed(int scancode) => false;

    public void Set(Key key, bool down)
    {
        if (down ? !_pressed.Add(key) : !_pressed.Remove(key))
        {
            return;
        }
        (down ? KeyDown : KeyUp)?.Invoke(this, key, 0);
    }

    public void Type(char character) => KeyChar?.Invoke(this, character);

    public void ReleaseAll()
    {
        foreach (var key in _pressed.ToArray())
        {
            Set(key, false);
        }
    }

    public void BeginInput()
    {
    }

    public void EndInput()
    {
    }
}

public sealed class IosMouse : IMouse
{
    private readonly HashSet<MouseButton> _pressed = new();
    private readonly ScrollWheel[] _scrollWheels = { new(0.0f, 0.0f) };

    public string Name => "Touch";
    public int Index => 0;
    public bool IsConnected => true;
    public IReadOnlyList<MouseButton> SupportedButtons { get; } = new[] { MouseButton.Left, MouseButton.Middle, MouseButton.Right };
    public IReadOnlyList<ScrollWheel> ScrollWheels => _scrollWheels;
    public Vector2 Position { get; set; }
    public ICursor Cursor { get; } = new IosCursor();
    public int DoubleClickTime { get; set; } = 500;
    public int DoubleClickRange { get; set; } = 8;

    public event Action<IMouse, MouseButton>? MouseDown;
    public event Action<IMouse, MouseButton>? MouseUp;
    public event Action<IMouse, MouseButton, Vector2>? Click;
    public event Action<IMouse, MouseButton, Vector2>? DoubleClick { add { } remove { } }
    public event Action<IMouse, Vector2>? MouseMove;
    public event Action<IMouse, ScrollWheel>? Scroll;

    public bool IsButtonPressed(MouseButton button) => _pressed.Contains(button);

    public void Move(Vector2 position)
    {
        Position = position;
        MouseMove?.Invoke(this, position);
    }

    public void Down(MouseButton button)
    {
        if (_pressed.Add(button))
        {
            MouseDown?.Invoke(this, button);
        }
    }

    public void Up(MouseButton button)
    {
        if (!_pressed.Remove(button))
        {
            return;
        }
        MouseUp?.Invoke(this, button);
        Click?.Invoke(this, button, Position);
    }

    public void Wheel(float x, float y)
    {
        _scrollWheels[0] = new ScrollWheel(_scrollWheels[0].X + x, _scrollWheels[0].Y + y);
        Scroll?.Invoke(this, _scrollWheels[0]);
    }

    public void EndFrame()
    {
        if (_scrollWheels[0].X != 0.0f || _scrollWheels[0].Y != 0.0f)
        {
            _scrollWheels[0] = new ScrollWheel(0.0f, 0.0f);
        }
    }

    public void ReleaseAll()
    {
        foreach (var button in _pressed.ToArray())
        {
            Up(button);
        }
    }
}

// A finger has no cursor to hide or lock; look-around works from position changes either way.
public sealed class IosCursor : ICursor
{
    public CursorType Type { get; set; }
    public StandardCursor StandardCursor { get; set; }
    public CursorMode CursorMode { get; set; }
    public bool IsConfined { get; set; }
    public int HotspotX { get; set; }
    public int HotspotY { get; set; }
    public Silk.NET.Core.RawImage Image { get; set; }
    public bool IsSupported(CursorMode mode) => true;
    public bool IsSupported(StandardCursor standardCursor) => false;
}
