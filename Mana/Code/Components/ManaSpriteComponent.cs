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
        /// <summary>The picture: a PNG, as a repo path (Content/Sprites/hero.png).</summary>
        public string Texture { get; set; } = string.Empty;

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

        /// <summary>Keep each pixel a crisp square rather than blending neighbours -- off for painted art.</summary>
        public bool PixelArt { get; set; } = true;

        private MeshRendererComponent? _renderer;
        private string? _builtKey;

        public ManaSpriteComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite";
        }

        public int FrameCount => Math.Max(1, Columns) * Math.Max(1, Rows);

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Refresh();
        }

        /// <summary>Builds or rebuilds the quad when the picture or its size changed, and shows the current frame.</summary>
        public void Refresh()
        {
            if (string.IsNullOrWhiteSpace(Texture))
            {
                return;
            }

            var columns = Math.Max(1, Columns);
            var rows = Math.Max(1, Rows);
            var key = $"{Texture}|{NormalMap}|{PixelsPerUnit}|{Pivot}|{columns}|{rows}|{Tint}|{PixelArt}";
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
            var material = _renderer.Material;
            material.UvScale = new Vector2((FlipX ? -1.0f : 1.0f) / columns, 1.0f / rows);
            // Rows are counted from the top of the sheet, and v runs up it (see ManaSpriteMesh).
            material.UvOffset = new Vector2((column + (FlipX ? 1 : 0)) / (float)columns, (rows - 1 - row) / (float)rows);
        }

        private void Rebuild(int columns, int rows)
        {
            var texturePath = ManaTexture.ResolveTexturePath(Texture);
            var normalPath = string.IsNullOrWhiteSpace(NormalMap) ? null : ManaTexture.ResolveTexturePath(NormalMap);
            var pixels = PngSize(texturePath) ?? new Vector2(PixelsPerUnit, PixelsPerUnit);
            var ppu = MathF.Max(PixelsPerUnit, 0.01f);
            var size = new Vector2(pixels.X / columns / ppu, pixels.Y / rows / ppu);

            var mesh = new ManaSpriteMesh(Owner.Context, texturePath, File.Exists(normalPath ?? "") ? normalPath : null, size, Pivot, Tint, PixelArt);
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
