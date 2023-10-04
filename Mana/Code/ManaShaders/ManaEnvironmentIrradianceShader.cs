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
            LoadShaderSource(shaderSources["cubemap.vert"], shaderSources["irradiance_convolution.frag"]);
            BindShader();
            UnbindShader();
        }

    }
}
