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

        public ManaSpriteSheetComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Sprite Sheet";
        }

        /// <summary>The sheet asset, loaded and cached until its file changes.</summary>
        public ManaSpriteSheet? Asset => ManaContentAssets.Load<ManaSpriteSheet>(Sheet, ManaSpriteSheet.Extension);

        public override int FrameCount => Asset?.FrameCount ?? 1;

        protected override int CurrentFrame => PlayingFrame ?? Frame;

        public override void UpdateComponent()
        {
            Advance(ManaTime.GlobalTime.DeltaTime.TotalSeconds);
            base.UpdateComponent();
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
