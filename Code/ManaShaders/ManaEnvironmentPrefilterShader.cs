using Mana.Code.Materials;
using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentPrefilterShader : ManaShader
    {
        static ManaEnvironmentPrefilterShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/prefilter.frag");
        }

        public ManaEnvironmentPrefilterShader(ManaContext context) : base(context)
        {
            LoadShaderSource(shaderSources["cubemap.vert"], shaderSources["prefilter.frag"]);
            BindShader();

           
            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera)
        {
            base.BindShader(material, objectMatrix, camera);
        }
    }
}
