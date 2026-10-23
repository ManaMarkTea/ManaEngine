using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>
    /// The render-target viewer's picture: the frame's intermediate targets drawn small, side by side, into one texture
    /// that the UI shows by <see cref="TextureId"/> (ManaUiDrawList.SetTexture). The backend fills it only on frames whose
    /// UI draws from it, so it costs nothing while no viewer is open.
    /// </summary>
    public static class ManaRenderTargetPreview
    {
        /// <summary>The UI texture id the preview is registered under.</summary>
        public const int TextureId = 0x52545056;

        public const int Columns = 3;
        public const int Rows = 2;
        public const int TileWidth = 480;
        public const int TileHeight = 270;
        public const int Width = Columns * TileWidth;
        public const int Height = Rows * TileHeight;

        /// <summary>What each tile shows, in tile order (left to right, then down) -- the order <c>target_preview.wgsl</c> draws them.</summary>
        public static readonly string[] Names =
        [
            "Scene colour (HDR, before tone map)",
            "Depth (white near, black at 1 km)",
            "Sun shadow, cascade 1 (nearest)",
            "Sun shadow, cascade 2",
            "Sun shadow, cascade 3 (farthest)",
            "Placed lights per screen tile (most in any slice)"
        ];

        /// <summary>A tile's place in the preview texture, as u0, v0, u1, v1.</summary>
        public static Vector4 Uv(int tile)
        {
            var column = tile % Columns;
            var row = tile / Columns;
            return new Vector4(
                column / (float)Columns, row / (float)Rows,
                (column + 1) / (float)Columns, (row + 1) / (float)Rows);
        }
    }
}
