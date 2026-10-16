namespace Mana.Code
{
    public class ManaNearOceanShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        public ManaNearOceanShader(ManaContext context) : base(context) { }
    }
}
