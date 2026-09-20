using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// A coin: collected on touch, counted, and hidden for the rest of the run. Pair it with Pickup Motion
    /// for the spin and bob.
    /// </summary>
    public sealed class CollectibleComponent : ManaTriggerListenerComponent
    {
        public const string ScriptType = "Collectible";

        private readonly HiddenMeshes _hidden = new();

        public CollectibleComponent(ManaSceneNode owner) : base(owner)
        {
        }

        public int Value { get; set; } = 1;

        /// <summary>Turns the coin while playing; zero leaves it still. The editor never spins it.</summary>
        public float SpinDegreesPerSecond { get; set; } = 120.0f;

        protected override Vector3 DefaultSize => new Vector3(1.0f, 1.0f, 1.0f);

        protected override Vector3 DefaultOffset => new Vector3(0.0f, 0.5f, 0.0f);

        protected override Vector4 DefaultColor => new(1.0f, 0.85f, 0.15f, 1.0f);

        private Vector3 _restRotation;

        [JsonIgnore]
        public bool IsCollected { get; private set; }

        public override void BeginPlay()
        {
            base.BeginPlay();
            _restRotation = Owner.Rotation;
            Owner.Context.World.GameState.RegisterCollectible(Value);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            if (SpinDegreesPerSecond != 0.0f && !IsCollected && Owner.Context.World.IsPlaying)
            {
                var turned = Owner.Rotation;
                turned.Y += SpinDegreesPerSecond * (float)(Math.PI / 180.0) * (float)ManaTime.GlobalTime.DeltaTime.TotalSeconds;
                Owner.Rotation = turned;
            }
        }

        public override void EndPlay()
        {
            Owner.Rotation = _restRotation;
            _hidden.Restore();
            IsCollected = false;
            base.EndPlay();
        }

        protected override void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
            if (IsCollected)
            {
                return;
            }

            IsCollected = true;
            state.AddCoins(Value);
            Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.Pickup);
            _hidden.Hide(Owner);
        }
    }
}
