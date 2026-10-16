namespace Mana.Code
{
    public class ManaWaterShader : ManaShader
    {
        public override bool IsTransparent => true;
        public ManaWaterShader(ManaContext context) : base(context) { }
    }
}
