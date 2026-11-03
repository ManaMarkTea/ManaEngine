using System.ComponentModel;
using Mana.Code.Content;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Plays a pixel art effect -- a torch, an explosion, a pickup sparkle -- from a sheet made by
    /// Tools/PixelFx/import_pixelfx.mjs out of the pixelfx library (Content/Sprites/PixelFx/). Loops play on and on;
    /// a one-shot plays, waits <see cref="RepeatDelay"/> and plays again, or once when <see cref="Repeat"/> is off.
    /// <see cref="ManaPixelFx.Spawn"/> puts one in the world from code and takes it away when it is done.
    /// </summary>
    public sealed class ManaPixelFxComponent : ManaSpriteBase
    {
        [AssetKind(ManaSpriteSheet.Extension), Category("Effect")]
        public ManaAssetRef Effect { get; set; } = ManaAssetRef.None;

        [Category("Effect")]
        public bool Playing { get; set; } = true;

        /// <summary>1 plays at the speed it was drawn; 0.5 is slow motion.</summary>
        [Category("Effect")]
        public float Speed { get; set; } = 1.0f;

        /// <summary>A one-shot plays again after <see cref="RepeatDelay"/>; off plays it once. Loops always loop.</summary>
        [Category("Effect")]
        public bool Repeat { get; set; } = true;

        [Category("Effect")]
        public float RepeatDelay { get; set; } = 1.0f;

        /// <summary>Seconds before it first plays -- a row of them out of step.</summary>
        [Category("Effect")]
        public float Delay { get; set; }

        /// <summary>Turn about the up axis to face the camera, as flat effects in a 3D world should; side-on 2D faces it anyway.</summary>
        [Category("Effect")]
        public bool FaceCamera { get; set; } = true;

        /// <summary>Take the node away once a one-shot is over -- for effects spawned in play. Never saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool RemoveWhenDone { get; set; }

        /// <summary>A one-shot that has played and will not again.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool Done { get; private set; }

        private double _seconds;
        private int _frame;
        private bool _removing;

        public ManaPixelFxComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Pixel FX";
            PixelArt = true;
        }

        public ManaSpriteSheet? Asset => ManaContentAssets.Load<ManaSpriteSheet>(Effect, ManaSpriteSheet.Extension);

        public override int FrameCount => Asset?.FrameCount ?? 1;

        protected override int CurrentFrame => _frame;

        // Before the node's world transform, so turning to the camera lands in this frame's.
        public override bool UpdatesBeforeNodeTransform => FaceCamera;

        /// <summary>Plays it again from the start.</summary>
        public void Restart()
        {
            _seconds = 0.0;
            Done = false;
        }

        public override void UpdateComponent()
        {
            if (Playing && !ManaTime.GlobalTime.TimePaused)
            {
                Advance(ManaTime.GlobalTime.DeltaTime.TotalSeconds * Speed);
            }
            if (FaceCamera && Owner.Context.World.CameraNode is { } camera)
            {
                Owner.LocalTransform = FacingCamera(camera.WorldTransform.Translation);
            }
            base.UpdateComponent();

            if (Done && RemoveWhenDone && !_removing && Owner.Parent is { } parent)
            {
                // Not during the scene walk, which is going through the parent's children.
                _removing = true;
                var world = Owner.Context.World;
                world.PostUpdateFutures.Add(() =>
                {
                    lock (world.SceneLock)
                    {
                        parent.RemoveChild(Owner);
                    }
                });
            }
        }

        /// <summary>Steps the effect on, and works out the frame it shows.</summary>
        public void Advance(double seconds)
        {
            _seconds += Math.Max(seconds, 0.0);
            var animation = Asset?.Animations.FirstOrDefault();
            if (animation == null || animation.Frames.Count == 0)
            {
                _frame = 0;
                return;
            }

            var length = animation.Frames.Count / (double)Math.Max(animation.FramesPerSecond, 0.01f);
            var time = _seconds - Delay;
            // A one-shot ends empty, so its last frame stands in for nothing shown -- before it starts and between plays.
            var last = animation.Frames[^1];
            if (time < 0.0)
            {
                _frame = animation.Loop ? animation.FrameAt(time + length * Math.Ceiling(-time / length)) : last;
                return;
            }
            if (animation.Loop)
            {
                _frame = animation.FrameAt(time);
                return;
            }
            if (Repeat)
            {
                var period = length + Math.Max(RepeatDelay, 0.0f);
                time %= period;
            }
            else if (time >= length)
            {
                Done = true;
            }
            _frame = time < length ? animation.FrameAt(time) : last;
        }

        /// <summary>The node's local transform turned about the world's up so its face (+Z) looks at <paramref name="camera"/>.</summary>
        public Matrix4x4 FacingCamera(Vector3 camera)
        {
            var parent = Owner.Parent?.WorldTransform ?? Matrix4x4.Identity;
            var position = Vector3.Transform(Owner.Position, parent);
            var toward = camera - position;
            var yaw = 0.0f;
            if (toward.X * toward.X + toward.Z * toward.Z > 1e-8f)
            {
                yaw = MathF.Atan2(toward.X, toward.Z);
            }

            var parentTurn = Quaternion.Identity;
            if (Matrix4x4.Decompose(parent, out _, out var turn, out _))
            {
                parentTurn = turn;
            }
            var local = Quaternion.Inverse(parentTurn) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            return Matrix4x4.CreateScale(Owner.Scale)
                * Matrix4x4.CreateFromQuaternion(local)
                * Matrix4x4.CreateTranslation(Owner.Position);
        }

        protected override string LayoutKey() => $"{Effect.AssetId}|{Effect.AssetPath}|{(Asset is { } asset ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(asset) : 0)}";

        protected override SpriteLayout? BuildLayout()
        {
            if (Asset is not { } sheet || ManaSpritePictures.Resolve(sheet.Texture) is not { } picture)
            {
                return null;
            }
            var columns = Math.Max(1, sheet.Columns);
            var rows = Math.Max(1, sheet.Rows);
            return new SpriteLayout(picture, null, columns, rows, FrameSize(picture, columns, rows, sheet.PixelsPerUnit), sheet.PivotPoint, Vector2.One);
        }
    }

    /// <summary>Pixel effects put in the world from code: an explosion where a crate broke, a sparkle on a pickup.</summary>
    public static class ManaPixelFx
    {
        /// <summary>
        /// Plays <paramref name="effect"/> once at <paramref name="position"/> under <paramref name="parent"/> (in its
        /// frame) and removes it when it ends. Added after this frame's scene walk, so it is safe to call from any update.
        /// </summary>
        public static ManaPixelFxComponent Spawn(ManaSceneNode parent, ManaAssetRef effect, Vector3 position, bool flip = false, float scale = 1.0f)
        {
            var node = new ManaSceneNode(parent.Context, Matrix4x4.Identity)
            {
                Name = "Pixel FX",
                IsEditorTransient = true,
                Position = position,
                Scale = new Vector3(scale),
            };
            var fx = new ManaPixelFxComponent(node) { Effect = effect, Repeat = false, RemoveWhenDone = true, FlipX = flip };
            node.Components.Add(fx);
            var world = parent.Context.World;
            world.PostUpdateFutures.Add(() =>
            {
                lock (world.SceneLock)
                {
                    parent.AddChild(node);
                }
            });
            return fx;
        }
    }
}
