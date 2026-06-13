namespace Mana.Code
{
    public class ManaLightningBallShader : ManaShader
    {
        static ManaLightningBallShader()
        {
            CacheShader("Assets/ShaderToys/LightningBall.vert");
            CacheShader("Assets/ShaderToys/LightningBall.frag");
        }

        public ManaLightningBallShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["LightningBall.vert"], ShaderSources["LightningBall.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}
