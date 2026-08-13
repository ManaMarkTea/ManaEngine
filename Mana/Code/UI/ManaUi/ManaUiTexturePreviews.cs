using ManaUI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>Where a texture's thumbnail sits in the UI's colour atlas.</summary>
    public readonly struct ManaUiTexturePreview
    {
        public ManaUiTexturePreview(Vector4 uv, Vector2 size, uint sourceWidth, uint sourceHeight)
        {
            Uv = uv;
            Size = size;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public Vector4 Uv { get; }

        /// <summary>Thumbnail size in pixels, aspect preserved.</summary>
        public Vector2 Size { get; }

        /// <summary>The texture's real dimensions, which are worth showing beside the preview.</summary>
        public uint SourceWidth { get; }
        public uint SourceHeight { get; }
    }

    /// <summary>
    /// Thumbnails of engine textures, packed into ManaUI's colour atlas so an inspector can show what a
    /// material is actually wearing.
    /// <para>
    /// Lives in the engine layer rather than in ManaUI, because it is the half that knows what a
    /// <see cref="ManaTexture"/> is. ManaUI is handed finished pixels and a rectangle, which is the whole
    /// reason it can stay free of any graphics dependency.
    /// </para>
    /// <para>
    /// Cached hard. Building one means decoding an image that may be several thousand pixels square, and a
    /// material with six channels would otherwise pay that on every rebuild of the panel -- which is every
    /// time the selection changes.
    /// </para>
    /// </summary>
    public static class ManaUiTexturePreviews
    {
        /// <summary>Longest edge of a thumbnail. Small enough that a few hundred fit the atlas.</summary>
        private const int ThumbnailSize = 64;

        private static readonly Dictionary<(ManaTexture Texture, int Generation), ManaUiTexturePreview?> Cache = new();

        /// <summary>
        /// A thumbnail for a texture, decoding and packing it on first request. Returns false for a texture
        /// with no readable pixels, which is a normal state -- a material can name a file that is not there.
        /// </summary>
        public static bool TryGet(ManaImageAtlas atlas, ManaTexture? texture, out ManaUiTexturePreview preview)
        {
            preview = default;

            if (atlas == null || texture == null)
            {
                return false;
            }

            // The generation is part of the key because a reset atlas invalidates every UV in it, and a
            // stale one would draw whatever landed in that rectangle afterwards.
            var key = (texture, atlas.Generation);

            if (Cache.TryGetValue(key, out var cached))
            {
                if (cached == null)
                {
                    return false;
                }

                preview = cached.Value;
                return true;
            }

            var built = Build(atlas, texture);
            Cache[key] = built;

            if (built == null)
            {
                return false;
            }

            preview = built.Value;
            return true;
        }

        private static ManaUiTexturePreview? Build(ManaImageAtlas atlas, ManaTexture texture)
        {
            try
            {
                // A texture uploaded straight to the GPU has no CPU copy, so one is read on demand. This is
                // the expensive part and the reason for the cache.
                if (texture.PixelData is not { Length: > 0 })
                {
                    texture.LoadCpuPixelData();
                }

                if (texture.PixelData is not { Length: > 0 } || texture.Width == 0 || texture.Height == 0)
                {
                    return null;
                }

                var width = (int)texture.Width;
                var height = (int)texture.Height;

                if (texture.PixelData.Length < width * height * 4)
                {
                    // Not the RGBA8 this expects -- a single-channel or compressed texture, say.
                    return null;
                }

                using var image = Image.LoadPixelData<Rgba32>(texture.PixelData, width, height);

                var scale = ThumbnailSize / (float)Math.Max(width, height);
                var thumbWidth = Math.Max(1, (int)MathF.Round(width * MathF.Min(1.0f, scale)));
                var thumbHeight = Math.Max(1, (int)MathF.Round(height * MathF.Min(1.0f, scale)));

                image.Mutate(context =>
                {
                    context.Resize(thumbWidth, thumbHeight);

                    // LoadCpuPixelData flips on the way in, because that is what a GL texture coordinate
                    // wants. A thumbnail is looked at rather than sampled, so it is flipped back.
                    context.Flip(FlipMode.Vertical);
                });

                var pixels = new byte[thumbWidth * thumbHeight * 4];
                image.CopyPixelDataTo(pixels);

                var uv = atlas.Add(pixels, thumbWidth, thumbHeight);
                if (uv == null)
                {
                    return null;
                }

                return new ManaUiTexturePreview(
                    uv.Value,
                    new Vector2(thumbWidth, thumbHeight),
                    texture.Width,
                    texture.Height);
            }
            catch (Exception)
            {
                // A texture that cannot be decoded is one missing preview, not a broken inspector.
                return null;
            }
        }

        /// <summary>Drops every cached thumbnail. For a scene reload, where the textures themselves are gone.</summary>
        public static void Clear()
        {
            Cache.Clear();
        }
    }
}
