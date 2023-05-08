using Silk.NET.OpenGL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Renderers
{
    public class ManaRenderer : ManaObject
    {
        static ManaRenderer()
        {
            string pbrShader = File.ReadAllText("Assets/shaders/Khronos/pbr.frag");
            string brdfShader = File.ReadAllText("Assets/shaders/Khronos/brdf.glsl");
            string iridescenceShader = File.ReadAllText("Assets/shaders/Khronos/iridescence.glsl");
            string materialInfoShader = File.ReadAllText("Assets/shaders/Khronos/material_info.glsl");
            string iblShader = File.ReadAllText("Assets/shaders/Khronos/ibl.glsl");
            string punctualShader = File.ReadAllText("Assets/shaders/Khronos/punctual.glsl");
            string primitiveShader = File.ReadAllText("Assets/shaders/Khronos/primitive.vert");
            string texturesShader = File.ReadAllText("Assets/shaders/Khronos/textures.glsl");
            string tonemappingShader = File.ReadAllText("Assets/shaders/Khronos/tonemapping.glsl");
            string shaderFunctions = File.ReadAllText("Assets/shaders/Khronos/functions.glsl");
            string animationShader = File.ReadAllText("Assets/shaders/Khronos/animation.glsl");
            string cubemapVertShader = File.ReadAllText("Assets/shaders/Khronos/cubemap.vert");
            string cubemapFragShader = File.ReadAllText("Assets/shaders/Khronos/cubemap.frag");

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

        public List<ManaSceneNode> visibleLights;
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projMatrix;
        public Matrix4x4 viewProjectionMatrix;

        public Vector4 DefaultClearColor = new Vector4(100 / 255.0f, 149 / 255.0f, 237 / 255.0f, 255 / 255.0f);

        public static Dictionary<string, string> shaderSources = new Dictionary<string,string>();

        public ManaRenderer(ManaContext context) : base(context) 
        {
        
        }

        public virtual void Initialize() { }

        public virtual void Resize(int width, int height) { }

        public virtual void Clear() { }

        public virtual void Render(Action drawScenes) { }
    }
}
