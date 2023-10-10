namespace Mana.Code
{

    public class ManaLiquidShader : ManaShader
    {
        static ManaLiquidShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/ShaderToys/LiquidFill.frag");
            CacheShader("Assets/ShaderToys/LightningFill.frag");
        }

        public ManaLiquidShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["LightningFill.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}
