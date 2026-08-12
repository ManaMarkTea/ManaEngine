namespace ManaUI
{
    public enum HorizontalAlignment
    {
        Stretch,
        Left,
        Center,
        Right
    }

    public enum VerticalAlignment
    {
        Stretch,
        Top,
        Center,
        Bottom
    }

    public enum Orientation
    {
        Vertical,
        Horizontal
    }

    public enum Dock
    {
        Left,
        Top,
        Right,
        Bottom
    }

    /// <summary>
    /// How an element participates in layout when hidden. Collapsed elements measure to zero and give
    /// their space back; Hidden ones keep their slot.
    /// </summary>
    public enum Visibility
    {
        Visible,
        Hidden,
        Collapsed
    }

    public enum TextAlignment
    {
        Left,
        Center,
        Right
    }

    public enum TextWrapping
    {
        NoWrap,
        Wrap
    }

    public enum TextTrimming
    {
        None,
        CharacterEllipsis
    }
}
