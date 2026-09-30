using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Draws a picture on its node: a flat quad facing +Z, sized from the picture at <see cref="PixelsPerUnit"/>,
    /// lit by the scene like any surface and bumped by <see cref="NormalMap"/> when it has one. A sheet of frames
    /// is <see cref="Columns"/> x <see cref="Rows"/>, and <see cref="Frame"/> -- counted across, then down -- picks
    /// one (a SpriteAnimation plays them). A script, so a sprite saves with a level and a prefab can be one.
    /// <para>
    /// The quad is a mesh renderer on the node itself rather than on a hidden child, so picking, bounds and a Glow
    /// all treat the sprite as the thing it is. It is rebuilt from these properties, never saved.
    /// </para>
    /// </summary>
    public sealed class ManaSpriteComponent : ManaComponent
    {
        /// <summary>The picture: a PNG, as a repo path (Content/Sprites/hero.png) or inside a pack (zip:pack.zip/Sprites/x.png).</summary>
        public string Texture { get; set; } = string.Empty;

        /// <summary>
        /// Separate pictures, one per frame, instead of one sheet -- how packs like Kenney's ship animation.
        /// When set they are the sheet, one frame each, and <see cref="Texture"/>, columns and rows are unused.
        /// </summary>
        public string[] Frames { get; set; } = [];

        /// <summary>Pictures drawn once at the ends of a tiled sprite, with <see cref="Texture"/> repeated between -- a platform's rounded edges.</summary>
        public string LeftEnd { get; set; } = string.Empty;
        public string RightEnd { get; set; } = string.Empty;

        /// <summary>Optional normal map for the same picture, so lights shade it as if it had depth.</summary>
        public string NormalMap { get; set; } = string.Empty;

        /// <summary>How many of the picture's pixels make a metre.</summary>
        public float PixelsPerUnit { get; set; } = 32.0f;

        /// <summary>Where the node sits on the picture, 0..1 across and up: (0.5, 0) is its bottom middle.</summary>
        public Vector2 Pivot { get; set; } = new(0.5f, 0.0f);

        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;

        /// <summary>The frame shown, counted across the sheet then down, from 0.</summary>
        public int Frame { get; set; }

        /// <summary>
        /// The frame an animation is showing, over <see cref="Frame"/>. Never saved, so a sprite playing in the
        /// editor does not make its level read as changed every frame.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int? PlayingFrame { get; set; }

        /// <summary>Mirror it left to right -- a character turning round.</summary>
        public bool FlipX { get; set; }

        public Vector4 Tint { get; set; } = Vector4.One;

        /// <summary>
        /// Draw the picture repeated across this size in metres rather than once at its own -- a long ledge from
        /// one brick tile. Zero (the default) draws it once. Single-frame pictures only: a sheet cannot repeat one frame.
        /// </summary>
        public Vector2 TiledSize { get; set; }

        /// <summary>Keep each pixel a crisp square rather than blending neighbours -- off for painted art.</summary>
        public bool PixelArt { get; set; } = true;

        /// <summary>Show the picture as drawn, whatever the lights -- flat art. Off lets lamps and the sun shade it.</summary>
        public bool Unlit { get; set; }

        private MeshRendererComponent? _renderer;
        private string? _builtKey;
        private Vector2 _repeats = Vector2.One;
        private Vector2 _pixels = Vector2.One;

        public ManaSpriteComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite";
        }

        public int FrameCount => Frames.Length > 0 ? Frames.Length : Math.Max(1, Columns) * Math.Max(1, Rows);

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Refresh();
        }

        /// <summary>Builds or rebuilds the quad when the picture or its size changed, and shows the current frame.</summary>
        public void Refresh()
        {
            if (string.IsNullOrWhiteSpace(Texture) && Frames.Length == 0)
            {
                return;
            }

            var columns = Frames.Length > 0 ? Frames.Length : Math.Max(1, Columns);
            var rows = Frames.Length > 0 ? 1 : Math.Max(1, Rows);
            var key = $"{Texture}|{string.Join(',', Frames)}|{LeftEnd}|{RightEnd}|{NormalMap}|{PixelsPerUnit}|{Pivot}|{columns}|{rows}|{Tint}|{PixelArt}|{Unlit}|{TiledSize}";
            if (key != _builtKey || _renderer == null)
            {
                Rebuild(columns, rows);
                _builtKey = key;
            }

            if (_renderer == null)
            {
                return;
            }

            var shown = PlayingFrame ?? Frame;
            var frame = ((shown % FrameCount) + FrameCount) % FrameCount;
            var column = frame % columns;
            var row = frame / columns;
            // Repeats across a tiled size ride on the UV scale, which the sampler wraps.
            var repeats = _repeats;
            // Half a texel in from each edge of a frame that does not repeat: smooth sampling would otherwise
            // blend in the neighbouring frame, or wrap the far edge round -- a faint line along the border.
            var inset = new Vector2(
                repeats.X == 1.0f && !PixelArt ? 0.5f / MathF.Max(_pixels.X, 1.0f) : 0.0f,
                repeats.Y == 1.0f && !PixelArt ? 0.5f / MathF.Max(_pixels.Y, 1.0f) : 0.0f);
            var cell = new Vector2(repeats.X / columns - 2.0f * inset.X, repeats.Y / rows - 2.0f * inset.Y);
            var corner = new Vector2((float)column / columns + inset.X, (float)(rows - 1 - row) / rows + inset.Y);
            var material = _renderer.Material;
            // Rows are counted from the top of the sheet, and v runs up it (see ManaSpriteMesh).
            material.UvScale = new Vector2(FlipX ? -cell.X : cell.X, cell.Y);
            material.UvOffset = new Vector2(FlipX ? corner.X + cell.X : corner.X, corner.Y);
        }

        private void Rebuild(int columns, int rows)
        {
            var texturePath = (Frames.Length > 0 ? ManaSpritePictures.Join(Frames) : ManaSpritePictures.Resolve(Texture))
                ?? ManaTexture.ResolveTexturePath(Texture);
            var normalPath = string.IsNullOrWhiteSpace(NormalMap) ? null : ManaSpritePictures.Resolve(NormalMap);
            var pixels = PngSize(texturePath) ?? new Vector2(PixelsPerUnit, PixelsPerUnit);
            _pixels = pixels;
            var ppu = MathF.Max(PixelsPerUnit, 0.01f);
            var size = new Vector2(pixels.X / columns / ppu, pixels.Y / rows / ppu);
            _repeats = Vector2.One;
            if (IsTiled(columns, rows))
            {
                if (HasEnds && JoinEnds(size) is { } joined)
                {
                    // The ends are part of the picture now, joined once, so it is drawn whole rather than repeated.
                    texturePath = joined;
                    normalPath = null;
                    _pixels = PngSize(joined) ?? _pixels;
                }
                else
                {
                    _repeats = TiledSize / size;
                }
                size = TiledSize;
            }

            var mesh = new ManaSpriteMesh(Owner.Context, texturePath, File.Exists(normalPath ?? "") ? normalPath : null, size, Pivot, Tint, PixelArt);
            mesh.Materials[0].Unlit = Unlit;
            lock (Owner.Context.World.SceneLock)
            {
                if (_renderer != null)
                {
                    Owner.Components.Remove(_renderer);
                }
                _renderer = new MeshRendererComponent(Owner, mesh);
                Owner.Components.Add(_renderer);
            }
        }

        private bool HasEnds => !string.IsNullOrWhiteSpace(LeftEnd) || !string.IsNullOrWhiteSpace(RightEnd);

        // Left end, the middle as many times as fits between, right end: whole tiles, as near the tiled size as they come.
        private string? JoinEnds(Vector2 tile)
        {
            var ends = new[] { LeftEnd, RightEnd }.Count(end => !string.IsNullOrWhiteSpace(end));
            var count = Math.Max((int)MathF.Round(TiledSize.X / MathF.Max(tile.X, 1e-4f)), ends + 1);
            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(LeftEnd))
            {
                pieces.Add(LeftEnd);
            }
            pieces.AddRange(Enumerable.Repeat(Texture, count - ends));
            if (!string.IsNullOrWhiteSpace(RightEnd))
            {
                pieces.Add(RightEnd);
            }
            var rows = Math.Max(1, (int)MathF.Round(TiledSize.Y / MathF.Max(tile.Y, 1e-4f)));
            return ManaSpritePictures.Join(pieces, rows);
        }

        private bool IsTiled(int columns, int rows) => TiledSize.X > 0.0f && TiledSize.Y > 0.0f && columns == 1 && rows == 1;

        /// <summary>A PNG's pixel size, read from its header, or null when it is not one.</summary>
        public static Vector2? PngSize(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[24];
                if (stream.Read(header) < 24 || header[0] != 0x89 || header[1] != (byte)'P')
                {
                    return null;
                }
                var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return new Vector2(width, height);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
