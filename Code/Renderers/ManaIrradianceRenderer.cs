using Mana.Code.ManaShaders;
using Mana.Code.Shapes;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
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

        public ManaLearnShader PbrShader;
        public ManaEnvironmentCubeShader EnvironmentShader;
        public ManaEnvironmentBrdfShader EnvironmentBrdfShader;
        public ManaCube SimpleCube;
        public ManaQuad SimpleQuad;
        public ManaSphere SimpleSphere;


        public override void Initialize() 
        {
            var gl = Context.gl;
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.ColorMask(true, true, true, true);
            gl.ClearDepth(1.0);

            IrradianceFramebuffer = new ManaRenderTarget(Context, 32, 32);
            IrradianceFramebuffer.AddFloatRgbCubeBuffer("Irradiance");
            IrradianceFramebuffer.Build();
            IrradianceFramebuffer.UnBind();

            PrefilterFramebuffer = new ManaRenderTarget(Context, 128, 128);
            PrefilterFramebuffer.AddFloatRgbCubeBuffer("PreFilter");
            PrefilterFramebuffer.Build();
            PrefilterFramebuffer.UnBind();

            BrdfFramebuffer = new ManaRenderTarget(Context, 512, 512);
            BrdfFramebuffer.AddFloatBuffer("BRDF");
            BrdfFramebuffer.Build();
            BrdfFramebuffer.UnBind();

            PbrShader = new ManaLearnShader(Context);
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

            RenderIrradiance();

            BrdfFramebuffer.Bind();
            Context.gl.ClearColor<float>(brdfTestColor);
            Clear();
            EnvironmentBrdfShader.BindShader();
            SimpleQuad.RenderDirectly();
            BrdfFramebuffer.UnBind();

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
            Vector4D<float> irradanceTestColor = Colors.Black.ToVector4D() * 255.0f * 10.0f;
            Vector4D<float> prefilterTestColor = Colors.White.ToVector4D() * 255.0f * 0.0f;

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

                IrradianceFramebuffer.UnBind();

                PrefilterFramebuffer.Bind(i);
                Context.gl.ClearColor<float>(prefilterTestColor);
                //Clear();
                PrefilterFramebuffer.UnBind();  
            }

        }

        void RenderTestScene()
        {
            Matrix4x4 model = Matrix4x4.Identity;
            float rows = 20;
            float cols = 20;
            float spacing = 1;

            PbrShader.BindShader(ManaMaterial.DefaultMaterial, Matrix4x4.Identity, Context.World.MainCamera);
            for (int row = 0; row < rows; ++row)
            {
                PbrShader.FindUniform("metallic").SetValue(row / rows);

                for (int i = 0; i < 1; ++i)
                {
                    PbrShader.FindUniform($"lightPositions[{i}]").SetValue(new Vector3(1, 1, 1));
                    PbrShader.FindUniform($"lightColors[{i}]").SetValue(Colors.White.ToVector3() * 10);
                }

                for (int col = 0; col < cols; ++col)
                {
                    // we clamp the roughness to 0.025 - 1.0 as perfectly smooth surfaces (roughness of 0.0) tend to look a bit off
                    // on direct lighting.
                    PbrShader.FindUniform("albedo").SetValue(Colors.Cyan.ToVector3());
                    PbrShader.FindUniform("roughness").SetValue( Math.Clamp((float)col / (float)cols, 0.05f, 1.0f));

                    model = Matrix4x4.CreateTranslation( (float)(col - (cols / 2)) * spacing, (float)(row - (rows / 2)) * spacing, -2.0f);
                    
                    Matrix4x4.Invert(model, out Matrix4x4 modelInv);
                    modelInv = Matrix4x4.Transpose(modelInv);

                    PbrShader.FindUniform("model").SetValue(model);
                    PbrShader.FindUniform("normalMatrix").SetValue(modelInv.ToMatrix3());
                    SimpleSphere.RenderDirectly();
                }
            }
        }
    }
}


