namespace ManaUI
{
    public enum PointerButton
    {
        Left,
        Right,
        Middle
    }

    [Flags]
    public enum UiModifiers
    {
        None = 0,
        Shift = 1 << 0,
        Control = 1 << 1,
        Alt = 1 << 2,

        /// <summary>Windows key / Command key.</summary>
        Meta = 1 << 3
    }

    /// <summary>
    /// Base for routed events. <see cref="Handled"/> stops the walk, which is how a child claims a click
    /// its ancestors would otherwise also react to.
    /// </summary>
    public abstract class UiEventArgs : EventArgs
    {
        /// <summary>The element the event was originally targeted at, unchanged as the event routes.</summary>
        public UiElement? Source { get; internal set; }

        public bool Handled { get; set; }
    }

    public class UiPointerEventArgs : UiEventArgs
    {
        /// <summary>Pointer position in root (logical) coordinates.</summary>
        public Vector2 Position { get; internal set; }

        public PointerButton Button { get; internal set; }
        public UiModifiers Modifiers { get; internal set; }

        /// <summary>Consecutive clicks within the double-click window: 1 for single, 2 for double.</summary>
        public int ClickCount { get; internal set; } = 1;

        /// <summary>Position relative to an element's top-left corner.</summary>
        public Vector2 GetPosition(UiElement element)
        {
            return Position - element.Bounds.Position;
        }
    }

    public class UiPointerWheelEventArgs : UiPointerEventArgs
    {
        /// <summary>Scroll delta in notches; positive Y is scroll-up.</summary>
        public Vector2 Delta { get; internal set; }
    }

    public class UiKeyEventArgs : UiEventArgs
    {
        public UiKey Key { get; internal set; }
        public UiModifiers Modifiers { get; internal set; }

        /// <summary>True when the key event came from auto-repeat rather than a fresh press.</summary>
        public bool IsRepeat { get; internal set; }
    }

    /// <summary>Committed text. Separate from key events because one keystroke can produce zero or many characters.</summary>
    public class UiTextInputEventArgs : UiEventArgs
    {
        public string Text { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// Keys the UI reacts to. Deliberately not Silk.NET's enum -- ManaUI must not depend on a windowing
    /// library, so the host maps its platform keys onto this.
    /// </summary>
    public enum UiKey
    {
        None = 0,
        Left, Right, Up, Down,
        Home, End, PageUp, PageDown,
        Backspace, Delete, Insert,
        Enter, Escape, Tab, Space,
        A, C, D, S, V, X, Z, Y,
        F2, F4
    }
}
