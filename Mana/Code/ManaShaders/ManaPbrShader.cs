using Mana.Code.Nodes;

namespace Mana.Code
{
    public class ManaPbrShader : ManaShader
    {
        static ManaPbrShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/background.frag");
            CacheShader("Assets/Shaders/LearnSpecular/background.vert");
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/equirectangular_to_cubemap.frag");
            CacheShader("Assets/Shaders/LearnSpecular/irradiance_convolution.frag");
            //CacheShader("Assets/Shaders/LearnSpecular/pbr.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/Shaders/LearnSpecular/prefilter.frag");
            
            // For fun:
            CacheShader("Assets/Shaders/Math/FractalGlowRings.frag");
            CacheShader("Assets/ShaderToys/Sunflowers.frag");
            
        }

        public ManaPbrShader(ManaContext context) : base(context)
        {
            LoadPBR();
        }

        public void LoadPBR()
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["pbr.frag"]);
            BindShader();

            // Environment
            CompiledShader.FindUniform("irradianceMap")?.SetValue(0);
            CompiledShader.FindUniform("prefilterMap")?.SetValue(1);
            CompiledShader.FindUniform("brdfLUT")?.SetValue(2);

            // Textures
            CompiledShader.FindUniform("albedoMap")?.SetValue(3);
            CompiledShader.FindUniform("normalMap")?.SetValue(4);
            CompiledShader.FindUniform("metallicMap")?.SetValue(5);
            CompiledShader.FindUniform("roughnessMap")?.SetValue(6);
            CompiledShader.FindUniform("aoMap")?.SetValue(7);

            // Colors
            CompiledShader.FindUniform("albedo")?.SetValue(0.5f, 0.0f, 0.0f);
            CompiledShader.FindUniform("ao")?.SetValue(1.0f);

            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera);

            var renderer = Context.World.Renderer as ManaIrradianceRenderer;
            if (renderer != null)
            {
                var Irradiance = renderer.IrradianceFramebuffer.bufferMap["Irradiance"];
                var Prefilter = renderer.PrefilterFramebuffer.bufferMap["PreFilter"];
                var Brdf = renderer.BrdfFramebuffer.bufferMap["BRDF"];

                Irradiance?.Texture?.Bind(0);
                Prefilter?.Texture?.Bind(1);
                Brdf?.Texture?.Bind(2);

                CompiledShader.FindUniform("irradianceMap")?.SetValue(0);
                CompiledShader.FindUniform("prefilterMap")?.SetValue(1);
                CompiledShader.FindUniform("brdfLUT")?.SetValue(2);

                CompiledShader.FindUniform("albedoMap")?.SetValue(3);
                CompiledShader.FindUniform("normalMap")?.SetValue(4);
                CompiledShader.FindUniform("emissionMap")?.SetValue(5);
                CompiledShader.FindUniform("metallicMap")?.SetValue(6);
                CompiledShader.FindUniform("roughnessMap")?.SetValue(7);
                CompiledShader.FindUniform("aoMap")?.SetValue(8);

                renderer.ApplyLights(this);
                CompiledShader.FindUniform("exposure")?.SetValue(renderer.PbrExposure);
                CompiledShader.FindUniform("iblIntensity")?.SetValue(renderer.IblIntensity);
            }

            material.Albedo.Bind(3);
            material.Normal.Bind(4);
            material.Emission.Bind(5);
            material.Metallic.Bind(6);
            material.Roughness.Bind(7);
            material.Ambient.Bind(8);

            CompiledShader.FindUniform("normalMapSelection")?.SetValue(material.NormalSelector);
        }

        protected override string CleanSource(string source)
        {
            return base.CleanSource(source);
        }
    }
}
