namespace ManaUI
{
    /// <summary>One picture in the colour atlas that a control stretches as a nine-slice: its corners stay put, its edges and middle stretch.</summary>
    public readonly record struct UiSkinSlice(Vector4 Uv, Vector2 Size, Thickness Insets);

    /// <summary>
    /// Pictures a theme can dress controls in, by name ("button", "panel", "titleBar", "track", "fill", "primary").
    /// A control draws its slice when the theme has one and falls back to flat colour when not, so a theme with
    /// no skin -- or a skin whose pictures could not be loaded -- looks like any other.
    /// </summary>
    public sealed class UiSkin
    {
        private readonly Dictionary<string, UiSkinSlice> _slices = new(StringComparer.OrdinalIgnoreCase);

        public UiSkin Add(string name, UiSkinSlice slice)
        {
            _slices[name] = slice;
            return this;
        }

        public bool TryGet(string name, out UiSkinSlice slice) => _slices.TryGetValue(name, out slice);
    }
}
