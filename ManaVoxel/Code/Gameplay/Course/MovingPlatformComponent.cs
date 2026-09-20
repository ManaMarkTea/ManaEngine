using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// Slides its node back and forth between where it was placed and <see cref="Travel"/> away, carrying
    /// the player when they are standing on it.
    /// <para>
    /// The motion is a pure function of time spent playing, so it is repeatable and testable, and the node is
    /// put back where it was placed when play stops -- the editor never sees it move. Carrying is done by
    /// shifting the player by the platform's own step rather than leaning on contact friction, because the
    /// pawn sets its own velocity every frame and would simply shrug friction off.
    /// </para>
    /// </summary>
    public sealed class MovingPlatformComponent : ManaComponent
    {
        public const string ScriptType = "MovingPlatform";

        // How far past the surface a foot can be and still count as standing on it: the sliver of air a
        // grounded capsule hovers in, and a little slop for the platform having just moved.
        private const float StandBelow = 0.35f;
        private const float StandAbove = 0.3f;
        private const float EdgeMargin = 0.3f;

        private static readonly Vector4 MarkerColor = new(0.75f, 0.4f, 1.0f, 1.0f);

        private readonly EditorVolumeMarker _destination;

        private Vector3 _start;
        private double _elapsedSeconds;
        private bool _audioMoving;

        public MovingPlatformComponent(ManaSceneNode owner) : base(owner)
        {
            _destination = new EditorVolumeMarker(owner, "Platform End");
            _start = owner.Position;
        }

        public override bool UpdatesBeforeNodeTransform => true;

        /// <summary>How far the far end is from the placed position, in world axes.</summary>
        public Vector3 Travel { get; set; } = new(0.0f, 0.0f, 8.0f);

        public float Speed { get; set; } = 3.0f;

        /// <summary>How long it waits at each end.</summary>
        public float PauseSeconds { get; set; } = 1.0f;

        /// <summary>Delays the start, so a row of platforms is not in lockstep.</summary>
        public float PhaseSeconds { get; set; }

        /// <summary>The standing surface, used when the node has no collision box to measure.</summary>
        public Vector3 SurfaceSize { get; set; } = new(3.0f, 0.5f, 3.0f);

        [JsonIgnore]
        public bool IsCarryingPlayer { get; private set; }

        /// <summary>The far-end ghost the editor shows, or null before the first tick. Exposed for tests.</summary>
        public ManaSceneNode? DestinationMarker => _destination.Node;

        public override void BeginPlay()
        {
            base.BeginPlay();
            _start = Owner.Position;
            _elapsedSeconds = 0.0;
            _audioMoving = false;
            IsCarryingPlayer = false;
            _destination.Update(false, SurfaceSize, Vector3.Zero, MarkerColor);
        }

        public override void EndPlay()
        {
            _audioMoving = false;
            Mana.Code.Audio.ManaAudioCueRouter.StopAt(Owner, ManaCore.Audio.ManaSoundCue.PlatformMove);
            Owner.Position = _start;
            _elapsedSeconds = 0.0;
            IsCarryingPlayer = false;
            base.EndPlay();
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            var world = Owner.Context.World;
            if (!world.IsPlaying)
            {
                ShowDestination();
                return;
            }

            _elapsedSeconds += ManaTime.GlobalTime.DeltaTime.TotalSeconds;

            var fraction = FractionAt(Travel.Length(), Speed, PauseSeconds, (float)_elapsedSeconds + PhaseSeconds);
            var target = _start + (Travel * fraction);
            var delta = target - Owner.Position;
            bool moving = delta.LengthSquared() > 1e-10f;
            if (ManaTime.GlobalTime.DeltaTime.TotalSeconds > 0 && moving != _audioMoving)
            {
                if (moving)
                {
                    Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.PlatformMove);
                }
                else
                {
                    Mana.Code.Audio.ManaAudioCueRouter.StopAt(Owner, ManaCore.Audio.ManaSoundCue.PlatformMove);
                    Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.PlatformStop);
                }
                _audioMoving = moving;
            }

            // Judged against where the platform is now, before it moves: that is what the player is
            // standing on this frame.
            IsCarryingPlayer = false;
            var player = world.GameState.Player;
            if (player != null && delta != Vector3.Zero && player.IsGrounded && IsStandingOn(player.WorldTransform.Translation))
            {
                player.Carry(delta);
                IsCarryingPlayer = true;
            }

            Owner.Position = target;
        }

        /// <summary>
        /// How far along the trip it is, 0 at the placed end and 1 at the far end, at <paramref name="seconds"/>
        /// into play. Out, wait, back, wait -- with each leg eased so it starts and stops softly rather than
        /// snapping to speed.
        /// </summary>
        public static float FractionAt(float distance, float speed, float pauseSeconds, float seconds)
        {
            if (distance <= 0.0f || speed <= 0.0f || seconds <= 0.0f)
            {
                return 0.0f;
            }

            var leg = distance / speed;
            var pause = MathF.Max(0.0f, pauseSeconds);
            var t = seconds % (2.0f * (leg + pause));

            float linear;
            if (t < leg)
            {
                linear = t / leg;
            }
            else if (t < leg + pause)
            {
                linear = 1.0f;
            }
            else if (t < (2.0f * leg) + pause)
            {
                linear = 1.0f - ((t - leg - pause) / leg);
            }
            else
            {
                linear = 0.0f;
            }

            return linear * linear * (3.0f - (2.0f * linear));
        }

        /// <summary>Whether a foot position is on the top face of a box, within a little slack.</summary>
        public static bool IsStandingOn(Matrix4x4 boxWorld, Vector3 boxCentreOffset, Vector3 boxSize, Vector3 feet)
        {
            if (!Matrix4x4.Decompose(boxWorld, out _, out var rotation, out var position))
            {
                return false;
            }

            var local = Vector3.Transform(feet - position, Quaternion.Inverse(rotation)) - boxCentreOffset;
            var half = boxSize * 0.5f;
            var aboveTop = local.Y - half.Y;

            return MathF.Abs(local.X) <= half.X + EdgeMargin
                && MathF.Abs(local.Z) <= half.Z + EdgeMargin
                && aboveTop >= -StandBelow
                && aboveTop <= StandAbove;
        }

        // The node's collision box when it has one, since that is the surface the capsule actually stands
        // on; SurfaceSize otherwise.
        private (Vector3 Size, Vector3 Offset) Surface()
        {
            var box = Owner.Components.OfType<ManaCollisionComponent>().FirstOrDefault();
            if (box?.Shape is Box shape)
            {
                // The collision offset is in the node's own unscaled space, while the box is already sized in
                // world units -- a scaled mesh (a container at 1.5x) would otherwise be measured off-centre.
                Matrix4x4.Decompose(Owner.WorldTransform, out var scale, out _, out _);
                return (new Vector3(shape.Width, shape.Height, shape.Length), box.Position * scale);
            }

            return (SurfaceSize, Vector3.Zero);
        }

        private bool IsStandingOn(Vector3 feet)
        {
            var (size, offset) = Surface();
            return IsStandingOn(Owner.WorldTransform, offset, size, feet);
        }

        // A ghost of the platform at the far end, so where it goes can be seen while placing it.
        private void ShowDestination()
        {
            if (!Matrix4x4.Decompose(Owner.WorldTransform, out _, out var rotation, out _))
            {
                rotation = Quaternion.Identity;
            }

            var (size, offset) = Surface();
            var local = Vector3.Transform(Travel, Quaternion.Inverse(rotation));
            _destination.Update(true, size, local + offset, MarkerColor);
        }
    }
}
