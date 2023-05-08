using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentBrdfShader : ManaShader
    {
        static ManaEnvironmentBrdfShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/brdf.vert");
            CacheShader("Assets/Shaders/LearnSpecular/brdf.frag");            
        }

        public ManaEnvironmentBrdfShader(ManaContext context) : base(context)
        {            
            LoadShaderSource(shaderSources["brdf.vert"], shaderSources["brdf.frag"]);
            BindShader();

           
            UnbindShader();
        }
    }
}
