using System.ComponentModel;
using Mana.Code.Content;
using Mana.Code.Nodes;
using ManaPixelFx;
using ManaPixelFx.Core;

namespace Mana.Code.Components
{
    /// <summary>
    /// Plays a pixel art effect -- a torch, an explosion, a pickup sparkle -- from the pixelfx library. An effect
    /// ported to C# (ManaPixelFx) is drawn live, so each one can have its own seed, palette and glow; the rest play
    /// from the sheet Tools/PixelFx/import_pixelfx.mjs made of them (Content/Sprites/PixelFx/). Loops play on and on;
    /// a one-shot plays, waits <see cref="RepeatDelay"/> and plays again, or once when <see cref="Repeat"/> is off.
    /// <see cref="ManaPixelFxSpawner.Spawn"/> puts one in the world from code and takes it away when it is done.
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

        /// <summary>Draw a ported effect live rather than from its sheet; off, or not ported yet, plays the sheet.</summary>
        [Category("Live")]
        public bool Live { get; set; } = true;

        /// <summary>A different take on a live effect; 0 keeps the effect's own.</summary>
        [Category("Live")]
        public int Seed { get; set; }

        /// <summary>Recolours a live effect: a pixelfx ramp by name (ice, arcane, holy, poison, neon...); empty keeps its own.</summary>
        [Category("Live")]
        public string Palette { get; set; } = string.Empty;

        /// <summary>How strongly a live effect's light shines (pixelfx's light option); above 1 blooms more.</summary>
        [Category("Live")]
        public float Glow { get; set; } = 1.0f;

        /// <summary>Take the node away once a one-shot is over -- for effects spawned in play. Never saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool RemoveWhenDone { get; set; }

        /// <summary>A one-shot that has played and will not again.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool Done { get; private set; }

        /// <summary>The ported effect drawing this one live, or null while it plays from its sheet.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public PixelEffect? LiveEffect => _live?.Effect;

        private double _seconds;
        private int _frame;
        private bool _removing;
        private ManaPixelFxLive? _live;
        private string? _liveKey;
        private MeshRendererComponent? _sheetLight;
        private string? _sheetLightKey;

        public ManaPixelFxComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Pixel FX";
            PixelArt = true;
        }

        public ManaSpriteSheet? Asset => ManaContentAssets.Load<ManaSpriteSheet>(Effect, ManaSpriteSheet.Extension);

        public override int FrameCount => Asset?.FrameCount ?? 1;

        protected override int CurrentFrame => _frame;

        // Soft glow and smoke: blended, not cut out at half.
        protected override ManaCore.Graphics.ManaOpacityMode OpacityMode => ManaCore.Graphics.ManaOpacityMode.Blend;

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

            if (SyncLive() is { } live)
            {
                live.Show(Phase(live.Effect.Meta.Duration, live.Effect.Meta.Loop), FlipX);
            }
            else
            {
                base.UpdateComponent();
                SyncSheetLight();
            }

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

        /// <summary>Steps the effect on, and works out the sheet frame it shows.</summary>
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
            // A one-shot ends empty, so its last frame stands in for nothing shown -- before it starts and between plays.
            if (Phase(length, animation.Loop) is { } time)
            {
                _frame = animation.FrameAt(time);
            }
            else
            {
                _frame = animation.Frames[^1];
            }
        }

        /// <summary>
        /// Seconds into the effect now, for one <paramref name="length"/> long, or null while nothing shows: before the
        /// delay is up, between repeats, and once a one-shot is over (which marks it done).
        /// </summary>
        private double? Phase(double length, bool loop)
        {
            var time = _seconds - Delay;
            if (time < 0.0)
            {
                if (loop)
                {
                    return time + length * Math.Ceiling(-time / length);
                }
                return null;
            }
            if (loop)
            {
                return time;
            }
            if (Repeat)
            {
                time %= length + Math.Max(RepeatDelay, 0.0f);
            }
            else if (time >= length)
            {
                Done = true;
            }
            if (time < length)
            {
                return time;
            }
            return null;
        }

        /// <summary>Builds, rebuilds or drops the live drawing as the settings ask; null while it plays from the sheet.</summary>
        private ManaPixelFxLive? SyncLive()
        {
            PixelEffect? effect = null;
            if (Live && Asset is { IsPixelFx: true } sheet)
            {
                effect = PixelEffects.Find(sheet.Source[ManaSpriteSheet.PixelFxSource.Length..]);
            }
            var key = $"{effect?.Meta.Id}|{Seed}|{Palette}|{Glow}|{Tint}|{Asset?.PixelsPerUnit}";
            if (effect == null || key != _liveKey)
            {
                _live?.Remove();
                _live = null;
                _liveKey = null;
            }
            if (effect == null)
            {
                return null;
            }
            if (_live == null)
            {
                // Coming off the sheet: its quads go, the live pair takes their place.
                if (SpriteQuad is { } sheetQuad)
                {
                    lock (Owner.Context.World.SceneLock)
                    {
                        Owner.Components.Remove(sheetQuad);
                    }
                    ForgetQuad();
                }
                RemoveSheetLight();
                _live = new ManaPixelFxLive(Owner, effect, Options(), Asset?.PixelsPerUnit ?? 20.0f, Tint);
                _liveKey = key;
            }
            return _live;
        }

        /// <summary>
        /// The sheet's light (pixelfx's glow) as a second quad added over the picture, on the same frame -- the GPU
        /// blends the colour and adds the light, as a live effect's are.
        /// </summary>
        private void SyncSheetLight()
        {
            var quad = SpriteQuad;
            var sheet = Asset;
            string? picture = null;
            if (quad != null && sheet != null && sheet.Light.Length > 0)
            {
                picture = ManaSpritePictures.Resolve(sheet.Light);
            }
            if (picture == null || sheet == null || quad == null)
            {
                RemoveSheetLight();
                return;
            }

            var key = $"{picture}|{sheet.Columns}|{sheet.Rows}|{sheet.PixelsPerUnit}|{sheet.PivotPoint}|{Tint}|{PixelArt}";
            if (key != _sheetLightKey || _sheetLight == null)
            {
                RemoveSheetLight();
                var columns = Math.Max(1, sheet.Columns);
                var rows = Math.Max(1, sheet.Rows);
                // A millimetre nearer the camera, so the light sorts after the colour it lands on.
                var mesh = new Mana.Code.Shapes.ManaSpriteMesh(Owner.Context, picture, null, FrameSize(picture, columns, rows, sheet.PixelsPerUnit), sheet.PivotPoint, Tint, PixelArt, 0.001f);
                mesh.Materials[0].Unlit = true;
                mesh.Materials[0].OpacityMode = ManaCore.Graphics.ManaOpacityMode.Additive;
                _sheetLight = new MeshRendererComponent(Owner, mesh);
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.Components.Add(_sheetLight);
                }
                _sheetLightKey = key;
            }
            _sheetLight.Material.UvScale = quad.Material.UvScale;
            _sheetLight.Material.UvOffset = quad.Material.UvOffset;
        }

        private void RemoveSheetLight()
        {
            if (_sheetLight == null)
            {
                return;
            }
            lock (Owner.Context.World.SceneLock)
            {
                Owner.Components.Remove(_sheetLight);
            }
            _sheetLight = null;
            _sheetLightKey = null;
        }

        private Dictionary<string, object> Options()
        {
            var options = new Dictionary<string, object> { ["light"] = (double)Glow };
            if (Seed != 0)
            {
                options["seed"] = (double)Seed;
            }
            if (Palette.Length > 0 && PixelColor.Pal.TryGetValue(Palette.Trim().ToLowerInvariant(), out var ramp))
            {
                options["palette"] = ramp;
            }
            return options;
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
    public static class ManaPixelFxSpawner
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
