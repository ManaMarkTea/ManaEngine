using Mana;
using Mana.Code;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>The finish: reaching it ends the level.</summary>
    public sealed class GoalComponent : ManaTriggerComponent
    {
        protected override Vector4 VolumeColor => new(0.25f, 0.95f, 0.45f, 1.0f);

        public const string ScriptType = "Goal";

        public GoalComponent(ManaSceneNode owner) : base(owner)
        {
            Size = new Vector3(3.0f, 3.0f, 3.0f);
        }

        /// <summary>The goal stays shut until every coin in the level is collected.</summary>
        public bool RequireAllCoins { get; set; }

        protected override void OnPlayerEnter(ManaPawnNode player, ManaGameState state)
        {
            if (RequireAllCoins && state.Coins < state.TotalCoins)
            {
                state.ShowMessage($"Collect every coin first ({state.Coins}/{state.TotalCoins})", 2.5f);
                return;
            }

            state.Win();
        }
    }
}
