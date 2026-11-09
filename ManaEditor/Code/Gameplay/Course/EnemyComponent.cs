using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>
    /// Walks a beat back and forth, optionally chases the player when they come close, and hurts on contact
    /// -- unless the player lands on its head, which defeats it and bounces them.
    /// <para>
    /// A trigger volume like the other course pieces, with the two outcomes decided by where the player's
    /// feet are relative to the top of the volume. Moves by writing its own position (no physics body): it
    /// walks a flat beat, and its whole behaviour is a pure step function, which is what makes it testable.
    /// </para>
    /// </summary>
    public sealed class EnemyComponent : ManaTriggerListenerComponent
    {
        public const string ScriptType = "Enemy";

        // Feet this far below the top of the volume, or higher, are a landing on its head. Generous, because
        // a falling capsule is a frame or two into the volume before the overlap is noticed.
        private const float StompDepth = 0.7f;

        private static readonly Vector3 BeatEndSize = new(0.4f, 0.4f, 0.4f);

        private static readonly Vector4 MarkerColor = new(1.0f, 0.55f, 0.1f, 1.0f);

        // Every enemy in play, so a projectile can find what it hit without walking the scene.
        private static readonly List<EnemyComponent> Active = new();

        private readonly EditorVolumeMarker _beatEnd;
        private readonly HiddenMeshes _hidden = new();

        private Vector3 _start;
        private float _patrolSeconds;
        private bool _chasing;

        // An animated model (the mini-dungeon orc) plays walk, sprint and idle to match what the enemy is
        // doing, and a die clip when stomped. A rigid one (a cube-pets animal) has none of this and is
        // simply slid along its beat.
        private string? _playingClip;
        private float _hideAfterSeconds;

        public EnemyComponent(ManaSceneNode owner) : base(owner)
        {
            _beatEnd = new EditorVolumeMarker(owner, "Patrol End");
            _start = owner.Position;
        }

        protected override Vector3 DefaultSize => new Vector3(1.4f, 1.4f, 1.4f);

        protected override Vector4 DefaultColor => MarkerColor;

        /// <summary>How far the far end of its beat is from where it was placed, in world axes.</summary>
        public Vector3 Travel { get; set; } = new(6.0f, 0.0f, 0.0f);

        public float Speed { get; set; } = 2.0f;

        /// <summary>How long it stands at each end before turning round.</summary>
        public float PauseSeconds { get; set; } = 0.5f;

        /// <summary>Starts chasing when the player is this close on the ground plane; zero never chases.</summary>
        public float ChaseRadius { get; set; }

        public float ChaseSpeed { get; set; } = 3.5f;

        /// <summary>How far from its beat it will follow the player before giving up.</summary>
        public float LeashRadius { get; set; } = 8.0f;

        public int Damage { get; set; } = 1;

        /// <summary>For an enemy drawn by a Sprite: which way its picture looks. Kenney's enemies look left.</summary>
        public bool SpriteFacesRight { get; set; }

        private bool? _startFlip;

        /// <summary>How fast a stomp throws the player back up.</summary>
        public float BounceSpeed { get; set; } = 9.0f;

        [JsonIgnore]
        public bool IsDefeated { get; private set; }

        [JsonIgnore]
        public bool IsChasing => _chasing;

        public override void BeginPlay()
        {
            base.BeginPlay();
            _start = Owner.Position;
            _patrolSeconds = 0.0f;
            _chasing = false;
            IsDefeated = false;
            _hideAfterSeconds = 0.0f;
            _playingClip = null;
            Owner.Context.World.GameState.RegisterEnemy();
            lock (Active)
            {
                Active.Add(this);
            }
            // The route's end marker is an editor aid; left up, it would ride along beside the enemy all game.
            _beatEnd.Update(false, BeatEndSize, Vector3.Zero, MarkerColor);
            _startFlip = Owner.GetComponent<Mana.Code.Components.ManaSpriteBase>()?.FlipX;
        }

        public override void EndPlay()
        {
            lock (Active)
            {
                Active.Remove(this);
            }

            Owner.Position = _start;
            if (_startFlip is { } flip && Owner.GetComponent<Mana.Code.Components.ManaSpriteBase>() is { } sprite)
            {
                sprite.FlipX = flip;
            }
            _hidden.Restore();
            IsDefeated = false;
            _chasing = false;
            _hideAfterSeconds = 0.0f;
            _playingClip = null;

            // Back to the bind pose the editor shows, with looping restored for the next run.
            if (FindAnimation() is { } animation)
            {
                animation.Looping = true;
                animation.Stop();
            }

            base.EndPlay();
        }

        public override void UpdateComponent()
        {
            var world = Owner.Context.World;
            if (!world.IsPlaying)
            {
                ShowBeatEnd();
            }
            else if (!IsDefeated)
            {
                Walk(world.GameState, (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds);
            }
            else if (_hideAfterSeconds > 0.0f)
            {
                // Let the die clip play out before the model goes.
                _hideAfterSeconds -= (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
                if (_hideAfterSeconds <= 0.0f)
                {
                    _hidden.Hide(Owner);
                }
            }

            base.UpdateComponent();
        }

        protected override void OnPlayerStay(ManaPawnNode player, ManaGameState state)
        {
            if (IsDefeated || state.Status != ManaGameStatus.Playing)
            {
                return;
            }

            var feet = player.WorldTransform.Translation.Y;
            var top = Owner.WorldTransform.Translation.Y + Offset.Y + (Size.Y * 0.5f);
            var fallingSpeed = -(player.Body?.Velocity.Y ?? 0.0f);

            if (IsStomp(feet, top, fallingSpeed))
            {
                IsDefeated = true;
                _chasing = false;
                Defeat();
                state.DefeatEnemy();
                Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.EnemyStomp);
                Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.EnemyDefeat);
                player.Bounce(BounceSpeed);
                return;
            }

            state.Damage(Damage, Owner.Name, ManaDeathFlavor.Enemy);
        }

        /// <summary>
        /// Defeats the first living enemy of <paramref name="world"/> that the segment from <paramref name="from"/>
        /// to <paramref name="to"/> passes through, as a thrown or shot thing would. Judged in the XY plane, since
        /// a side-on shot has no depth to miss by.
        /// </summary>
        public static bool TryShoot(ManaWorld world, Vector3 from, Vector3 to)
        {
            EnemyComponent[] enemies;
            lock (Active)
            {
                enemies = Active.ToArray();
            }

            var state = world.GameState;
            foreach (var enemy in enemies)
            {
                if (enemy.IsDefeated || !ReferenceEquals(enemy.Owner.Context.World, world) || state.Status != ManaGameStatus.Playing)
                {
                    continue;
                }

                var centre = enemy.Owner.WorldTransform.Translation + enemy.Offset;
                var half = enemy.Size * 0.5f;
                if (!SegmentCrossesBox(from.X, from.Y, to.X, to.Y, centre.X, centre.Y, half.X, half.Y))
                {
                    continue;
                }

                enemy.IsDefeated = true;
                enemy._chasing = false;
                enemy.Defeat();
                state.DefeatEnemy();
                Mana.Code.Audio.ManaAudioCueRouter.EmitAt(enemy.Owner, ManaCore.Audio.ManaSoundCue.EnemyDefeat);
                return true;
            }

            return false;
        }

        /// <summary>Slab test of a segment against a box centred on (cx, cy) with the given half extents.</summary>
        public static bool SegmentCrossesBox(float x0, float y0, float x1, float y1, float cx, float cy, float halfX, float halfY)
        {
            var enter = 0.0f;
            var exit = 1.0f;
            return Slab(x0, x1 - x0, cx - halfX, cx + halfX, ref enter, ref exit)
                && Slab(y0, y1 - y0, cy - halfY, cy + halfY, ref enter, ref exit);
        }

        private static bool Slab(float start, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (MathF.Abs(delta) < 1e-6f)
            {
                return start >= min && start <= max;
            }

            var a = (min - start) / delta;
            var b = (max - start) / delta;
            enter = MathF.Max(enter, MathF.Min(a, b));
            exit = MathF.Min(exit, MathF.Max(a, b));
            return enter <= exit;
        }

        /// <summary>Whether a player whose feet are at <paramref name="feetY"/> is landing on a head at <paramref name="topY"/>.</summary>
        public static bool IsStomp(float feetY, float topY, float fallingSpeed)
        {
            // Not while rising: jumping up into an enemy from underneath is a hit, however high the feet are.
            return fallingSpeed > -0.5f && feetY >= topY - StompDepth;
        }

        /// <summary>
        /// One step of behaviour: chase if the player is close, otherwise follow the beat, and if it has been
        /// drawn off the beat, walk back to where it should be before carrying on.
        /// </summary>
        private void Walk(ManaGameState state, float deltaSeconds)
        {
            if (deltaSeconds <= 0.0f)
            {
                return;
            }

            var position = Owner.Position;
            var beatPoint = _start + (Travel * MovingPlatformComponent.FractionAt(
                Travel.Length(), Speed, PauseSeconds, _patrolSeconds));

            var player = state.Player;
            bool wasChasing = _chasing;
            _chasing = ChaseRadius > 0.0f
                && player != null
                && state.Status == ManaGameStatus.Playing
                && Horizontal(player.WorldTransform.Translation - position).Length() <= ChaseRadius
                && Horizontal(position - _start).Length() <= LeashRadius;

            if (_chasing && !wasChasing)
            {
                Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.EnemyAlert);
            }
            Vector3 next;
            if (_chasing)
            {
                next = StepToward(position, player!.WorldTransform.Translation, ChaseSpeed * deltaSeconds);
            }
            else if (Horizontal(beatPoint - position).Length() > 0.05f + (Speed * deltaSeconds * 2.0f))
            {
                // Off the beat, having chased: go back to it without the beat's clock running on.
                next = StepToward(position, beatPoint, Speed * deltaSeconds);
            }
            else
            {
                _patrolSeconds += deltaSeconds;
                next = new Vector3(beatPoint.X, position.Y, beatPoint.Z);
            }

            var heading = Horizontal(next - position);
            if (heading.LengthSquared() > 1e-8f && Owner.GetComponent<Mana.Code.Components.ManaSpriteBase>() is { } sprite)
            {
                // A picture turned to face its path would go edge-on; it is mirrored instead.
                if (MathF.Abs(heading.X) > 1e-6f)
                {
                    sprite.FlipX = (heading.X > 0.0f) != SpriteFacesRight;
                }
            }
            else if (heading.LengthSquared() > 1e-8f)
            {
                var turned = Owner.Rotation;

                // Characters and the enemy models face +Z, so yaw is measured from it.
                turned.Y = MathF.Atan2(heading.X, heading.Z);
                Owner.Rotation = turned;
            }

            Owner.Position = next;

            var moving = heading.LengthSquared() > 1e-8f;
            PlayClip(_chasing ? "sprint" : moving ? "walk" : "idle");
        }

        private ManaSkeletalAnimationComponent? FindAnimation()
        {
            return (Owner as ManaSkeletalActorNode)?.Animation
                ?? Owner.GetComponent<ManaSkeletalAnimationComponent>();
        }

        // Switches only when the clip changes: Play restarts the clip, so asking for the same one every
        // frame would hold the model on its first frame.
        private void PlayClip(string clip)
        {
            if (_playingClip == clip || FindAnimation() is not { } animation)
            {
                return;
            }

            if (animation.HasClip(clip) || (clip == "sprint" && animation.HasClip("walk")))
            {
                animation.Looping = true;
                animation.Play(animation.HasClip(clip) ? clip : "walk");
                _playingClip = clip;
            }
        }

        private void Defeat()
        {
            var animation = FindAnimation();
            if (animation != null && animation.HasClip("die"))
            {
                animation.Looping = false;
                animation.Play("die");
                _playingClip = "die";
                _hideAfterSeconds = MathF.Max(0.4f, (float)(animation.ActiveClip?.DurationSeconds ?? 0.8));
                return;
            }

            _hidden.Hide(Owner);
        }

        private static Vector3 Horizontal(Vector3 value) => new(value.X, 0.0f, value.Z);

        private static Vector3 StepToward(Vector3 from, Vector3 to, float distance)
        {
            var toward = Horizontal(to - from);
            var length = toward.Length();
            if (length <= distance || length < 1e-6f)
            {
                return new Vector3(to.X, from.Y, to.Z);
            }

            return from + (toward / length * distance);
        }

        // A small marker where the beat ends, so the route can be seen while placing it. Deliberately not the
        // size of the trigger volume: a full-size box out there read as a second, misplaced volume.
        private void ShowBeatEnd()
        {
            if (!Matrix4x4.Decompose(Owner.WorldTransform, out _, out var rotation, out _))
            {
                rotation = Quaternion.Identity;
            }

            var local = Vector3.Transform(Travel, Quaternion.Inverse(rotation));
            _beatEnd.Update(true, BeatEndSize, local, MarkerColor);
        }
    }
}
