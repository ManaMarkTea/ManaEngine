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

        public virtual void UpdateComponent() { }
        public virtual void DrawComponent() { }
    }
}
