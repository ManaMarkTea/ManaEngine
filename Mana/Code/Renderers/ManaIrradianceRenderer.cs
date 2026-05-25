using ImGuiNET;
using Mana.Code.Nodes;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Mana.Code.Renderers
{
    public class ManaIrradianceRenderer : ManaRenderer
    {
        public ManaIrradianceRenderer(ManaContext context) : base(context)
        {

        }
        
        public ManaRenderTarget EnvironmentCubeTarget;
        public ManaRenderTarget IrradianceFramebuffer;
        public ManaRenderTarget PrefilterFramebuffer;
        public ManaRenderTarget BrdfFramebuffer;

        public ManaEnvironmentIrradianceShader EnvironmentIrradianceShader;
        public ManaEnvironmentCubeShader EnvironmentShader;
        public ManaEnvironmentBrdfShader EnvironmentBrdfShader;
        public ManaEnvironmentPrefilterShader EnvironmentPrefilterShader;

        public ManaBasicTextureShader BasicShader;
        public ManaBasicTextureShader BasicCubeShader;

        public ManaTexture EnvironmentMap;

        public ManaCube SimpleCube;
        public ManaQuad SimpleQuad;
        public ManaSphere SimpleSphere;

        bool drawDebugWorld = false;
        float debugMetallic = 1.0f;
        float debugAmbient = 0.25f;
        float debugRoughness = 0.1f;
        Vector4 debugAlbedo = Colors.White;
        float debugSceneScale = 1.0f;

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

            // Shaders
            BasicShader = new ManaBasicTextureShader(Context, false);
            BasicCubeShader = new ManaBasicTextureShader(Context, true);
            EnvironmentIrradianceShader = new ManaEnvironmentIrradianceShader(Context);
            EnvironmentShader = new ManaEnvironmentCubeShader(Context);
            EnvironmentBrdfShader = new ManaEnvironmentBrdfShader(Context);
            EnvironmentPrefilterShader = new ManaEnvironmentPrefilterShader(Context);

            // Basic Shapes
            SimpleCube = new ManaCube(Context, Colors.White, Vector3.One);
            SimpleQuad = new ManaQuad(Context, Colors.White, Vector3.One * 2.0f);
            SimpleSphere = new ManaSphere(Context, Colors.White, 0.5f);

            // Targets
            EnvironmentCubeTarget = new ManaRenderTarget(Context, 512, 512);
            {
                RenderBufferInfo info = new RenderBufferInfo();
                info.FloatBuffer = true;
                info.HasAlpha = false;
                info.HasCube = true;
                info.HasMip = true;
                info.MinLinearMip = true;

                EnvironmentCubeTarget.AddBuffer("EnvironmentCube", info);
                EnvironmentCubeTarget.Build();
                EnvironmentCubeTarget.UnBind();
            }
            // Load the environment into a texture;
            RenderEnvironmentMapTarget();
            EnvironmentMap = EnvironmentCubeTarget.bufferMap["EnvironmentCube"].Texture;


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
            RenderBrdfTarget();

        }

        public override void Resize(int width, int height) { }

        public override void Clear()
        {
            Context.gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        }

        public void ClearDepth()
        {
            Context.gl.Clear((uint)(ClearBufferMask.DepthBufferBit));
        }


        public override void Render(Action drawScenes)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Irradiance");

            ImGui.Checkbox("Debug World", ref drawDebugWorld);

            ImGui.SliderFloat("Metallic", ref debugMetallic, 0.0f, 1.0f);
            ImGui.SliderFloat("Roughness", ref debugRoughness, 0.0f, 1.0f);
            ImGui.SliderFloat("Ambience", ref debugAmbient, 0.0f, 1.0f);
            ImGui.ColorEdit4("Albedo", ref debugAlbedo);

            ImGui.SliderFloat("Light Intensity", ref debugLightIntensity, 0.0f, 100.0f);
            ImGui.ColorEdit4("Light Color", ref debugLightColor);
            ImGui.SliderFloat("Debug Scene Scale", ref debugSceneScale, 0.35f, 2.75f);

            ImGui.End();            
            RenderIrradiance();
            RenderPrefilteredReflectionMap();

            var window = Context.Window;
            Context.gl.Viewport(window.FramebufferSize);
            Context.gl.ClearColor<float>(DefaultClearColor.ToVector4D() * 255.0f);
            Clear();

            RenderEnvironmentBackground();

            Context.gl.Enable(EnableCap.DepthTest);
            Context.gl.Viewport(window.FramebufferSize);
            Context.gl.ClearDepth(0);
            Context.gl.Enable(EnableCap.DepthTest);
            Context.gl.DepthFunc(DepthFunction.Lequal);
            Context.gl.ColorMask(true, true, true, true);
            Context.gl.ClearDepth(1.0);
            Clear();

            drawScenes();

            if (drawDebugWorld) 
            {
                RenderTestScene();
            }
            
        }

        //private static Vector4[] lightPositions =
        //{
        //    new Vector4(-10.0f,  10.0f, 10.0f, 1.0f),
        //    new Vector4( 10.0f,  10.0f, 10.0f, 1.0f),
        //    new Vector4(-10.0f, -10.0f, 10.0f, 1.0f),
        //    new Vector4(64, 64, -64, 1.0f),
        //};

        private static readonly Vector3[] lightPositionOffsets =
        {
            new Vector3(-12.0f,  12.0f, 12.0f),
            new Vector3( 12.0f,  12.0f, 12.0f),
            new Vector3(-12.0f,  -8.0f, 12.0f),
            new Vector3( 12.0f,  -8.0f, 12.0f),
        };

        private Vector3 debugSceneCenter = new Vector3(64.0f, 3.0f, 64.0f);

        
        private static readonly Vector3[] lightColors =
        {
            new Vector3(220.0f, 220.0f, 220.0f),
            new Vector3(220.0f, 220.0f, 220.0f),
            new Vector3(200.0f, 200.0f, 200.0f),
            new Vector3(200.0f, 200.0f, 200.0f)
        };



        private static Matrix4x4 captureProjection = Matrix4x4.CreatePerspectiveFieldOfView(90.0f.ToRadians(), 1.0f, 0.1f, 100.0f);
        private static Matrix4x4[] captureViews = new Matrix4x4[]
        {
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f,  0.0f,  0.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(-1.0f, 0.0f,  0.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  1.0f,  0.0f), new Vector3(0.0f,  0.0f,  1.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f,  0.0f), new Vector3(0.0f,  0.0f, -1.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  0.0f,  1.0f), new Vector3(0.0f, -1.0f,  0.0f)),
                Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f,  0.0f, -1.0f), new Vector3(0.0f, -1.0f,  0.0f))
        };

        public void RenderEnvironmentMapTarget()
        {
            for (int m = 0; m < EnvironmentCubeTarget.MaxMips; m++)
            {
                for (int i = 0; i < 6; i++)
                {
                    EnvironmentCubeTarget.Bind(i, m);
                    Clear();

                    EnvironmentShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.CreateScale(100), Context.World.MainCamera);
                    EnvironmentShader.BindEnvironmentTexture();
                    EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);
                    EnvironmentShader.FindUniform("projection").SetValue(captureProjection);
                    EnvironmentShader.FindUniform("view").SetValue(captureViews[i]);

                    SimpleCube.RenderDirectly();
                }
            }

            EnvironmentCubeTarget.UnBind();
        }

        public void RenderBrdfTarget()
        {
            BrdfFramebuffer.Bind();           
            var clearColor = Colors.Pink;
            Context.gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
            Clear();
            EnvironmentBrdfShader.BindShader();
            SimpleQuad.RenderDirectly();
            BrdfFramebuffer.UnBind();
        }

        public void RenderEnvironmentBackground()
        {
            var directionOnly = Context.World.MainCamera._ViewMatrix;
            directionOnly.Translation = new Vector3(0, 0, 0);

            EnvironmentShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.CreateScale(100), Context.World.MainCamera);
            EnvironmentShader.BindEnvironmentTexture();
            EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);
            EnvironmentShader.FindUniform("view").SetValue(directionOnly);

            Context.gl.Disable(EnableCap.DepthTest);
            SimpleCube.RenderDirectly();
        }

        public void RenderIrradiance()
        {
            Vector4D<float> irradanceTestColor = Colors.Black.ToVector4D();
            for (int i = 0; i < 6; i++)
            {
                IrradianceFramebuffer.Bind(i);
                Context.gl.ClearColor<float>(irradanceTestColor);
                Clear();

                EnvironmentIrradianceShader.BindShader();
                EnvironmentMap.Bind(0);
                EnvironmentIrradianceShader.FindUniform("environmentMap").SetValue(0);
                EnvironmentIrradianceShader.FindUniform("projection").SetValue(captureProjection);
                EnvironmentIrradianceShader.FindUniform("view").SetValue(captureViews[i]);

                SimpleCube.RenderDirectly();

            }

            IrradianceFramebuffer.UnBind();
        }

        public void RenderPrefilteredReflectionMap()
        {
            Vector4D<float> prefilterTestColor = Colors.White.ToVector4D();

            int m = preFilterSlice;
            preFilterSlice = preFilterSlice >= PrefilterFramebuffer.MaxMips - 1 ? 0 : preFilterSlice + 1;

            uint[] samplesByMip = new uint[] { 32u, 64u, 128u, 512u, 1024u };

            for (int i = 0; i < 6; i++)
            {
                PrefilterFramebuffer.Bind(i, m);
                Context.gl.ClearColor<float>(prefilterTestColor);
                ClearDepth();

                float roughness = (float)m / (float)(PrefilterFramebuffer.MaxMips - 1);

                if (roughness < 0.1f)
                {
                    // Makes a very pretty reflection, and fast
                    EnvironmentShader.BindShader();
                    EnvironmentShader.BindEnvironmentTexture();
                    EnvironmentShader.FindUniform("equirectangularMap").SetValue(0);
                    EnvironmentShader.FindUniform("projection").SetValue(captureProjection);
                    EnvironmentShader.FindUniform("view").SetValue(captureViews[i]);
                }
                else
                {
                    // Makes a very blurry reflection, but slower
                    EnvironmentPrefilterShader.BindShader();
                    EnvironmentMap.Bind(0);
                    EnvironmentPrefilterShader.FindUniform("environmentMap").SetValue(0);
                    EnvironmentPrefilterShader.FindUniform("SAMPLE_COUNT").SetValue(samplesByMip[Math.Clamp(preFilterSlice, 0, samplesByMip.Length - 1)]);
                    EnvironmentPrefilterShader.FindUniform("projection").SetValue(captureProjection);
                    EnvironmentPrefilterShader.FindUniform("view").SetValue(captureViews[i]);
                    EnvironmentPrefilterShader.FindUniform("roughness").SetValue(roughness);
                }

                SimpleCube.RenderDirectly();
            }


            PrefilterFramebuffer.UnBind();
        }

        private int preFilterSlice = 0;

        private List<ManaMeshSceneNode> TestModels = new List<ManaMeshSceneNode>();

        private ManaMeshSceneNode? DebugModel = null;

        void RenderTestScene()
        {
            Matrix4x4 model = Matrix4x4.Identity;
            float rows = 5;
            float cols = 6;
            float spacing = 1.1f * debugSceneScale;
            var rowCenter = (rows - 1) * 0.5f;
            var colCenter = (cols - 1) * 0.5f;

            if (TestModels.Count == 0)
            {
                Func<Vector3, float, float, ManaMeshSceneNode> createSphereModelAtPoint = (pos, metallic, roughness) =>
                {
                    ManaMeshSceneNode newNode = new ManaMeshSceneNode(Context, SimpleSphere);
                    newNode.Name = $"TestSphere_{pos}_{metallic}x{roughness}";
                    newNode.MeshRenderer.DynamicMaterial.Metallic.Value = metallic;
                    newNode.MeshRenderer.DynamicMaterial.Metallic.Value = metallic;
                    newNode.MeshRenderer.DynamicMaterial.Roughness.Value = roughness;
                    newNode.MeshRenderer.DynamicMaterial.Albedo.Value = new Vector4(roughness * 360, 0.5f, 0.5f, 1.0f).ToRgbFromHsl();
                    newNode.Position = pos;
                    return newNode;
                };

                Func<Vector3, float, float, ManaMeshSceneNode> createCubeModelAtPoint = (pos, metallic, roughness) =>
                {
                    ManaMeshSceneNode newNode = new ManaMeshSceneNode(Context, SimpleCube);
                    newNode.Name = $"TestCube_{pos}_{metallic}x{roughness}";
                    newNode.MeshRenderer.DynamicMaterial.Metallic.Value = metallic;
                    newNode.MeshRenderer.DynamicMaterial.Roughness.Value = roughness;
                    newNode.Position = pos;
                    return newNode;
                };

                for (int row = 0; row < rows; ++row)
                {
                    float metallic = row / rows;
                    for (int col = 0; col < cols; ++col)
                    {
                        float roughness = Math.Clamp(col / (float)cols, 0.05f, 1.0f);

                        Vector3 gridPosition = new Vector3((col - colCenter) * spacing, (row - rowCenter) * spacing, -1.4f * debugSceneScale);
                        gridPosition += debugSceneCenter;
                        var sphereModel = createSphereModelAtPoint(gridPosition, metallic, roughness);
                        gridPosition.Z = debugSceneCenter.Z - 4.8f * debugSceneScale;
                        var cubeModel = createCubeModelAtPoint(gridPosition, metallic, roughness);

                        TestModels.Add(cubeModel);
                        TestModels.Add(sphereModel);

                    }
                }
            }

            DebugModel = new ManaMeshSceneNode(Context, SimpleCube);
            DebugModel.LocalTransform = Matrix4x4.CreateTranslation(debugSceneCenter + new Vector3(4.0f, 0.0f, 0.5f) * debugSceneScale);


            foreach (var testModel in TestModels)
            {
                testModel.UpdateTransform();
                testModel.Draw();
            }


            model = Matrix4x4.CreateRotationY(-90.0f.ToRadians()) * Matrix4x4.CreateTranslation(debugSceneCenter + new Vector3(5.0f, 0.0f, -1.0f) * debugSceneScale);
            BasicCubeShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.Identity, Context.World.MainCamera);
            EnvironmentMap.Bind(0);
            BasicCubeShader.FindUniform("model").SetValue(model);
            SimpleCube.RenderDirectly();

            model = Matrix4x4.CreateRotationY(-90.0f.ToRadians()) * Matrix4x4.CreateTranslation(debugSceneCenter + new Vector3(5.0f, 0.0f, 1.0f) * debugSceneScale);
            BasicCubeShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.Identity, Context.World.MainCamera);
            IrradianceFramebuffer.bufferMap["Irradiance"]?.Texture?.Bind(0);
            BasicCubeShader.FindUniform("model").SetValue(model);
            SimpleCube.RenderDirectly();

            model = Matrix4x4.CreateRotationY(-90.0f.ToRadians()) * Matrix4x4.CreateTranslation(debugSceneCenter + new Vector3(5.0f, 0.0f, 3.0f) * debugSceneScale);
            BasicCubeShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.Identity, Context.World.MainCamera);
            PrefilterFramebuffer.bufferMap["PreFilter"]?.Texture?.Bind(0);
            BasicCubeShader.FindUniform("model").SetValue(model);
            SimpleCube.RenderDirectly();

            model = Matrix4x4.CreateRotationY(-90.0f.ToRadians()) * Matrix4x4.CreateTranslation(debugSceneCenter + new Vector3(5.0f, 0.0f, 5.0f) * debugSceneScale);
            BasicShader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Matrix4x4.Identity, Context.World.MainCamera);
            BrdfFramebuffer.bufferMap["BRDF"]?.Texture?.Bind(0);
            BasicShader.FindUniform("model").SetValue(model);
            SimpleQuad.RenderDirectly();
        }

        public void ApplyLights(ManaPbrShader manaPbrShader)
        {
            for (int i = 0; i < lightPositionOffsets.Length; ++i)
            {
                var lightPosition = debugSceneCenter + lightPositionOffsets[i] * debugSceneScale;
                manaPbrShader.FindUniform($"lightPositions[0]")?.SetValue(lightPosition, i);
                manaPbrShader.FindUniform($"lightColors[0]")?.SetValue(lightColors[i], i);
            }
        }
    }
}


