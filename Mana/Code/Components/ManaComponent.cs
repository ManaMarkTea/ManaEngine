using Mana.Code;
using Mana.Code.Nodes;

namespace Mana
{
    public class ManaComponent : ManaObject
    {

        // TODO Component Transform

        public Vector3 Position { get; set; }


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

        public virtual void UpdateComponent() { }
        public virtual void DrawComponent() { }
    }
}
