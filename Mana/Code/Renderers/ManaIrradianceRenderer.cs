using Mana.Code.Nodes;
using Mana.Code.Graphics;
using ManaCore.Graphics;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Mana.Code.Renderers
{
    public class ManaIrradianceRenderer : ManaRenderer
    {
        public ManaIrradianceRenderer(ManaContext context) : base(context)
        {

        }
        
        public ManaRenderTarget EnvironmentCubeTarget = null!;
        public ManaRenderTarget IrradianceFramebuffer = null!;
        public ManaRenderTarget PrefilterFramebuffer = null!;
        public ManaRenderTarget BrdfFramebuffer = null!;

        public ManaEnvironmentIrradianceShader EnvironmentIrradianceShader = null!;
        public ManaEnvironmentCubeShader EnvironmentShader = null!;
        public ManaEnvironmentBrdfShader EnvironmentBrdfShader = null!;
        public ManaEnvironmentPrefilterShader EnvironmentPrefilterShader = null!;

        public ManaTexture EnvironmentMap = null!;
        public IManaEnvironmentReflectionMap? EnvironmentReflectionMap { get; private set; }
        public ManaOpenGlHdrPostProcessor? PostProcessor { get; private set; }

        public ManaCube SimpleCube = null!;
        public ManaQuad SimpleQuad = null!;

        private ManaIrradianceSettings IrradianceSettings => Context.World.IrradianceSettings;
        public float PbrExposure => IrradianceSettings.Exposure;
        public float IblIntensity => IrradianceSettings.IblIntensity;

        public override void Initialize()
        {
            var gl = Context.RequireOpenGl();
            gl.Enable(EnableCap.DepthTest);
            gl.Enable(EnableCap.TextureCubeMapSeamless);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.ColorMask(true, true, true, true);
            gl.ClearDepth(1.0);

            // Shaders
            EnvironmentIrradianceShader = new ManaEnvironmentIrradianceShader(Context);
            EnvironmentShader = new ManaEnvironmentCubeShader(Context);
            EnvironmentBrdfShader = new ManaEnvironmentBrdfShader(Context);
            EnvironmentPrefilterShader = new ManaEnvironmentPrefilterShader(Context);

            // Basic Shapes
            SimpleCube = new ManaCube(Context, Colors.White, Vector3.One);
            SimpleQuad = new ManaQuad(Context, Colors.White, Vector3.One * 2.0f);

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
            EnvironmentMap = EnvironmentCubeTarget.bufferMap["EnvironmentCube"].Texture
                ?? throw new InvalidOperationException("Environment cube buffer did not create a texture.");


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

            EnvironmentReflectionMap = new ManaEnvironmentReflectionMap(
                ManaGraphicsBackendKind.OpenGL,
                Context.World.EnvironmentReflectionSource,
                EnvironmentMap?.GpuTexture,
                IrradianceFramebuffer.bufferMap["Irradiance"].Texture?.GpuTexture,
                PrefilterFramebuffer.bufferMap["PreFilter"].Texture?.GpuTexture,
                BrdfFramebuffer.bufferMap["BRDF"].Texture?.GpuTexture);

            var size = Context.Window.FramebufferSize;
            PostProcessor = new ManaOpenGlHdrPostProcessor(Context, size.X, size.Y);
        }

        public override void Resize(int width, int height)
        {
            PostProcessor?.Resize(width, height);
        }

        public override void Clear()
        {
            Context.RequireOpenGl().Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public void ClearDepth()
        {
            Context.RequireOpenGl().Clear((uint)(ClearBufferMask.DepthBufferBit));
        }

        public override void Render(Action drawScenes)
        {
            RenderIrradiance();
            RenderPrefilteredReflectionMap();

            if (PostProcessor == null)
            {
                var size = Context.Window.FramebufferSize;
                PostProcessor = new ManaOpenGlHdrPostProcessor(Context, size.X, size.Y);
            }

            PostProcessor.Begin();

            var gl = Context.RequireOpenGl();
            gl.Viewport(0, 0, PostProcessor.HdrSceneTarget.Width, PostProcessor.HdrSceneTarget.Height);
            gl.ClearColor(DefaultClearColor.X, DefaultClearColor.Y, DefaultClearColor.Z, DefaultClearColor.W);
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            gl.DepthFunc(DepthFunction.Lequal);
            gl.ColorMask(true, true, true, true);

            RenderEnvironmentBackground();

            gl.Enable(EnableCap.DepthTest);
            gl.ClearDepth(1.0);
            gl.Clear((uint)ClearBufferMask.DepthBufferBit);

            SyncGridLightRig();
            drawScenes();

            PostProcessor.RenderOutlineMask(EnumerateOutlinedMeshRenderers());
            PostProcessor.End();
        }

        private IEnumerable<(ManaSceneNode Node, MeshRendererComponent Renderer)> EnumerateOutlinedMeshRenderers()
        {
            foreach (var scene in Context.World.Scenes)
            {
                foreach (var rootNode in scene.Children)
                {
                    foreach (var outlinedMesh in EnumerateOutlinedMeshRenderers(rootNode))
                    {
                        yield return outlinedMesh;
                    }
                }
            }
        }

        private static IEnumerable<(ManaSceneNode Node, MeshRendererComponent Renderer)> EnumerateOutlinedMeshRenderers(
            ManaSceneNode node)
        {
            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                if (renderer.IsVisible && renderer.RenderOutlineEnabled)
                {
                    yield return (node, renderer);
                }
            }

            foreach (var child in node.Children)
            {
                foreach (var outlinedMesh in EnumerateOutlinedMeshRenderers(child))
                {
                    yield return outlinedMesh;
                }
            }
        }

        private const float DebugGridSize = 256.0f;
        private const float GridLightPowerScale = DebugGridSize * DebugGridSize;

        private static readonly Vector3[] gridLightPositions =
        {
            new Vector3(-DebugGridSize * 0.5f, DebugGridSize, -DebugGridSize * 0.5f),
            new Vector3(DebugGridSize * 0.5f, DebugGridSize, -DebugGridSize * 0.5f),
            new Vector3(-DebugGridSize * 0.5f, DebugGridSize, DebugGridSize * 0.5f),
            new Vector3(DebugGridSize * 0.5f, DebugGridSize, DebugGridSize * 0.5f),
        };

        private static readonly string[] gridLightNames =
        {
            "PBR Grid Light Top Near Left",
            "PBR Grid Light Top Near Right",
            "PBR Grid Light Top Far Left",
            "PBR Grid Light Top Far Right",
        };

        private Vector3 debugSceneCenter = new Vector3(64.0f, 32.0f, 64.0f);

        
        private static readonly float[] lightWeights =
        {
            1.00f,
            0.65f,
            0.35f,
            0.35f
        };

        private ManaSceneNode? gridLightRigNode;
        private readonly List<ManaSceneNode> gridLightNodes = new();



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
            Context.RequireOpenGl().ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
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

            Context.RequireOpenGl().Disable(EnableCap.DepthTest);
            SimpleCube.RenderDirectly();
        }

        public void RenderIrradiance()
        {
            Vector4D<float> irradanceTestColor = Colors.Black.ToVector4D();
            for (int i = 0; i < 6; i++)
            {
                IrradianceFramebuffer.Bind(i);
                Context.RequireOpenGl().ClearColor<float>(irradanceTestColor);
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
                Context.RequireOpenGl().ClearColor<float>(prefilterTestColor);
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

        private void SyncGridLightRig()
        {
            if (Context.World == null || Context.World.DefaultScene == null)
            {
                return;
            }

            var rig = EnsureGridLightRigNode();
            UpdateGridLightSettings();
            if (rig.Parent != null)
            {
                rig.Parent.RemoveChild(rig);
            }

            if (!Context.World.DefaultScene.Children.Contains(rig))
            {
                Context.World.DefaultScene.Children.Add(rig);
            }

            rig.UpdateTransform();
        }

        private ManaSceneNode EnsureGridLightRigNode()
        {
            if (gridLightRigNode != null)
            {
                return gridLightRigNode;
            }

            gridLightRigNode = new ManaSceneNode(Context, Matrix4x4.Identity)
            {
                Name = "PBR Grid Lights"
            };

            var settings = IrradianceSettings;
            for (int i = 0; i < gridLightPositions.Length; ++i)
            {
                var lightNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(gridLightPositions[i]))
                {
                    Name = gridLightNames[i]
                };

                var light = new ManaLightSourceComponent(lightNode)
                {
                    Name = "PBR Point Light",
                    IsGridLight = true,
                    Color = settings.LightColor,
                    Intensity = settings.LightIntensity * lightWeights[i]
                };

                lightNode.Components.Add(light);
                gridLightRigNode.AddChild(lightNode);
                gridLightNodes.Add(lightNode);
            }

            return gridLightRigNode;
        }

        private void UpdateGridLightSettings()
        {
            var settings = IrradianceSettings;
            for (int i = 0; i < gridLightNodes.Count; ++i)
            {
                var light = GetLightComponent(gridLightNodes[i]);
                if (light == null)
                {
                    continue;
                }

                light.Color = settings.LightColor;
                light.Intensity = settings.LightIntensity * lightWeights[Math.Clamp(i, 0, lightWeights.Length - 1)];
            }
        }

        public void ApplyLights(ManaPbrShader manaPbrShader)
        {
            if (gridLightNodes.Count == 0)
            {
                SyncGridLightRig();
            }

            for (int i = 0; i < gridLightPositions.Length; ++i)
            {
                var lightNode = i < gridLightNodes.Count ? gridLightNodes[i] : null;
                var light = lightNode == null ? null : GetLightComponent(lightNode);
                var lightPosition = lightNode?.WorldTransform.Translation ?? gridLightPositions[i];
                var settings = IrradianceSettings;
                var lightColor = light?.Enabled == false
                    ? Vector3.Zero
                    : (light?.Color ?? settings.LightColor).ToVector3()
                        * (light?.Intensity ?? settings.LightIntensity * lightWeights[i])
                        * GridLightPowerScale;

                manaPbrShader.FindUniform($"lightPositions[0]")?.SetValue(lightPosition, i);
                manaPbrShader.FindUniform($"lightColors[0]")?.SetValue(lightColor, i);
            }
        }

        private static ManaLightSourceComponent? GetLightComponent(ManaSceneNode node)
        {
            foreach (var component in node.Components)
            {
                if (component is ManaLightSourceComponent light)
                {
                    return light;
                }
            }

            return null;
        }

        public override void GetLightPositionsAndColors(out Vector4[] positions, out Vector4[] colors)
        {
            positions = new Vector4[4];
            colors = new Vector4[4];

            if (gridLightNodes.Count == 0)
            {
                SyncGridLightRig();
            }

            for (int i = 0; i < gridLightPositions.Length; ++i)
            {
                var lightNode = i < gridLightNodes.Count ? gridLightNodes[i] : null;
                var light = lightNode == null ? null : GetLightComponent(lightNode);
                var lightPosition = lightNode?.WorldTransform.Translation ?? gridLightPositions[i];
                var settings = IrradianceSettings;
                var lightColor = light?.Enabled == false
                    ? Vector3.Zero
                    : (light?.Color ?? settings.LightColor).ToVector3()
                        * (light?.Intensity ?? settings.LightIntensity * lightWeights[i])
                        * GridLightPowerScale;

                positions[i] = new Vector4(lightPosition, 1.0f);
                colors[i] = new Vector4(lightColor, 1.0f);
            }
        }
    }
}
