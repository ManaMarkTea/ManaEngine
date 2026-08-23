using Mana.Code.Materials;
using Mana.Code.Nodes;
using System.Numerics;

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
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.frag");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/Shaders/LearnSpecular/prefilter.frag");
            CacheShader("Assets/Shaders/Math/FractalGlowRings.frag");
            CacheShader("Assets/ShaderToys/Sunflowers.frag");
        }

        public ManaPbrShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["pbr.frag"]);
            if (!context.DeferGpuUploads)
            {
                InitializeUniforms();
            }
        }

        public override void OnShaderCompiled()
        {
            InitializeUniforms();
        }

        private void InitializeUniforms()
        {
            BindShader();

            // Environment
            CompiledShader.FindUniform("irradianceMap")?.SetValue(0);
            CompiledShader.FindUniform("prefilterMap")?.SetValue(1);
            CompiledShader.FindUniform("brdfLUT")?.SetValue(2);

            // Textures
            CompiledShader.FindUniform("albedoMap")?.SetValue(3);
            CompiledShader.FindUniform("normalMap")?.SetValue(4);
            CompiledShader.FindUniform("emissionMap")?.SetValue(5);
            CompiledShader.FindUniform("metallicMap")?.SetValue(6);
            CompiledShader.FindUniform("roughnessMap")?.SetValue(7);
            CompiledShader.FindUniform("aoMap")?.SetValue(8);

            // Colors
            CompiledShader.FindUniform("albedo")?.SetValue(0.5f, 0.0f, 0.0f);
            CompiledShader.FindUniform("ao")?.SetValue(1.0f);

            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera);

#if !MANA_BROWSER
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
                CompiledShader.FindUniform("opacityMap")?.SetValue(9);

                renderer.ApplyLights(this);
                CompiledShader.FindUniform("exposure")?.SetValue(renderer.PbrExposure);
                CompiledShader.FindUniform("iblIntensity")?.SetValue(renderer.IblIntensity);
                var debugMode = material.ShaderUniforms.TryGetValue("pbrDebugMode", out var debugOverride)
                    ? debugOverride.FloatValue
                    : Context.World.IrradianceSettings.WebGpuDebugMode;
                CompiledShader.FindUniform("pbrDebugMode")?.SetValue(debugMode);
            }
#endif

            material.Albedo.Bind(3);
            material.Normal.Bind(4);
            material.Emission.Bind(5);
            material.Metallic.Bind(6);
            material.Roughness.Bind(7);
            material.Occlusion.Bind(8);
            material.Opacity.Bind(9);

            // The scalars beside each map. Without these the maps were the only source, so a material with
            // no map took whatever the fallback texture held and every slider in the inspector was inert.
            CompiledShader.FindUniform("albedoColor")?.SetValue(material.Albedo.Value);

            CompiledShader.FindUniform("materialFactors")?.SetValue(new Vector4(
                (float)material.Metallic.Value,
                (float)material.Roughness.Value,
                (float)material.Occlusion.Value,
                (float)material.AlphaCutoff));

            CompiledShader.FindUniform("opacityFactor")?.SetValue((float)material.Opacity.Value);
            CompiledShader.FindUniform("opacityMode")?.SetValue((float)material.EffectiveOpacityMode);

            CompiledShader.FindUniform("uvTransform")?.SetValue(new Vector4(
                material.UvScale.X,
                material.UvScale.Y,
                material.UvOffset.X,
                material.UvOffset.Y));

            CompiledShader.FindUniform("normalMapSelection")?.SetValue(material.NormalSelector);
            CompiledShader.FindUniform("emissionColor")?.SetValue(material.Emission.Value.X, material.Emission.Value.Y, material.Emission.Value.Z);
            CompiledShader.FindUniform("emissionStrength")?.SetValue((float)material.EmissionStrength);
        }

        protected override string CleanSource(string source)
        {
            return base.CleanSource(source);
        }
    }
}
