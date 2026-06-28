using Silk.NET.OpenGL;
using System;

namespace Mana.Code.Graphics
{
    public class ManaOpenGlHdrPostProcessor : ManaObject
    {
        public ManaRenderTarget HdrSceneTarget { get; private set; } = null!;
        private ManaRenderTarget _bloomTargetA = null!;
        private ManaRenderTarget _bloomTargetB = null!;

        private ManaShader _brightShader = null!;
        private ManaShader _blurShader = null!;
        private ManaShader _combineShader = null!;

        private uint _dummyVao;

        public ManaOpenGlHdrPostProcessor(ManaContext context, int width, int height) : base(context)
        {
            var gl = Context.RequireOpenGl();
            _dummyVao = gl.GenVertexArray();

            // Compile shaders
            _brightShader = new ManaShader(Context);
            _brightShader.LoadShader("Assets/Shaders/LearnSpecularTextured/post_quad.vert", "Assets/Shaders/LearnSpecularTextured/bloom_bright.frag");

            _blurShader = new ManaShader(Context);
            _blurShader.LoadShader("Assets/Shaders/LearnSpecularTextured/post_quad.vert", "Assets/Shaders/LearnSpecularTextured/bloom_blur.frag");

            _combineShader = new ManaShader(Context);
            _combineShader.LoadShader("Assets/Shaders/LearnSpecularTextured/post_quad.vert", "Assets/Shaders/LearnSpecularTextured/bloom_combine.frag");

            // Initialize targets
            RecreateTargets(width, height);
        }

        public void Resize(int width, int height)
        {
            RecreateTargets(width, height);
        }

        private void RecreateTargets(int width, int height)
        {
            HdrSceneTarget = new ManaRenderTarget(Context, width, height);
            HdrSceneTarget.AddFloatBuffer("SceneColor");
            HdrSceneTarget.AddDepthBuffer("SceneDepth");
            HdrSceneTarget.Build();
            HdrSceneTarget.UnBind();

            int bloomW = Math.Max(1, width / 4);
            int bloomH = Math.Max(1, height / 4);

            _bloomTargetA = new ManaRenderTarget(Context, bloomW, bloomH);
            _bloomTargetA.AddFloatBuffer("BloomA");
            _bloomTargetA.Build();
            _bloomTargetA.UnBind();

            _bloomTargetB = new ManaRenderTarget(Context, bloomW, bloomH);
            _bloomTargetB.AddFloatBuffer("BloomB");
            _bloomTargetB.Build();
            _bloomTargetB.UnBind();
        }

        public void Begin()
        {
            HdrSceneTarget.Bind();
            var gl = Context.RequireOpenGl();
            gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            gl.Enable(EnableCap.DepthTest);
        }

        public void End()
        {
            HdrSceneTarget.UnBind();

            var gl = Context.RequireOpenGl();
            gl.Disable(EnableCap.DepthTest);
            gl.BindVertexArray(_dummyVao);

            // Pass 1: Bright pass -> Bloom Target A
            _bloomTargetA.Bind();
            gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            _brightShader.BindShader();
            HdrSceneTarget.bufferMap["SceneColor"].Texture?.Bind(0);
            _brightShader.FindUniform("hdrColor")?.SetValue(0);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _brightShader.UnbindShader();
            _bloomTargetA.UnBind();

            // Pass 2: Horizontal Blur (Bloom Target A -> Bloom Target B)
            _bloomTargetB.Bind();
            gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            _blurShader.BindShader();
            _bloomTargetA.bufferMap["BloomA"].Texture?.Bind(0);
            _blurShader.FindUniform("image")?.SetValue(0);
            _blurShader.FindUniform("horizontal")?.SetValue(1);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _blurShader.UnbindShader();
            _bloomTargetB.UnBind();

            // Pass 3: Vertical Blur (Bloom Target B -> Bloom Target A)
            _bloomTargetA.Bind();
            gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            _blurShader.BindShader();
            _bloomTargetB.bufferMap["BloomB"].Texture?.Bind(0);
            _blurShader.FindUniform("image")?.SetValue(0);
            _blurShader.FindUniform("horizontal")?.SetValue(0);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _blurShader.UnbindShader();
            _bloomTargetA.UnBind();

            // Pass 4: Combine & Tonemap (HdrScene + Bloom Target A -> Default Screen Framebuffer)
            var windowSize = Context.Window.FramebufferSize;
            gl.Viewport(0, 0, (uint)windowSize.X, (uint)windowSize.Y);

            _combineShader.BindShader();
            HdrSceneTarget.bufferMap["SceneColor"].Texture?.Bind(0);
            _combineShader.FindUniform("hdrColor")?.SetValue(0);

            _bloomTargetA.bufferMap["BloomA"].Texture?.Bind(1);
            _combineShader.FindUniform("bloomColor")?.SetValue(1);

            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _combineShader.UnbindShader();

            gl.BindVertexArray(0);
        }
    }
}
