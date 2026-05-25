using System.Text;

namespace Mana.Code
{
    public class ManaLearnShaderTextured : ManaShader
    {
        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();
        public static Dictionary<string, string> shaderFiles = new Dictionary<string, string>();

        new static void CacheShader(string shaderFile)
        {
            Encoding encoding = Encoding.UTF8;

            string source = File.ReadAllText(shaderFile, encoding);
            string key = System.IO.Path.GetFileName(shaderFile);
            shaderSources[key] = source;
            shaderFiles[key] = shaderFile;
        }

        static ManaLearnShaderTextured()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/background.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/background.vert");
            CacheShader("Assets/Shaders/LearnSpecularTextured/brdf.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/brdf.vert");
            CacheShader("Assets/Shaders/LearnSpecularTextured/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecularTextured/equirectangular_to_cubemap.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/irradiance_convolution.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/Shaders/LearnSpecularTextured/prefilter.frag");
        }

        public ManaLearnShaderTextured(ManaContext context) : base(context)
        {

        }

        public void LoadPBR()
        {
            LoadShader(shaderFiles["pbr.vert"], shaderFiles["pbr.frag"]);
            BindShader();
            CompiledShader.UniformMap["irradianceMap"].SetValue(0);
            CompiledShader.UniformMap["prefilterMap"].SetValue(1);
            CompiledShader.UniformMap["brdfLUT"].SetValue(2);
            CompiledShader.UniformMap["albedoMap"].SetValue(3);
            CompiledShader.UniformMap["normalMap"].SetValue(4);
            CompiledShader.UniformMap["metallicMap"].SetValue(5);
            CompiledShader.UniformMap["roughnessMap"].SetValue(6);
            CompiledShader.UniformMap["aoMap"].SetValue(7);
            UnbindShader();
        }

        protected override string CleanSource(string source)
        {
            return base.CleanSource(source);
        }
    }
}
