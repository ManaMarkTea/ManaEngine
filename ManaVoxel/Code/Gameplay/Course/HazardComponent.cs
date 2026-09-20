using Mana;
using Mana.Code;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// Spikes, lava, a bottomless pit's mouth: hurts while the player is inside it. Ticked on Stay so
    /// standing in it keeps costing health -- the game state's brief invulnerability after a hit is what
    /// spaces the damage out.
    /// </summary>
    public sealed class HazardComponent : ManaTriggerComponent
    {
        protected override Vector4 VolumeColor => new(1.0f, 0.2f, 0.2f, 1.0f);

        public const string ScriptType = "Hazard";

        public HazardComponent(ManaSceneNode owner) : base(owner)
        {
        }

        public int Damage { get; set; } = 1;

        /// <summary>Ignores health entirely: touching it is a death.</summary>
        public bool Kills { get; set; }

        public override void BeginPlay()
        {
            base.BeginPlay();
            Owner.Context.World.GameState.RegisterHazard();
        }

        protected override void OnPlayerStay(ManaPawnNode player, ManaGameState state)
        {
            if (Kills)
            {
                if (state.Status == ManaGameStatus.Playing && !state.IsInvulnerable)
                {
                    Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.HazardContact, player.WorldTransform.Translation);
                    state.Die(Owner.Name);
                }

                return;
            }

            if (state.Status == ManaGameStatus.Playing && !state.IsInvulnerable && Damage > 0)
            {
                Mana.Code.Audio.ManaAudioCueRouter.EmitAt(Owner, ManaCore.Audio.ManaSoundCue.HazardContact, player.WorldTransform.Translation);
                state.Damage(Damage, Owner.Name);
            }
        }
    }
}
