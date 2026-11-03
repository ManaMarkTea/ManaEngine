using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Mana.Code.Nodes;
using ManaPixelFx;
using ManaPixelFx.Core;

namespace Mana.Code.Components
{
    /// <summary>Which point of a pixel effect sits on its node.</summary>
    public enum PixelFxAnchor
    {
        /// <summary>The lowest pixel it draws over a whole run: a torch, a brazier or a candle stands on the node.</summary>
        Bottom,

        /// <summary>pixelfx's own origin: the floor under a ground effect, a wall torch's bracket, an explosion's heart.</summary>
        Origin,
    }

    /// <summary>
    /// Plays a pixel art effect from the pixelfx library, ported to C# (ManaPixelFx) and drawn live: its options --
    /// palette ramps, seed, sizes -- are the effect's own, changed per instance in <see cref="Options"/>. Loops play on
    /// and on; a one-shot plays, waits <see cref="RepeatDelay"/> and plays again, or once when <see cref="Repeat"/> is
    /// off. The Inspector bakes one into a sprite sheet. <see cref="ManaPixelFxSpawner.Spawn"/> plays one from code.
    /// </summary>
    public sealed class ManaPixelFxComponent : ManaComponent
    {
        // A run's measured bounds, per effect and shape-changing options: drawing a run to measure it costs a few
        // dozen milliseconds, and recolouring never moves a pixel.
        private static readonly ConcurrentDictionary<string, (PixelBounds Color, PixelBounds All)> Bounds = new();

        private Dictionary<string, JsonElement> _options = new(StringComparer.Ordinal);
        private int _optionsRevision;
        private double _seconds;
        private bool _removing;
        private bool _facing;
        private ManaPixelFxLive? _live;
        private string? _liveKey;

        public ManaPixelFxComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Pixel FX";
        }

        /// <summary>The effect, by its pixelfx id: "fire/torch". <see cref="PixelEffects"/> lists them.</summary>
        [Category("Effect")]
        public string Effect { get; set; } = "fire/torch";

        /// <summary>The effect's own options changed on this one (palette, seed, height...); the rest keep their defaults.</summary>
        [Category("Effect"), Browsable(false)]
        public Dictionary<string, JsonElement> Options
        {
            get => _options;
            set
            {
                _options = new Dictionary<string, JsonElement>(value ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal);
                _optionsRevision++;
            }
        }

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

        /// <summary>Which of its points stands on the node.</summary>
        [Category("Placement")]
        public PixelFxAnchor Anchor { get; set; } = PixelFxAnchor.Bottom;

        /// <summary>Turn about the up axis to face the camera, as flat effects in a 3D world should; off, the node's own turn.</summary>
        [Category("Placement")]
        public bool FaceCamera { get; set; } = true;

        /// <summary>Its pixels a metre: 20 stands pixelfx's 38 px character about 1.9 m tall.</summary>
        [Category("Placement")]
        public float PixelsPerUnit { get; set; } = 20.0f;

        /// <summary>Mirror it left to right: a directional effect facing left.</summary>
        [Category("Look")]
        public bool FlipX { get; set; }

        [Category("Look")]
        public Vector4 Tint { get; set; } = Vector4.One;

        /// <summary>How strongly its light shines (pixelfx's light option); above 1 blooms more.</summary>
        [Category("Look")]
        public float Glow { get; set; } = 1.0f;

        /// <summary>Take the node away once a one-shot is over -- for effects spawned in play. Never saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool RemoveWhenDone { get; set; }

        /// <summary>A one-shot that has played and will not again.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public bool Done { get; private set; }

        /// <summary>Seconds into the effect it shows now, or null while nothing shows (waiting to start, between repeats, over).</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public double? ShownAt { get; private set; }

        /// <summary>The ported effect it names, or null for an id the library does not have.</summary>
        [System.Text.Json.Serialization.JsonIgnore, Browsable(false)]
        public PixelEffect? Definition => PixelEffects.Find(Effect);

        // Before the node's world transform, so turning to the camera lands in this frame's.
        public override bool UpdatesBeforeNodeTransform => FaceCamera || _facing;

        // ---- options
        /// <summary>An option's value now: this one's own when changed, else the effect's default (null when it has none).</summary>
        public object? OptionValue(string name)
        {
            if (_options.TryGetValue(name, out var value))
            {
                return FromJson(value);
            }
            if (Definition is { } effect && effect.Meta.Options.TryGetValue(name, out var fallback))
            {
                return fallback;
            }
            return null;
        }

        public bool IsOptionChanged(string name) => _options.ContainsKey(name);

        /// <summary>Changes one of the effect's options on this one; null puts it back to the effect's default.</summary>
        public void SetOption(string name, object? value)
        {
            if (value == null)
            {
                _options.Remove(name);
            }
            else
            {
                _options[name] = JsonSerializer.SerializeToElement(value, value.GetType());
            }
            _optionsRevision++;
        }

        /// <summary>The options handed to the effect: what this one changed, and its glow.</summary>
        public Dictionary<string, object> EffectOptions()
        {
            var options = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (key, value) in _options)
            {
                if (FromJson(value) is { } converted)
                {
                    options[key] = converted;
                }
            }
            options["light"] = (double)Glow;
            return options;
        }

        private static object? FromJson(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    return value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
                case JsonValueKind.Number:
                    return value.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                    return value.GetString();
                default:
                    return null;
            }
        }

        // ---- playing
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
            Turn();

            var live = SyncLive();
            if (live != null)
            {
                live.Show(ShownAt);
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

        /// <summary>Steps the effect on and works out the moment it shows.</summary>
        public void Advance(double seconds)
        {
            _seconds += Math.Max(seconds, 0.0);
            if (Definition is not { } effect)
            {
                ShownAt = null;
                return;
            }
            ShownAt = Phase(effect.Meta.Duration, effect.Meta.Loop);
        }

        /// <summary>
        /// Seconds into an effect <paramref name="length"/> long, or null while nothing shows: before the delay is up,
        /// between repeats, and once a one-shot is over (which marks it done).
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

        // Faces the camera while asked to; let go, the node goes back to the turn it was placed with.
        private void Turn()
        {
            if (FaceCamera && Owner.Context.World.CameraNode is { } camera)
            {
                Owner.LocalTransform = FacingCamera(camera.WorldTransform.Translation);
                _facing = true;
                return;
            }
            if (_facing)
            {
                _facing = false;
                Owner.LocalTransform = PlacedTransform();
            }
        }

        // The node as it was placed: its own scale, turn and position.
        private Matrix4x4 PlacedTransform() =>
            Matrix4x4.CreateScale(Owner.Scale) * Matrix4x4.CreateFromQuaternion(Owner.Rotation.ToQuaternionFromEuler()) * Matrix4x4.CreateTranslation(Owner.Position);

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

        /// <summary>Where the node sits on the effect's canvas, in pixels from its top left.</summary>
        public (double X, double Y) AnchorPixel()
        {
            if (Definition is not { } effect)
            {
                return (0, 0);
            }
            var (ox, oy) = effect.Meta.Origin;
            if (Anchor == PixelFxAnchor.Origin)
            {
                return (ox, oy);
            }
            var drawn = MeasureRun().Color;
            if (drawn.IsEmpty)
            {
                return (ox, oy);
            }
            return (ox, drawn.Y1 + 1);
        }

        /// <summary>What one run of the effect, with this one's options, draws -- measured once and kept.</summary>
        public (PixelBounds Color, PixelBounds All) MeasureRun()
        {
            if (Definition is not { } effect)
            {
                return (PixelBounds.Empty, PixelBounds.Empty);
            }
            var options = EffectOptions();
            var shape = string.Join(";", options.Where(pair => pair.Value is not string[]).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            return Bounds.GetOrAdd($"{effect.Meta.Id}|{shape}", _ => PixelFxRun.Measure(effect, options));
        }

        /// <summary>Builds, or rebuilds when a setting it depends on changed, the live drawing.</summary>
        private ManaPixelFxLive? SyncLive()
        {
            var effect = Definition;
            var key = $"{effect?.Meta.Id}|{_optionsRevision}|{Glow}|{Tint}|{PixelsPerUnit}|{Anchor}|{FlipX}";
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
                _live = new ManaPixelFxLive(Owner, effect, EffectOptions(), PixelsPerUnit, AnchorPixel(), MeasureRun().All, FlipX, Tint);
                _liveKey = key;
            }
            return _live;
        }

        /// <summary>Takes the live drawing off the node and gives it back its own turn -- removed, or baked.</summary>
        public override void OnRemoved()
        {
            _live?.Remove();
            _live = null;
            _liveKey = null;
            if (_facing)
            {
                _facing = false;
                Owner.LocalTransform = PlacedTransform();
            }
        }
    }

    /// <summary>Pixel effects put in the world from code: an explosion where a crate broke, a sparkle on a pickup.</summary>
    public static class ManaPixelFxSpawner
    {
        /// <summary>
        /// Plays the effect with pixelfx id <paramref name="effect"/> once with its origin at <paramref name="position"/>
        /// under <paramref name="parent"/> (in its frame), and removes it when it ends. Added after this frame's scene
        /// walk, so it is safe to call from any update.
        /// </summary>
        public static ManaPixelFxComponent Spawn(ManaSceneNode parent, string effect, Vector3 position, bool flip = false, float scale = 1.0f)
        {
            var node = new ManaSceneNode(parent.Context, Matrix4x4.Identity)
            {
                Name = "Pixel FX",
                IsEditorTransient = true,
                Position = position,
                Scale = new Vector3(scale),
            };
            var fx = new ManaPixelFxComponent(node) { Effect = effect, Repeat = false, RemoveWhenDone = true, FlipX = flip, Anchor = PixelFxAnchor.Origin };
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
