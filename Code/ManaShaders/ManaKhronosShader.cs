using System.Text;
using System.Text.RegularExpressions;

namespace Mana.Code
{
    public class ManaKhronosShader : ManaShader
    {
        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();
        static ManaKhronosShader()
        {
            Encoding encoding = Encoding.UTF8;

            string pbrShader = File.ReadAllText("Assets/shaders/Khronos/pbr.frag", encoding);
            string brdfShader = File.ReadAllText("Assets/shaders/Khronos/brdf.glsl", encoding);
            string iridescenceShader = File.ReadAllText("Assets/shaders/Khronos/iridescence.glsl", encoding);
            string materialInfoShader = File.ReadAllText("Assets/shaders/Khronos/material_info.glsl", encoding);
            string iblShader = File.ReadAllText("Assets/shaders/Khronos/ibl.glsl", encoding);
            string punctualShader = File.ReadAllText("Assets/shaders/Khronos/punctual.glsl", encoding);
            string primitiveShader = File.ReadAllText("Assets/shaders/Khronos/primitive.vert", encoding);
            string texturesShader = File.ReadAllText("Assets/shaders/Khronos/textures.glsl", encoding);
            string tonemappingShader = File.ReadAllText("Assets/shaders/Khronos/tonemapping.glsl", encoding);
            string shaderFunctions = File.ReadAllText("Assets/shaders/Khronos/functions.glsl", encoding);
            string animationShader = File.ReadAllText("Assets/shaders/Khronos/animation.glsl", encoding);
            string cubemapVertShader = File.ReadAllText("Assets/shaders/Khronos/cubemap.vert", encoding);
            string cubemapFragShader = File.ReadAllText("Assets/shaders/Khronos/cubemap.frag", encoding);

            shaderSources["primitive.vert"] = primitiveShader;
            shaderSources["pbr.frag"] = pbrShader;
            shaderSources["material_info.glsl"] = materialInfoShader;
            shaderSources["brdf.glsl"] = brdfShader;
            shaderSources["iridescence.glsl"] = iridescenceShader;
            shaderSources["ibl.glsl"] = iblShader;
            shaderSources["punctual.glsl"] = punctualShader;
            shaderSources["tonemapping.glsl"] = tonemappingShader;
            shaderSources["textures.glsl"] = texturesShader;
            shaderSources["functions.glsl"] = shaderFunctions;
            shaderSources["animation.glsl"] = animationShader;
            shaderSources["cubemap.vert"] = cubemapVertShader;
            shaderSources["cubemap.frag"] = cubemapFragShader;
        }

        public ManaKhronosShader(ManaContext context) : base(context)
        {

        }

        protected override string CleanSource(string source)
        {
            Regex pattern = new Regex(@"#include\s+<([^>]*)>");

            Dictionary<string, string> replacements = new Dictionary<string, string>();

            foreach (Match match in pattern.Matches(source))
            {
                string file = match.Groups[1].ToString();
                if (shaderSources.ContainsKey(file))
                {
                    replacements[match.ToString()] = shaderSources[file];
                }
            }

            foreach (var replacementPair in replacements)
            {
                source = source.Replace(replacementPair.Key, replacementPair.Value);
            }

            return base.CleanSource(source);
        }
    }
}
