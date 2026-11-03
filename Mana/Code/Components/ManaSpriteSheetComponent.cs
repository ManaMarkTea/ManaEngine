using System.ComponentModel;
using Mana.Code.Content;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Shows a sprite sheet asset (.manasheet): plays the named <see cref="Animation"/> from it, or holds one
    /// <see cref="Frame"/> when none is named. The sheet holds the pictures, their size and the animations, set up
    /// once in its own window and shared by everything drawn from it. Plays in the editor too.
    /// </summary>
    public sealed class ManaSpriteSheetComponent : ManaSpriteBase
    {
        [AssetKind(ManaSpriteSheet.Extension), Category("Sheet")]
        public ManaAssetRef Sheet { get; set; } = ManaAssetRef.None;

        /// <summary>The animation played, by name (idle, walk...); empty holds <see cref="Frame"/>.</summary>
        [Category("Sheet")]
        public string Animation { get; set; } = string.Empty;

        [Category("Sheet")]
        public bool Playing { get; set; } = true;

        /// <summary>The frame held when no animation plays, counted from 0.</summary>
        [Category("Sheet")]
        public int Frame { get; set; }

        /// <summary>The frame the animation is showing. Never saved, so a playing sprite does not make its level read as changed.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public int? PlayingFrame { get; private set; }

        private double _seconds;
        private string? _playedAnimation;
        private MeshRendererComponent? _light;
        private string? _lightKey;

        public ManaSpriteSheetComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite Sheet";
        }

        /// <summary>The sheet asset, loaded and cached until its file changes.</summary>
        public ManaSpriteSheet? Asset => ManaContentAssets.Load<ManaSpriteSheet>(Sheet, ManaSpriteSheet.Extension);

        public override int FrameCount => Asset?.FrameCount ?? 1;

        protected override int CurrentFrame => PlayingFrame ?? Frame;

        // A sheet with a light layer is soft art -- glow, smoke -- so it blends rather than cutting out at half.
        protected override ManaCore.Graphics.ManaOpacityMode OpacityMode
        {
            get
            {
                if (Asset is { Light.Length: > 0 })
                {
                    return ManaCore.Graphics.ManaOpacityMode.Blend;
                }
                return ManaCore.Graphics.ManaOpacityMode.Cutout;
            }
        }

        public override void UpdateComponent()
        {
            Advance(ManaTime.GlobalTime.DeltaTime.TotalSeconds);
            base.UpdateComponent();
            SyncLight();
        }

        /// <summary>
        /// The sheet's light (a baked pixel effect's glow) as a second quad added over the picture, on the same frame:
        /// the GPU blends the colour and adds the light, as a live effect's are.
        /// </summary>
        private void SyncLight()
        {
            var quad = SpriteQuad;
            var sheet = Asset;
            string? picture = null;
            if (quad != null && sheet is { Light.Length: > 0 })
            {
                picture = ManaSpritePictures.Resolve(sheet.Light);
            }
            if (picture == null || sheet == null || quad == null)
            {
                RemoveLight();
                return;
            }

            var key = $"{picture}|{sheet.Columns}|{sheet.Rows}|{sheet.PixelsPerUnit}|{sheet.PivotPoint}|{Tint}|{PixelArt}";
            if (key != _lightKey || _light == null)
            {
                RemoveLight();
                var columns = Math.Max(1, sheet.Columns);
                var rows = Math.Max(1, sheet.Rows);
                // A millimetre nearer the camera, so the light sorts after the colour it lands on.
                var mesh = new Shapes.ManaSpriteMesh(Owner.Context, picture, null, FrameSize(picture, columns, rows, sheet.PixelsPerUnit), sheet.PivotPoint, Tint, PixelArt, 0.001f);
                mesh.Materials[0].Unlit = true;
                mesh.Materials[0].OpacityMode = ManaCore.Graphics.ManaOpacityMode.Additive;
                _light = new MeshRendererComponent(Owner, mesh);
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.Components.Add(_light);
                }
                _lightKey = key;
            }
            _light.Material.UvScale = quad.Material.UvScale;
            _light.Material.UvOffset = quad.Material.UvOffset;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            RemoveLight();
        }

        private void RemoveLight()
        {
            if (_light == null)
            {
                return;
            }
            lock (Owner.Context.World.SceneLock)
            {
                Owner.Components.Remove(_light);
            }
            _light = null;
            _lightKey = null;
        }

        /// <summary>Steps the animation on; restarted whenever a different one is asked for.</summary>
        public void Advance(double seconds)
        {
            if (!string.Equals(_playedAnimation, Animation, StringComparison.OrdinalIgnoreCase))
            {
                _playedAnimation = Animation;
                _seconds = 0.0;
            }

            var animation = Animation.Length > 0 ? Asset?.Animation(Animation) : null;
            if (animation == null)
            {
                PlayingFrame = null;
                return;
            }
            if (Playing)
            {
                _seconds += seconds;
            }
            PlayingFrame = animation.FrameAt(_seconds);
        }

        // The asset object is replaced when its file changes, so its identity stands in for its contents.
        protected override string LayoutKey() => $"{Sheet.AssetId}|{Sheet.AssetPath}|{(Asset is { } asset ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(asset) : 0)}";

        protected override SpriteLayout? BuildLayout()
        {
            if (Asset is not { } sheet)
            {
                return null;
            }

            var framed = sheet.Frames.Count > 0;
            var picture = framed ? ManaSpritePictures.Join(sheet.Frames) : ManaSpritePictures.Resolve(sheet.Texture);
            if (picture == null)
            {
                return null;
            }
            var columns = framed ? sheet.Frames.Count : Math.Max(1, sheet.Columns);
            var rows = framed ? 1 : Math.Max(1, sheet.Rows);
            var normal = framed ? null : ManaSpritePictures.Resolve(sheet.NormalMap);
            return new SpriteLayout(picture, normal, columns, rows, FrameSize(picture, columns, rows, sheet.PixelsPerUnit), sheet.PivotPoint, Vector2.One);
        }
    }
}
