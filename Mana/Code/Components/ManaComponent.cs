using Mana.Code;
using Mana.Code.Nodes;

namespace Mana
{
    public class ManaComponent : ManaObject
    {

        // TODO Component Transform

        public Vector3 Position { get; set; }

        /// <summary>
        /// The name a person gave this component, or null. Separate from <see cref="ManaObject.Name"/>, which every
        /// component has and which defaults to its class name: this is for telling apart two components of one type on
        /// a node ("Front Door" and "Back Door" triggers), it is saved with the level, and a
        /// <see cref="ManaComponentReference"/> uses it in place of a position when there is one.
        /// </summary>
        public string? Label { get; set; }


        public ManaSceneNode Owner { get; set; }
        public ManaComponent(ManaSceneNode owner) : base(owner.Context)
        {
            Owner = owner;
        }

        /// <summary>
        /// Components that drive their owner's transform can opt into this so their writes land before the
        /// scene graph computes matrices for the frame. The default keeps existing physics/camera style
        /// components on the old post-transform tick.
        /// </summary>
        public virtual bool UpdatesBeforeNodeTransform => false;

        /// <summary>Called once when the owning world enters play.</summary>
        public virtual void BeginPlay() { }

        /// <summary>Called once when the owning world leaves play.</summary>
        public virtual void EndPlay() { }

        public virtual void UpdateComponent() { }
        public virtual void DrawComponent() { }
    }
}
