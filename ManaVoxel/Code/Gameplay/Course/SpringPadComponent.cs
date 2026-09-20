using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// A pad that throws the player up to a set height: land on it, or run over it, and you are sent skyward.
    /// Put one under something high and out of reach and you have a secret.
    /// <para>
    /// The height is asked for in world units rather than as a speed. How high a launch goes depends on gravity,
    /// so the pad asks the pawn for the speed that reaches the height (see ManaPawnNode.VelocityForHeight) --
    /// change the gravity and the pad still reaches where the level says it does.
    /// </para>
    /// The pad squashes and springs back when it fires, purely for the look of it.
    /// </summary>
    public sealed class SpringPadComponent : ManaTriggerListenerComponent
    {
        public const string ScriptType = "SpringPad";

        private const float SquashSeconds = 0.45f;

        private Vector3 _restScale = Vector3.One;
        private float _squash;
        private float _cooldown;

        public SpringPadComponent(ManaSceneNode owner) : base(owner)
        {
        }

        protected override Vector3 DefaultSize => new Vector3(2.4f, 1.0f, 2.4f);

        protected override Vector3 DefaultOffset => new Vector3(0.0f, 0.5f, 0.0f);

        protected override Vector4 DefaultColor => new(0.3f, 1.0f, 0.45f, 1.0f);

        /// <summary>How high above the pad the launch reaches, in world units.</summary>
        public float LaunchHeight { get; set; } = 8.0f;

        [JsonIgnore]
        public int LaunchCount { get; private set; }

        [JsonIgnore]
        public float LastLaunchSpeed { get; private set; }

        public override void BeginPlay()
        {
            base.BeginPlay();
            _restScale = Owner.Scale;
            _squash = 0.0f;
            _cooldown = 0.0f;
            LaunchCount = 0;
        }

        public override void EndPlay()
        {
            Owner.Scale = _restScale;
            _squash = 0.0f;
            base.EndPlay();
        }

        public override void UpdateComponent()
        {
            var seconds = (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
            _cooldown = MathF.Max(0.0f, _cooldown - seconds);
            AnimateSquash(seconds);
            base.UpdateComponent();
        }

        protected override void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
            Fire(player, state);
        }

        protected override void OnPlayerStay(ManaPawnNode player, ManaGameState state)
        {
            // Standing in the volume with the pad off cooldown (having walked onto it just as it recovered)
            // still launches: the pad is a floor that throws you, not a doorway you must cross.
            if (_cooldown <= 0.0f && player.IsGrounded)
            {
                Fire(player, state);
            }
        }

        private void Fire(ManaPawnNode player, ManaGameState state)
        {
            if (_cooldown > 0.0f || state.Status != ManaGameStatus.Playing)
            {
                return;
            }

            _cooldown = 0.5f;
            _squash = 1.0f;
            LaunchCount++;
            LastLaunchSpeed = player.VelocityForHeight(LaunchHeight);
            player.Bounce(LastLaunchSpeed);
        }

        private void AnimateSquash(float seconds)
        {
            if (_squash <= 0.0f)
            {
                return;
            }

            _squash = MathF.Max(0.0f, _squash - (seconds / SquashSeconds));

            // Down fast, back up with a little overshoot: a spring, not a fade.
            var wobble = MathF.Sin(_squash * MathF.PI * 2.5f) * _squash;
            var height = 1.0f - (0.55f * MathF.Pow(_squash, 2.0f)) + (0.18f * wobble);
            var width = 1.0f + (0.22f * _squash);
            Owner.Scale = new Vector3(_restScale.X * width, _restScale.Y * height, _restScale.Z * width);
        }
    }
}
