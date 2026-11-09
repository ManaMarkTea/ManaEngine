using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>Touch it and this becomes where the player comes back after a fall or a death.</summary>
    public sealed class CheckpointComponent : ManaTriggerListenerComponent
    {
        protected override Vector3 DefaultSize => new Vector3(3.0f, 3.0f, 3.0f);

        protected override Vector4 DefaultColor => new(0.3f, 0.6f, 1.0f, 1.0f);

        public const string ScriptType = "Checkpoint";

        public CheckpointComponent(ManaSceneNode owner) : base(owner)
        {
        }

        [JsonIgnore]
        public bool IsActivated { get; private set; }

        public override void EndPlay()
        {
            IsActivated = false;
            base.EndPlay();
        }

        protected override void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
            if (IsActivated)
            {
                return;
            }

            IsActivated = true;
            Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.Checkpoint);

            // Nudged up so the capsule settles onto the floor rather than starting interpenetrated -- the
            // same reasoning as the spawn offset.
            state.SetCheckpoint(Owner.WorldTransform.Translation + new Vector3(0.0f, 0.5f, 0.0f));
        }
    }
}
