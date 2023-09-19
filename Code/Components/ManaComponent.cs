namespace Mana
{
    public class ManaComponent : ManaObject
    {
        public ManaSceneNode Owner { get; set; }
        public ManaComponent(ManaSceneNode owner) : base(owner.Context)
        {
            Owner = owner;
        }

        public virtual void UpdateComponent() { }
        public virtual void DrawComponent() { }
    }
}
