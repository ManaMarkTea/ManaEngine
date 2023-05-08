using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentCubeShader : ManaShader
    {
        static ManaEnvironmentCubeShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/equirectangular_to_cubemap.frag");
        }

        public ManaTexture EnvironmentTexture;

        public ManaEnvironmentCubeShader(ManaContext context) : base(context)
        {
            var environmentPath = "Assets/Textures/Environments/newport_loft.png";
            EnvironmentTexture = TextureManager.Instance.CreateTexture(environmentPath);
            EnvironmentTexture.OriginalFilename = environmentPath;
            EnvironmentTexture.Load();

            LoadShaderSource(shaderSources["cubemap.vert"], shaderSources["equirectangular_to_cubemap.frag"]);
            BindShader();

           
            UnbindShader();
        }

        public void BindEnvironmentTexture()
        {
            if (EnvironmentTexture != null)
            {
                EnvironmentTexture.Bind(0);
            }
        }
    }
}
