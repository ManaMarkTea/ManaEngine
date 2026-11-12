using ManaCore.Graphics;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A screen effect a level or prefab turns on (the RetroFilter...): what it covers is its <see cref="Scope"/> -- the
    /// whole screen, its scene, its node's subtree or its siblings'. Every enabled one adds what it covers to its effect's
    /// slot; the editor's Post FX switch hides them all while editing.
    /// </summary>
    public abstract class ManaPostEffectComponent : ManaComponent
    {
        protected ManaPostEffectComponent(ManaSceneNode owner) : base(owner)
        {
        }

        /// <summary>Off, the picture is shown as usual.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>What goes through the effect: the whole screen, this scene, this node and below, or its siblings too.</summary>
        public ManaPostEffectScope Scope { get; set; } = ManaPostEffectScope.Screen;

        /// <summary>The effect's slot in <see cref="ManaPostEffects"/>.</summary>
        public abstract int Slot { get; }

        /// <summary>The slots a node's enabled effects of <paramref name="scope"/> cover, as bits.</summary>
        public static byte Bits(ManaSceneNode node, ManaPostEffectScope scope)
        {
            byte bits = 0;
            foreach (var component in node.Components)
            {
                if (component is ManaPostEffectComponent { Enabled: true } effect && effect.Scope == scope)
                {
                    bits |= ManaPostEffects.Bit(effect.Slot);
                }
            }
            return bits;
        }
    }
}
