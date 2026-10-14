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

        /// <summary>The block's size in metres, rounded to whole tiles as it is built. The node's scale multiplies it: scaling
        /// a 9-slice adds tiles rather than stretching them.</summary>
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

        /// <summary>One tile in metres, or null until the asset and its pictures are found -- the step an editor resize takes.</summary>
        public Vector2? TileSize => Asset is { } slice && ManaSpritePictures.Resolve(slice.AnyPiece) is { } sample
            ? FrameSize(sample, 1, 1, slice.PixelsPerUnit)
            : null;

        protected override string LayoutKey() =>
            $"{Slice.AssetId}|{Slice.AssetPath}|{(Asset is { } asset ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(asset) : 0)}|{Size}|{Pivot}|{Owner.Scale.X}|{Owner.Scale.Y}";

        protected override SpriteLayout? BuildLayout()
        {
            if (Asset is not { } slice || ManaSpritePictures.Resolve(slice.AnyPiece) is not { } sample)
            {
                return null;
            }

            var tile = FrameSize(sample, 1, 1, slice.PixelsPerUnit);
            // The quad is divided back by the node's scale, so the tiles keep their own size and the block only
            // grows or shrinks a whole tile at a time.
            var scale = new Vector2(MathF.Max(MathF.Abs(Owner.Scale.X), 1e-4f), MathF.Max(MathF.Abs(Owner.Scale.Y), 1e-4f));
            var columns = Math.Max(1, (int)MathF.Round(Size.X * scale.X / MathF.Max(tile.X, 1e-4f)));
            var rows = Math.Max(1, (int)MathF.Round(Size.Y * scale.Y / MathF.Max(tile.Y, 1e-4f)));
            var picture = ManaSpritePictures.JoinGrid(slice.Layout(columns, rows));
            var size = new Vector2(columns * tile.X / scale.X, rows * tile.Y / scale.Y);
            return picture == null ? null : new SpriteLayout(picture, null, 1, 1, size, Pivot, Vector2.One);
        }
    }
}
