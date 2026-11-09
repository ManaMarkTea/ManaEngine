using Mana;
using Mana.Code;
using Mana.Code.Nodes;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>Run over it and you go faster for a few seconds: a longer jump, a shorter way round.</summary>
    public sealed class BoostPadComponent : ManaTriggerListenerComponent
    {
        public const string ScriptType = "BoostPad";

        public BoostPadComponent(ManaSceneNode owner) : base(owner)
        {
        }

        protected override Vector3 DefaultSize => new Vector3(3.0f, 1.2f, 3.0f);

        protected override Vector3 DefaultOffset => new Vector3(0.0f, 0.6f, 0.0f);

        protected override Vector4 DefaultColor => new(1.0f, 0.85f, 0.15f, 1.0f);

        /// <summary>How much faster: 1.6 is sixty percent more top speed.</summary>
        public float Multiplier { get; set; } = 1.6f;

        public float Seconds { get; set; } = 2.5f;

        protected override void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
            if (state.Status != ManaGameStatus.Playing)
            {
                return;
            }

            player.Boost(Multiplier, Seconds);
            state.ShowMessage("Speed boost!", 1.4f);
        }
    }
}
