namespace Mana.Code
{
    public class ManaOceanShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        public ManaOceanShader(ManaContext context) : base(context) { }
    }
}
