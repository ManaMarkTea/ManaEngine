using System.Text;
using System.Text.RegularExpressions;

namespace Mana.Code
{
    public class ManaKhronosShader : ManaShader
    {
        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();

        private static string ResolveShaderPath(string relativePath)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(Environment.CurrentDirectory, "Mana", relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException($"Could not locate shader file '{relativePath}'.");
        }

        private static string ReadShader(string relativePath, Encoding encoding)
        {
            return File.ReadAllText(ResolveShaderPath(relativePath), encoding);
        }

        static ManaKhronosShader()
        {
            Encoding encoding = Encoding.UTF8;

            string pbrShader = ReadShader("Assets/Shaders/Khronos/pbr.frag", encoding);
            string brdfShader = ReadShader("Assets/Shaders/Khronos/brdf.glsl", encoding);
            string iridescenceShader = ReadShader("Assets/Shaders/Khronos/iridescence.glsl", encoding);
            string materialInfoShader = ReadShader("Assets/Shaders/Khronos/material_info.glsl", encoding);
            string iblShader = ReadShader("Assets/Shaders/Khronos/ibl.glsl", encoding);
            string punctualShader = ReadShader("Assets/Shaders/Khronos/punctual.glsl", encoding);
            string primitiveShader = ReadShader("Assets/Shaders/Khronos/primitive.vert", encoding);
            string texturesShader = ReadShader("Assets/Shaders/Khronos/textures.glsl", encoding);
            string tonemappingShader = ReadShader("Assets/Shaders/Khronos/tonemapping.glsl", encoding);
            string shaderFunctions = ReadShader("Assets/Shaders/Khronos/functions.glsl", encoding);
            string animationShader = ReadShader("Assets/Shaders/Khronos/animation.glsl", encoding);
            string cubemapVertShader = ReadShader("Assets/Shaders/Khronos/cubemap.vert", encoding);
            string cubemapFragShader = ReadShader("Assets/Shaders/Khronos/cubemap.frag", encoding);

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
