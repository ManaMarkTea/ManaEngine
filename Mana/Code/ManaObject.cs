namespace Mana.Code
{
    public class ManaObject
    {
        public ManaContext Context { get; set; }
        public string Name { get; set; }

        public ManaObject(ManaContext context)
        {
            Context = context;
            Name = GetType().Name;
        }

        public override string ToString()
        {
            return Name.ToString();
        }
    }
}
