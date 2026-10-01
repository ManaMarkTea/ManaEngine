namespace Mana.Code
{

    public class ManaLiquidShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        static ManaLiquidShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/ShaderToys/LiquidFill.frag");
        }

        public ManaLiquidShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["LiquidFill.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}
