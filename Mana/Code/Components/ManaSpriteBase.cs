using System.ComponentModel;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace Mana.Code.Components
{
    /// <summary>
    /// What every sprite shares: a flat quad facing +Z drawn by a mesh renderer on its own node, the look settings,
    /// and showing one frame of its picture. Each kind -- one picture, a sheet, a 9-slice -- says only what its
    /// picture is (<see cref="BuildLayout"/>), and what would make it different (<see cref="LayoutKey"/>).
    /// <para>
    /// The quad sits on the node itself rather than a hidden child, so picking, bounds and a Glow treat the sprite
    /// as the thing it is. It is rebuilt from the properties, never saved.
    /// </para>
    /// </summary>
    public abstract class ManaSpriteBase : ManaComponent
    {
        /// <summary>Mirror it left to right -- a character turning round.</summary>
        [Category("Look")]
        public bool FlipX { get; set; }

        [Category("Look")]
        public Vector4 Tint { get; set; } = Vector4.One;

        /// <summary>Keep each pixel a crisp square rather than blending neighbours -- off for smooth art like Kenney's.</summary>
        [Category("Look")]
        public bool PixelArt { get; set; }

        /// <summary>Show the picture as drawn, whatever the lights -- flat art, as 2D sprites usually are. Off lets lamps and the sun shade it.</summary>
        [Category("Look")]
        public bool Unlit { get; set; } = true;

        /// <summary>
        /// While lit, give a picture with no normal map one made from its outline, so it shades round rather than flat.
        /// Off by default: the made maps don't look good enough yet (Todo.md).
        /// </summary>
        [Category("Look")]
        public bool AutoNormalMap { get; set; }

        /// <summary>How far in from the picture's edge the made normal map rounds over, in the picture's pixels.</summary>
        [Category("Look")]
        public float BevelPixels { get; set; } = 6.0f;

        /// <summary>How deep the made normal map creases where colours meet inside the picture -- 0 shapes only the outline.</summary>
        [Category("Look")]
        public float InnerEdges { get; set; } = 0.35f;

        /// <summary>A picture laid out for drawing: its file, how many frames across and down, its size in metres, and repeats.</summary>
        protected readonly record struct SpriteLayout(string Picture, string? NormalMap, int Columns, int Rows, Vector2 Size, Vector2 Pivot, Vector2 Repeats);

        private MeshRendererComponent? _renderer;
        private string? _builtKey;
        private string? _missingKey;
        private long _retryAt;
        private SpriteLayout _layout;
        private Vector2 _pixels = Vector2.One;

        protected ManaSpriteBase(ManaSceneNode owner) : base(owner)
        {
        }

        /// <summary>How many frames the picture has; one unless it is a sheet.</summary>
        public virtual int FrameCount => 1;

        /// <summary>The frame to show now, counted across then down.</summary>
        protected virtual int CurrentFrame => 0;

        /// <summary>How the picture's alpha is used: cut out at half by default; soft art (a glow, smoke) blends instead.</summary>
        protected virtual ManaCore.Graphics.ManaOpacityMode OpacityMode => ManaCore.Graphics.ManaOpacityMode.Cutout;

        /// <summary>Everything the picture depends on, cheaply -- checked every frame; a change rebuilds the quad.</summary>
        protected abstract string LayoutKey();

        /// <summary>The picture and its layout, worked out when <see cref="LayoutKey"/> changes; null for nothing to draw yet.</summary>
        protected abstract SpriteLayout? BuildLayout();

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Refresh();
        }

        /// <summary>Builds or rebuilds the quad when the picture or its size changed, and shows the current frame.</summary>
        public void Refresh()
        {
            var key = $"{LayoutKey()}|{Tint}|{PixelArt}|{Unlit}|{AutoNormalMap}|{BevelPixels}|{InnerEdges}";
            if ((key != _builtKey || _renderer == null) && (key != _missingKey || Environment.TickCount64 >= _retryAt))
            {
                if (BuildLayout() is not { } layout)
                {
                    // Not there yet -- an asset still being made, a pack not yet dropped in: asked again in a second.
                    _missingKey = key;
                    _retryAt = Environment.TickCount64 + 1000;
                    return;
                }
                _builtKey = key;
                _missingKey = null;
                _layout = layout;
                Rebuild(layout);
            }

            if (_renderer == null)
            {
                return;
            }

            var columns = Math.Max(1, _layout.Columns);
            var rows = Math.Max(1, _layout.Rows);
            var count = columns * rows;
            var frame = ((CurrentFrame % count) + count) % count;
            var column = frame % columns;
            var row = frame / columns;
            // Repeats ride on the UV scale, which the sampler wraps. Half a texel in from each edge that does not
            // repeat: smooth sampling would otherwise blend in the neighbouring frame, or wrap the far edge round.
            var repeats = _layout.Repeats;
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

        /// <summary>The quad drawing the picture, once built -- for a kind that lays a second one over it in step.</summary>
        protected MeshRendererComponent? SpriteQuad => _renderer;

        public override void OnRemoved()
        {
            if (_renderer != null)
            {
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.Components.Remove(_renderer);
                }
            }
            _renderer = null;
            _builtKey = null;
        }

        /// <summary>The size of one frame in metres, from the picture's pixels -- for a kind working out its layout.</summary>
        protected static Vector2 FrameSize(string picture, int columns, int rows, float pixelsPerUnit)
        {
            var pixels = PngSize(picture) ?? new Vector2(pixelsPerUnit, pixelsPerUnit);
            var ppu = MathF.Max(pixelsPerUnit, 0.01f);
            return new Vector2(pixels.X / Math.Max(1, columns) / ppu, pixels.Y / Math.Max(1, rows) / ppu);
        }

        private void Rebuild(SpriteLayout layout)
        {
            _pixels = PngSize(layout.Picture) ?? Vector2.One;
            var normal = layout.NormalMap != null && File.Exists(layout.NormalMap) ? layout.NormalMap : null;
            // Unlit art ignores normals, so nothing is made for it.
            if (normal == null && AutoNormalMap && !Unlit)
            {
                normal = ManaSpriteNormals.For(layout.Picture, BevelPixels, InnerEdges);
            }
            var mesh = new ManaSpriteMesh(Owner.Context, layout.Picture, normal, layout.Size, layout.Pivot, Tint, PixelArt);
            mesh.Materials[0].Unlit = Unlit;
            mesh.Materials[0].OpacityMode = OpacityMode;
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
        public static Vector2? PngSize(string path) =>
            ManaCore.Imaging.ManaImageCodec.Size(path) is { } size ? new Vector2(size.Width, size.Height) : null;
    }
}
