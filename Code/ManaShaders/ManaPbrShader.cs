using Mana.Code.Materials;
using Mana.Code.Renderers;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading.Tasks;

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
            CacheShader("Assets/Shaders/LearnSpecular/pbr.vert");
            CacheShader("Assets/Shaders/LearnSpecular/prefilter.frag");
        }

        public ManaPbrShader(ManaContext context) : base(context)
        {
            LoadPBR();
        }

        public void LoadPBR()
        {
            LoadShader(shaderFiles["pbr.vert"], shaderFiles["pbr.frag"]);
            BindShader();

            // Environment
            FindUniform("irradianceMap")?.SetValue(0);
            FindUniform("prefilterMap")?.SetValue(1);
            FindUniform("brdfLUT")?.SetValue(2);
            
            // Textures
            FindUniform("albedoMap")?.SetValue(3);
            FindUniform("normalMap")?.SetValue(4);
            FindUniform("metallicMap")?.SetValue(5);
            FindUniform("roughnessMap")?.SetValue(6);
            FindUniform("aoMap")?.SetValue(7);

            // Colors
            FindUniform("albedo")?.SetValue(0.5f, 0.0f, 0.0f);
            FindUniform("ao")?.SetValue(1.0f);            

            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera)
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
                FindUniform("albedoMap")?.SetValue(3);
                FindUniform("normalMap")?.SetValue(4);
                FindUniform("emissionMap")?.SetValue(5);
                FindUniform("metallicMap")?.SetValue(6);
                FindUniform("roughnessMap")?.SetValue(7);
                FindUniform("aoMap")?.SetValue(8);

                renderer.ApplyLights(this);
            }

            material.Albedo.Bind(3);
            material.Normal.Bind(4);
            material.Emission.Bind(5);
            material.Metallic.Bind(6);
            material.Roughness.Bind(7);
            material.Ambient.Bind(8);

            FindUniform("normalMapSelection")?.SetValue(1.0f);
        }

        protected override string CleanSource(string source)
        {           
            return base.CleanSource(source);
        }
    }
}
