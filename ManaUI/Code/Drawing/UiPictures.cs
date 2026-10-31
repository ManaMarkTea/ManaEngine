namespace ManaUI
{
    /// <summary>A picture in the colour atlas, with its size in pixels.</summary>
    public readonly record struct UiPicture(Vector4 Uv, Vector2 Size);

    /// <summary>
    /// Pictures by path, for what a layout file names. ManaUI has no idea where files live or how they are decoded,
    /// so the engine layer supplies the lookup; with none (headless) nothing is found and the elements stay empty.
    /// </summary>
    public static class UiPictures
    {
        /// <summary>Finds a path's picture in an atlas, adding it the first time; null while it cannot be had.</summary>
        public static Func<ManaImageAtlas, string, UiPicture?>? Resolver { get; set; }

        public static UiPicture? Get(UiElement element, string? path)
        {
            if (string.IsNullOrEmpty(path) || Resolver is not { } resolver || element.Root?.Images is not { } atlas)
            {
                return null;
            }

            return resolver(atlas, path);
        }
    }
}
