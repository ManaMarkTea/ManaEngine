using System.ComponentModel;
using Mana.Code.Content;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A block of any size built from a 9-slice asset (.manaslice): its corners at the corners, its edges along the
    /// edges and its middle filling the rest, in whole tiles -- a ground block or a platform in one node. The nine
    /// pieces are set up once in the asset; this only says how big.
    /// </summary>
    [DisplayName("Sprite 9-Slice")]
    public sealed class ManaSprite9SliceComponent : ManaSpriteBase
    {
        [AssetKind(ManaSpriteSlice.Extension), Category("Slice")]
        public ManaAssetRef Slice { get; set; } = ManaAssetRef.None;

        /// <summary>The block's size in metres, rounded to whole tiles as it is built.</summary>
        [Category("Slice")]
        public Vector2 Size { get; set; } = new(3.0f, 1.0f);

        /// <summary>Where the node sits on the block, 0..1 across and up: (0.5, 0) is its bottom middle.</summary>
        [Category("Slice")]
        public Vector2 Pivot { get; set; } = new(0.5f, 0.0f);

        public ManaSprite9SliceComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite 9-Slice";
        }

        public ManaSpriteSlice? Asset => ManaContentAssets.Load<ManaSpriteSlice>(Slice, ManaSpriteSlice.Extension);

        protected override string LayoutKey() =>
            $"{Slice.AssetId}|{Slice.AssetPath}|{(Asset is { } asset ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(asset) : 0)}|{Size}|{Pivot}";

        protected override SpriteLayout? BuildLayout()
        {
            if (Asset is not { } slice || ManaSpritePictures.Resolve(slice.AnyPiece) is not { } sample)
            {
                return null;
            }

            var tile = FrameSize(sample, 1, 1, slice.PixelsPerUnit);
            var columns = Math.Max(1, (int)MathF.Round(Size.X / MathF.Max(tile.X, 1e-4f)));
            var rows = Math.Max(1, (int)MathF.Round(Size.Y / MathF.Max(tile.Y, 1e-4f)));
            var picture = ManaSpritePictures.JoinGrid(slice.Layout(columns, rows));
            return picture == null ? null : new SpriteLayout(picture, null, 1, 1, Size, Pivot, Vector2.One);
        }
    }
}
