using Mana.Code.Nodes;

namespace Mana.Code.Renderers
{
    public class ManaRenderer : ManaObject
    {
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

        private static string ReadShader(string relativePath)
        {
            return File.ReadAllText(ResolveShaderPath(relativePath));
        }

        static ManaRenderer()
        {
            string pbrShader = ReadShader("Assets/Shaders/Khronos/pbr.frag");
            string brdfShader = ReadShader("Assets/Shaders/Khronos/brdf.glsl");
            string iridescenceShader = ReadShader("Assets/Shaders/Khronos/iridescence.glsl");
            string materialInfoShader = ReadShader("Assets/Shaders/Khronos/material_info.glsl");
            string iblShader = ReadShader("Assets/Shaders/Khronos/ibl.glsl");
            string punctualShader = ReadShader("Assets/Shaders/Khronos/punctual.glsl");
            string primitiveShader = ReadShader("Assets/Shaders/Khronos/primitive.vert");
            string texturesShader = ReadShader("Assets/Shaders/Khronos/textures.glsl");
            string tonemappingShader = ReadShader("Assets/Shaders/Khronos/tonemapping.glsl");
            string shaderFunctions = ReadShader("Assets/Shaders/Khronos/functions.glsl");
            string animationShader = ReadShader("Assets/Shaders/Khronos/animation.glsl");
            string cubemapVertShader = ReadShader("Assets/Shaders/Khronos/cubemap.vert");
            string cubemapFragShader = ReadShader("Assets/Shaders/Khronos/cubemap.frag");

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

        public int InternalMSAA = 4;

        public List<ManaSceneNode> visibleLights = new List<ManaSceneNode>();
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projMatrix;
        public Matrix4x4 viewProjectionMatrix;

        public Vector4 DefaultClearColor = new Vector4(100 / 255.0f, 149 / 255.0f, 237 / 255.0f, 255 / 255.0f);

        public static Dictionary<string, string> shaderSources = new Dictionary<string, string>();

        public ManaRenderer(ManaContext context) : base(context)
        {

        }

        public virtual void Initialize() { }

        public virtual void Resize(int width, int height) { }

        public virtual void Clear() { }

        public virtual void DrawImGui() { }

        public virtual void Render(Action drawScenes) { }

        public virtual void GetLightPositionsAndColors(out System.Numerics.Vector4[] positions, out System.Numerics.Vector4[] colors)
        {
            positions = new System.Numerics.Vector4[4];
            colors = new System.Numerics.Vector4[4];
        }
    }
}
