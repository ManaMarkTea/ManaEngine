using System.Text;

namespace Mana.Code
{
    public class ManaLearnShaderTextured : ManaShader
    {
        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();
        public static Dictionary<string, string> shaderFiles = new Dictionary<string, string>();

        static void CacheShader(string shaderFile)
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
            this.UniformMap["irradianceMap"].SetValue(0);
            this.UniformMap["prefilterMap"].SetValue(1);
            this.UniformMap["brdfLUT"].SetValue(2);
            this.UniformMap["albedoMap"].SetValue(3);
            this.UniformMap["normalMap"].SetValue(4);
            this.UniformMap["metallicMap"].SetValue(5);
            this.UniformMap["roughnessMap"].SetValue(6);
            this.UniformMap["aoMap"].SetValue(7);
            UnbindShader();
        }

        protected override string CleanSource(string source)
        {
            return base.CleanSource(source);
        }
    }
}
