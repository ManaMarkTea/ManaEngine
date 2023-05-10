using ImGuiNET;
using Mana.Code.ManaShaders;
using Mana.Code.Shapes;
using Silk.NET.Assimp;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Renderers
{
    public class ManaIrradianceRenderer : ManaRenderer
    {
        public ManaIrradianceRenderer(ManaContext context) : base(context) 
        { 

        }

        public ManaRenderTarget IrradianceFramebuffer;
        public ManaRenderTarget PrefilterFramebuffer;
        public ManaRenderTarget BrdfFramebuffer;

        public ManaPbrShader PbrShader;
        public ManaEnvironmentCubeShader EnvironmentShader;
        public ManaEnvironmentBrdfShader EnvironmentBrdfShader;
        public ManaCube SimpleCube;
        public ManaQuad SimpleQuad;
        public ManaSphere SimpleSphere;

        float debugMetallic = 1.0f;
        float debugAmbient = 0.25f;
        float debugRoughness = 0.1f;
        Vector4 debugAlbedo = Colors.White;

        float debugLightIntensity = 10.0f;
        Vector4 debugLightColor = Colors.White;

        public override void Initialize() 
        {
            var gl = Context.gl;
            gl.Enable(EnableCap.DepthTest);
            gl.Enable(EnableCap.TextureCubeMapSeamless);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.ColorMask(true, true, true, true);
            gl.ClearDepth(1.0);

            IrradianceFramebuffer = new ManaRenderTarget(Context, 32, 32);
            IrradianceFramebuffer.AddFloatRgbCubeBuffer("Irradiance");
            IrradianceFramebuffer.Build();
            IrradianceFramebuffer.UnBind();


            PrefilterFramebuffer = new ManaRenderTarget(Context, 512, 512);
            {
                RenderBufferInfo info = new RenderBufferInfo();
                info.FloatBuffer = true;
                info.HasAlpha = false;
                info.HasCube = true;
                info.HasMip = true;
                info.MinLinearMip = true;
                PrefilterFramebuffer.AddBuffer("PreFilter", info);
            }
            
            PrefilterFramebuffer.Build();
            PrefilterFramebuffer.UnBind();

            BrdfFramebuffer = new ManaRenderTarget(Context, 512, 512);
            BrdfFramebuffer.AddFloatBuffer("BRDF");
            BrdfFramebuffer.Build();
            BrdfFramebuffer.UnBind();

            PbrShader = new ManaPbrShader(Context);
            EnvironmentShader = new ManaEnvironmentCubeShader(Context);
            EnvironmentBrdfShader = new ManaEnvironmentBrdfShader(Context);

            SimpleCube = new ManaCube(Context, Colors.White, Vector3.One);
            SimpleQuad = new ManaQuad(Context, Colors.White, Vector3.One * 2.0f);
            SimpleSphere = new ManaSphere(Context, Colors.White, 0.5f);
        }

        public override void Resize(int width, int height) { }

        public override void Clear() 
        {            
            Context.gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }
        

        public override void Render(Action drawScenes) 
        {
            Vector4D<float> brdfTestColor = Colors.White.ToVector4D() * 255.0f;

            ImGui.Begin("Irradiance");
            ImGui.SliderFloat("Metallic", ref debugMetallic, 0.0f, 1.0f);
            ImGui.SliderFloat("Roughness", ref debugRoughness, 0.0f, 1.0f);
            ImGui.SliderFloat("Ambience", ref debugAmbient, 0.0f, 1.0f);
            ImGui.ColorPicker4("Albedo", ref debugAlbedo);

            ImGui.SliderFloat("Light Intensity", ref debugLightIntensity, 0.0f, 100.0f);
            ImGui.ColorPicker4("Light Color", ref debugLightColor);

            ImGui.End();



            RenderIrradiance();

            BrdfFramebuffer.Bind();
            Context.gl.ClearColor<float>(brdfTestColor);
            Clear();
            EnvironmentBrdfShader.BindShader();
            SimpleQuad.RenderDirectly();
            BrdfFramebuffer.UnBind();



            Context.gl.Viewport(Game.Instance._Window.Size);
            Context.gl.ClearColor<float>(DefaultClearColor.ToVector4D() * 255.0f);
            Clear();
            
            EnvironmentShader.BindShader(ManaMaterial.DefaultMaterial, Matrix4x4.CreateScale(100), Context.World.MainCamera);
            EnvironmentShader.BindEnvironmentTexture();
            EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);

            var directionOnly = Context.World.MainCamera._ViewMatrix;
            directionOnly.Translation = new Vector3(0, 0, 0);
            EnvironmentShader.FindUniform("view").SetValue(directionOnly);

            Context.gl.Disable(EnableCap.DepthTest);
            SimpleCube.RenderDirectly();

            Context.gl.Enable(EnableCap.DepthTest);
            drawScenes();

            RenderTestScene();
        }

        public void RenderIrradiance()
        {
            Vector4D<float> irradanceTestColor = Colors.Black.ToVector4D();
            Vector4D<float> prefilterTestColor = Colors.White.ToVector4D();

            Matrix4x4 captureProjection = Matrix4x4.CreatePerspectiveFieldOfView(90.0f.ToRadians(), 1.0f, 0.1f, 100.0f);            
            Matrix4x4[] captureViews = new Matrix4x4[]
            {
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f,  0.0f,  0.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(-1.0f, 0.0f,  0.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  1.0f,  0.0f), new Vector3(0.0f,  0.0f,  1.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f,  0.0f), new Vector3(0.0f,  0.0f, -1.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  0.0f,  1.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  0.0f, -1.0f), new Vector3(0.0f, -1.0f,  0.0f))
            };


            for (int i = 0; i < 6; i++)
            {
                IrradianceFramebuffer.Bind(i);
                Context.gl.ClearColor<float>(irradanceTestColor);
                Clear();

                EnvironmentShader.BindShader();
                EnvironmentShader.BindEnvironmentTexture();
                EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);
                EnvironmentShader.FindUniform("projection").SetValue(captureProjection);
                EnvironmentShader.FindUniform("view").SetValue(captureViews[i]);

                SimpleCube.RenderDirectly();

            }
             
            IrradianceFramebuffer.UnBind();

            for (int i = 0; i < 6; i++)
            {
                PrefilterFramebuffer.Bind(i);
                Context.gl.ClearColor<float>(prefilterTestColor);
                Clear();
                
                EnvironmentShader.BindShader();
                EnvironmentShader.BindEnvironmentTexture();
                EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);
                EnvironmentShader.FindUniform("projection").SetValue(captureProjection);
                EnvironmentShader.FindUniform("view").SetValue(captureViews[i]);

                SimpleCube.RenderDirectly();

                PrefilterFramebuffer.UnBind();
                Context.gl.GenerateMipmap(TextureTarget.TextureCubeMap);
            }

            Context.gl.GenerateMipmap(TextureTarget.TextureCubeMap);

        }

        void RenderTestScene()
        {
            Matrix4x4 model = Matrix4x4.Identity;
            float rows = 6;
            float cols = 8;
            float spacing = 1.5f;

            PbrShader.BindShader(ManaMaterial.DefaultMaterial, Matrix4x4.Identity, Context.World.MainCamera);
            for (int row = 0; row < rows; ++row)
            {
                PbrShader.FindUniform("metallic")?.SetValue(row / rows);

                for (int i = 0; i < 1; ++i)
                {
                    PbrShader.FindUniform($"lightPositions[{i}]")?.SetValue(new Vector3(1, 1, 1));
                    PbrShader.FindUniform($"lightColors[{i}]")?.SetValue(debugLightColor.ToVector3() * debugLightIntensity);
                }

                for (int col = 0; col < cols; ++col)
                {
                    // we clamp the roughness to 0.025 - 1.0 as perfectly smooth surfaces (roughness of 0.0) tend to look a bit off
                    // on direct lighting.
                    PbrShader.FindUniform("albedo")?.SetValue(Colors.White.ToVector3());
                    PbrShader.FindUniform("roughness")?.SetValue(Math.Clamp((float)col / (float)cols, 0.05f, 1.0f));

                    model = Matrix4x4.CreateTranslation((float)(col - (cols / 2)) * spacing, (float)(row - (rows / 2)) * spacing, -2.0f);

                    Matrix4x4.Invert(model, out Matrix4x4 modelInv);
                    modelInv = Matrix4x4.Transpose(modelInv);

                    PbrShader.FindUniform("model").SetValue(model);
                    PbrShader.FindUniform("normalMatrix").SetValue(modelInv.ToMatrix3());

                    SimpleSphere.RenderDirectly();

                    model = Matrix4x4.CreateTranslation((float)(col - (cols / 2)) * spacing, (float)(row - (rows / 2)) * spacing, -8.0f);

                    Matrix4x4.Invert(model, out modelInv);
                    modelInv = Matrix4x4.Transpose(modelInv);

                    PbrShader.FindUniform("model").SetValue(model);
                    PbrShader.FindUniform("normalMatrix").SetValue(modelInv.ToMatrix3());

                    SimpleCube.RenderDirectly();

                }
            }

            {
                model = Matrix4x4.CreateTranslation(8, 0, 1);

                Matrix4x4.Invert(model, out Matrix4x4 modelInv);
                var debugModelInverse = Matrix4x4.Transpose(modelInv);

                var Irradiance = IrradianceFramebuffer.bufferMap["Irradiance"];
                var Prefilter = PrefilterFramebuffer.bufferMap["PreFilter"];
                var Brdf = BrdfFramebuffer.bufferMap["BRDF"];
                Irradiance?.Texture?.Bind(0);
                Prefilter?.Texture?.Bind(1);
                Brdf?.Texture?.Bind(2);

                PbrShader.FindUniform("irradianceMap")?.SetValue(0);
                PbrShader.FindUniform("prefilterMap")?.SetValue(1);
                PbrShader.FindUniform("brdfLUT")?.SetValue(2);

                PbrShader.FindUniform("albedo")?.SetValue(debugAlbedo.ToVector3());
                PbrShader.FindUniform("metallic")?.SetValue(debugMetallic);
                PbrShader.FindUniform("ao")?.SetValue(debugAmbient);
                PbrShader.FindUniform("roughness")?.SetValue(debugRoughness);
                PbrShader.FindUniform("model").SetValue(model);
                PbrShader.FindUniform("normalMatrix").SetValue(debugModelInverse.ToMatrix3());

                SimpleSphere.RenderDirectly();
            }
        }
    }
}


