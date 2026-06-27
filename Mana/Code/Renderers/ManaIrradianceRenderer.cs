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
        
        public ManaRenderTarget EnvironmentCubeTarget = null!;
        public ManaRenderTarget IrradianceFramebuffer = null!;
        public ManaRenderTarget PrefilterFramebuffer = null!;
        public ManaRenderTarget BrdfFramebuffer = null!;

        public ManaEnvironmentIrradianceShader EnvironmentIrradianceShader = null!;
        public ManaEnvironmentCubeShader EnvironmentShader = null!;
        public ManaEnvironmentBrdfShader EnvironmentBrdfShader = null!;
        public ManaEnvironmentPrefilterShader EnvironmentPrefilterShader = null!;

        public ManaBasicTextureShader BasicShader = null!;
        public ManaBasicTextureShader BasicCubeShader = null!;

        public ManaTexture EnvironmentMap = null!;

        public ManaCube SimpleCube = null!;
        public ManaQuad SimpleQuad = null!;
        public ManaSphere SimpleSphere = null!;
        public ManaTorus SimpleTorus = null!;
        public ManaIcosahedron SimpleIcosahedron = null!;
        public ManaIsoSphere SimpleIsoSphere = null!;
        public ManaPyramid SimplePyramid = null!;

        bool drawDebugWorld = false;
        float debugMetallic = 1.0f;
        float debugAmbient = 0.25f;
        float debugRoughness = 0.1f;
        Vector4 debugAlbedo = Colors.White;
        float debugSceneScale = 1.0f;

        float debugLightIntensity = 1.35f;
        Vector4 debugLightColor = Colors.White;
        float debugPbrExposure = 0.92f;
        float debugIblIntensity = 0.90f;

        public float PbrExposure => debugPbrExposure;
        public float IblIntensity => debugIblIntensity;

        public override void Initialize()
        {
            var gl = Context.RequireOpenGl();
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
            SimpleTorus = new ManaTorus(Context, Colors.White);
            SimpleIcosahedron = new ManaIcosahedron(Context, Colors.White, 0.6f);
            SimpleIsoSphere = new ManaIsoSphere(Context, Colors.White, 0.55f);
            SimplePyramid = new ManaPyramid(Context, Colors.White, Vector3.One);

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

        }

        public override void Resize(int width, int height) { }

        public override void Clear()
        {
            Context.RequireOpenGl().Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        }

        public void ClearDepth()
        {
            Context.RequireOpenGl().Clear((uint)(ClearBufferMask.DepthBufferBit));
        }


        public override void DrawImGui()
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Irradiance");

            if (ImGui.Checkbox("Debug World", ref drawDebugWorld))
            {
                SyncDebugSceneMembership();
            }

            ImGui.SliderFloat("Metallic", ref debugMetallic, 0.0f, 1.0f);
            ImGui.SliderFloat("Roughness", ref debugRoughness, 0.0f, 1.0f);
            ImGui.SliderFloat("Ambience", ref debugAmbient, 0.0f, 1.0f);
            ImGui.ColorEdit4("Albedo", ref debugAlbedo);

            var lightSettingsChanged = false;
            lightSettingsChanged |= ImGui.SliderFloat("Light Intensity", ref debugLightIntensity, 0.0f, 8.0f);
            lightSettingsChanged |= ImGui.ColorEdit4("Light Color", ref debugLightColor);
            if (lightSettingsChanged)
            {
                UpdateGridLightSettings();
            }

            ImGui.SliderFloat("Exposure", ref debugPbrExposure, 0.25f, 3.0f);
            ImGui.SliderFloat("IBL Intensity", ref debugIblIntensity, 0.0f, 4.0f);
            if (ImGui.SliderFloat("Debug Scene Scale", ref debugSceneScale, 0.35f, 2.75f))
            {
                SyncDebugSceneTransform();
            }

            ImGui.End();
        }

        public override void Render(Action drawScenes)
        {
            RenderIrradiance();
            RenderPrefilteredReflectionMap();

            var window = Context.Window;
            Context.RequireOpenGl().Viewport(window.FramebufferSize);
            Context.RequireOpenGl().ClearColor<float>(DefaultClearColor.ToVector4D());
            Context.RequireOpenGl().ClearDepth(1.0);
            Context.RequireOpenGl().DepthFunc(DepthFunction.Lequal);
            Context.RequireOpenGl().ColorMask(true, true, true, true);
            Clear();

            RenderEnvironmentBackground();

            Context.RequireOpenGl().Enable(EnableCap.DepthTest);
            Context.RequireOpenGl().Viewport(window.FramebufferSize);
            Context.RequireOpenGl().ClearDepth(1.0);
            ClearDepth();

            SyncGridLightRig();
            SyncDebugSceneMembership();
            drawScenes();
            
        }

        private const float DebugGridSize = 256.0f;
        private const float GridLightPowerScale = DebugGridSize * DebugGridSize;

        private static readonly Vector3[] gridLightPositions =
        {
            new Vector3(0.0f, DebugGridSize, 0.0f),
            new Vector3(DebugGridSize, DebugGridSize, 0.0f),
            new Vector3(0.0f, DebugGridSize, DebugGridSize),
            new Vector3(DebugGridSize, DebugGridSize, DebugGridSize),
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

            for (int i = 0; i < gridLightPositions.Length; ++i)
            {
                var lightNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(gridLightPositions[i]))
                {
                    Name = gridLightNames[i]
                };

                var light = new ManaLightSourceComponent(lightNode)
                {
                    Name = "PBR Point Light",
                    Color = debugLightColor,
                    Intensity = debugLightIntensity * lightWeights[i]
                };

                lightNode.Components.Add(light);
                gridLightRigNode.AddChild(lightNode);
                gridLightNodes.Add(lightNode);
            }

            return gridLightRigNode;
        }

        private void UpdateGridLightSettings()
        {
            for (int i = 0; i < gridLightNodes.Count; ++i)
            {
                var light = GetLightComponent(gridLightNodes[i]);
                if (light == null)
                {
                    continue;
                }

                light.Color = debugLightColor;
                light.Intensity = debugLightIntensity * lightWeights[Math.Clamp(i, 0, lightWeights.Length - 1)];
            }
        }

        private ManaSceneNode? debugSceneNode;

        public ManaSceneNode GetOrCreateDebugSceneNode()
        {
            return EnsureDebugSceneNode();
        }

        public void AddDebugSceneToWorld()
        {
            drawDebugWorld = true;
            AttachDebugSceneToWorld();
        }

        public void RemoveDebugSceneFromWorld()
        {
            drawDebugWorld = false;
            DetachDebugSceneFromWorld();
        }

        private void SyncDebugSceneMembership()
        {
            if (drawDebugWorld)
            {
                AttachDebugSceneToWorld();
            }
            else
            {
                DetachDebugSceneFromWorld();
            }
        }

        private void AttachDebugSceneToWorld()
        {
            var node = EnsureDebugSceneNode();

            if (node.Parent != null)
            {
                node.Parent.RemoveChild(node);
            }

            if (!Context.World.DefaultScene.Children.Contains(node))
            {
                Context.World.DefaultScene.Children.Add(node);
            }

            SyncDebugSceneTransform();
        }

        private void DetachDebugSceneFromWorld()
        {
            if (debugSceneNode == null)
            {
                return;
            }

            foreach (var scene in Context.World.Scenes)
            {
                scene.Children.Remove(debugSceneNode);
            }

            debugSceneNode.Parent?.RemoveChild(debugSceneNode);
        }

        private void SyncDebugSceneTransform()
        {
            if (debugSceneNode == null)
            {
                return;
            }

            debugSceneNode.Position = debugSceneCenter;
            debugSceneNode.Scale = Vector3.One * debugSceneScale;
            debugSceneNode.UpdateTransform();
        }

        private ManaSceneNode EnsureDebugSceneNode()
        {
            if (debugSceneNode != null)
            {
                return debugSceneNode;
            }

            debugSceneNode = new ManaSceneNode(Context, Matrix4x4.Identity)
            {
                Name = "PBR Debug Scene",
                Position = debugSceneCenter,
                Scale = Vector3.One * debugSceneScale
            };

            BuildDebugScene(debugSceneNode);
            debugSceneNode.UpdateTransform();
            return debugSceneNode;
        }

        private void BuildDebugScene(ManaSceneNode root)
        {
            const int rows = 5;
            const int cols = 6;
            const float spacing = 2.0f;
            const float shapeSpacing = 10.0f;

            var shapeSamples = new (string Name, ManaMesh Mesh, Vector3 Rotation)[]
            {
                ("Cube", SimpleCube, Vector3.Zero),
                ("Sphere", SimpleSphere, Vector3.Zero),
                ("Torus", SimpleTorus, new Vector3(65.0f.ToRadians(), 0.0f, 0.0f)),
                ("Icosahedron", SimpleIcosahedron, Vector3.Zero),
                ("Icosphere", SimpleIsoSphere, Vector3.Zero),
                ("Pyramid", SimplePyramid, Vector3.Zero)
            };

            var rowCenter = (rows - 1) * 0.5f;
            var colCenter = (cols - 1) * 0.5f;
            var shapeCenter = (shapeSamples.Length - 1) * 0.5f;

            for (int row = 0; row < rows; ++row)
            {
                float metallic = row / (float)rows;

                for (int col = 0; col < cols; ++col)
                {
                    float roughness = Math.Clamp(col / (float)cols, 0.05f, 1.0f);
                    var rowColumnOffset = new Vector3((col - colCenter) * spacing, (row - rowCenter) * spacing, 0.0f);

                    for (int shapeIndex = 0; shapeIndex < shapeSamples.Length; ++shapeIndex)
                    {
                        var shape = shapeSamples[shapeIndex];
                        var shapeOffset = new Vector3(0.0f, 0.0f, (shapeIndex - shapeCenter) * shapeSpacing);
                        var debugNode = CreatePbrDebugNode(
                            $"PBR {shape.Name} M{metallic:0.##} R{roughness:0.##}",
                            shape.Mesh,
                            rowColumnOffset + shapeOffset,
                            metallic,
                            roughness);

                        debugNode.Rotation = shape.Rotation;
                        root.AddChild(debugNode);
                    }
                }
            }

            root.AddChild(CreateTexturePreviewNode(
                "Environment Map Preview",
                SimpleCube,
                BasicCubeShader,
                () => EnvironmentMap,
                new Vector3(5.0f, 0.0f, -1.0f)));

            root.AddChild(CreateTexturePreviewNode(
                "Irradiance Map Preview",
                SimpleCube,
                BasicCubeShader,
                () => IrradianceFramebuffer.bufferMap["Irradiance"]?.Texture,
                new Vector3(5.0f, 0.0f, 1.0f)));

            root.AddChild(CreateTexturePreviewNode(
                "Prefilter Map Preview",
                SimpleCube,
                BasicCubeShader,
                () => PrefilterFramebuffer.bufferMap["PreFilter"]?.Texture,
                new Vector3(5.0f, 0.0f, 3.0f)));

            root.AddChild(CreateTexturePreviewNode(
                "BRDF LUT Preview",
                SimpleQuad,
                BasicShader,
                () => BrdfFramebuffer.bufferMap["BRDF"]?.Texture,
                new Vector3(5.0f, 0.0f, 5.0f)));
        }

        private ManaMeshSceneNode CreatePbrDebugNode(string name, ManaMesh mesh, Vector3 localPosition, float metallic, float roughness)
        {
            ManaMeshSceneNode newNode = new ManaMeshSceneNode(Context, mesh);
            newNode.Name = name;
            newNode.Position = localPosition;
            var material = newNode.MeshRenderer.DynamicMaterial;
            material.Name = $"{name} Material";
            material.Metallic.Value = metallic;
            material.Roughness.Value = roughness;
            material.Albedo.Value = new Vector4(roughness * 360, 0.5f, 0.5f, 1.0f).ToRgbFromHsl();
            return newNode;
        }

        private ManaSceneNode CreateTexturePreviewNode(string name, ManaMesh mesh, ManaBasicTextureShader shader, Func<ManaTexture?> getTexture, Vector3 localPosition)
        {
            var node = new ManaSceneNode(Context, Matrix4x4.Identity)
            {
                Name = name,
                Position = localPosition,
                Rotation = new Vector3(0.0f, -90.0f.ToRadians(), 0.0f)
            };

            node.Components.Add(new DebugTexturePreviewComponent(node, mesh, shader, getTexture));
            return node;
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
                var lightColor = light?.Enabled == false
                    ? Vector3.Zero
                    : (light?.Color ?? debugLightColor).ToVector3()
                        * (light?.Intensity ?? debugLightIntensity * lightWeights[i])
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

        private class DebugTexturePreviewComponent : ManaComponent
        {
            private readonly ManaMesh mesh;
            private readonly ManaBasicTextureShader shader;
            private readonly Func<ManaTexture?> getTexture;

            public DebugTexturePreviewComponent(
                ManaSceneNode owner,
                ManaMesh mesh,
                ManaBasicTextureShader shader,
                Func<ManaTexture?> getTexture) : base(owner)
            {
                this.mesh = mesh;
                this.shader = shader;
                this.getTexture = getTexture;
            }

            public override void DrawComponent()
            {
                base.DrawComponent();

                var texture = getTexture();
                if (texture == null)
                {
                    return;
                }

                shader.BindShader(ManaMaterial.GetDefaultMaterial(Context), Owner.WorldTransform, Context.World.MainCamera, Owner);
                shader.FindUniform("mainTexture")?.SetValue(0);
                texture.Bind(0);
                mesh.RenderDirectly();
                texture.UnBind(0);
                shader.UnbindShader();
            }
        }
    }
}


