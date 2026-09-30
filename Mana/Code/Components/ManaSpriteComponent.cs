using System.ComponentModel;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// One picture on its node, sized from its pixels at <see cref="PixelsPerUnit"/>, lit by the scene (and bumped by
    /// <see cref="NormalMap"/>) unless unlit, and optionally repeated across <see cref="RepeatSize"/>. For frames,
    /// use a Sprite Sheet; for blocks built from corner and edge pieces, a Sprite 9-Slice.
    /// </summary>
    public sealed class ManaSpriteComponent : ManaSpriteBase
    {
        /// <summary>The picture: a PNG, as a repo path (Content/Sprites/hero.png) or inside a pack (zip:pack.zip/Sprites/x.png).</summary>
        [PicturePath, Category("Picture")]
        public string Texture { get; set; } = string.Empty;

        /// <summary>Optional normal map for the same picture, so lights shade it as if it had depth.</summary>
        [PicturePath, Category("Picture")]
        public string NormalMap { get; set; } = string.Empty;

        /// <summary>How many of the picture's pixels make a metre.</summary>
        [Category("Picture")]
        public float PixelsPerUnit { get; set; } = 64.0f;

        /// <summary>Where the node sits on the picture, 0..1 across and up: (0.5, 0) is its bottom middle.</summary>
        [Category("Picture")]
        public Vector2 Pivot { get; set; } = new(0.5f, 0.0f);

        /// <summary>Repeat the picture across this size in metres rather than drawing it once. Zero draws it once.</summary>
        [Category("Picture")]
        public Vector2 RepeatSize { get; set; }

        public ManaSpriteComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite";
        }

        protected override string LayoutKey() => $"{Texture}|{NormalMap}|{PixelsPerUnit}|{Pivot}|{RepeatSize}";

        protected override SpriteLayout? BuildLayout()
        {
            if (ManaSpritePictures.Resolve(Texture) is not { } picture)
            {
                return null;
            }

            var size = FrameSize(picture, 1, 1, PixelsPerUnit);
            var repeats = Vector2.One;
            if (RepeatSize.X > 0.0f && RepeatSize.Y > 0.0f)
            {
                repeats = RepeatSize / size;
                size = RepeatSize;
            }
            return new SpriteLayout(picture, ManaSpritePictures.Resolve(NormalMap), 1, 1, size, Pivot, repeats);
        }
    }
}
