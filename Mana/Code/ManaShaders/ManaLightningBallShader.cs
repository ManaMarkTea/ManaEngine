namespace Mana.Code
{
    public class ManaLightningBallShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        public ManaLightningBallShader(ManaContext context) : base(context) { }
    }
}
