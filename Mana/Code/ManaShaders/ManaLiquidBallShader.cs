namespace Mana.Code
{
    public class ManaLiquidBallShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        public ManaLiquidBallShader(ManaContext context) : base(context) { }
    }
}
