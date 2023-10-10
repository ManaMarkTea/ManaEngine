namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentIrradianceShader : ManaShader
    {
        static ManaEnvironmentIrradianceShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/irradiance_convolution.frag");
        }

        public ManaEnvironmentIrradianceShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["cubemap.vert"], ShaderSources["irradiance_convolution.frag"]);
            BindShader();
            UnbindShader();
        }

    }
}
