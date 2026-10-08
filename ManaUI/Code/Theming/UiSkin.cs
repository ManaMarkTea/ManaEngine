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

        /// <summary>The atlas the pictures were packed into, and its generation then. Other users of the atlas
        /// (the Import panel's thumbnails, when it fills) reset it, which leaves these UVs pointing at whatever
        /// was packed there next -- so a theme asks <see cref="Refresh"/> for a new skin when the generation moves.</summary>
        public ManaImageAtlas? Atlas { get; init; }

        public int Generation { get; init; }

        /// <summary>Builds the skin again into the current atlas; null leaves the theme flat.</summary>
        public Func<UiSkin?>? Refresh { get; init; }

        public bool IsStale => Atlas != null && Atlas.Generation != Generation;

        public UiSkin Add(string name, UiSkinSlice slice)
        {
            _slices[name] = slice;
            return this;
        }

        public bool TryGet(string name, out UiSkinSlice slice) => _slices.TryGetValue(name, out slice);
    }
}
