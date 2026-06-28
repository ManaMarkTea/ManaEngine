using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code.Renderers
{
    public sealed class ManaIrradianceDebugScene
    {
        private const int Rows = 5;
        private const int Columns = 6;
        private const float Spacing = 2.0f;
        private const float ShapeSpacing = 10.0f;
        private static readonly string[] DebugModeNames =
        {
            "Off",
            "View Vector",
            "World Normal",
            "Reflection",
            "Cubemap Dir",
            "Reflection Radiance",
            "BRDF Scale",
            "Linear Albedo",
            "F0",
            "Fresnel",
            "BRDF LUT",
            "Diffuse IBL",
            "Specular IBL",
            "Direct Lighting",
            "Final Linear"
        };

        private readonly ManaContext context;
        private readonly ManaIrradianceSettings settings;
        private readonly Vector3 debugSceneCenter = new Vector3(64.0f, 32.0f, 64.0f);
        private readonly List<DebugMaterialBinding> materialBindings = new();

        private ManaSceneNode? debugSceneNode;

        public ManaIrradianceDebugScene(ManaContext context, ManaIrradianceSettings settings)
        {
            this.context = context;
            this.settings = settings;
        }

        public ManaSceneNode GetOrCreateDebugSceneNode()
        {
            return EnsureDebugSceneNode();
        }

        public void SyncMembership()
        {
            if (settings.DrawDebugWorld)
            {
                AttachDebugSceneToWorld();
            }
            else
            {
                DetachDebugSceneFromWorld();
            }
        }

        public void SyncTransform()
        {
            if (debugSceneNode == null)
            {
                return;
            }

            debugSceneNode.Position = debugSceneCenter;
            debugSceneNode.Scale = Vector3.One * settings.DebugSceneScale;
            debugSceneNode.UpdateTransform();
        }

        public void SyncMaterials()
        {
            foreach (var binding in materialBindings)
            {
                var material = binding.Material;
                material.Albedo.Value = binding.UsePanelMaterial
                    ? settings.DebugAlbedo
                    : TintDebugAlbedo(Vector4.One, binding.Metallic, binding.Roughness);
                material.Metallic.Value = binding.UsePanelMaterial ? settings.DebugMetallic : binding.Metallic;
                material.Roughness.Value = binding.UsePanelMaterial ? settings.DebugRoughness : binding.Roughness;
                material.Ambient.Value = settings.DebugAmbient;
            }
        }

        private void AttachDebugSceneToWorld()
        {
            var node = EnsureDebugSceneNode();

            if (node.Parent != null)
            {
                node.Parent.RemoveChild(node);
            }

            if (!context.World.DefaultScene.Children.Contains(node))
            {
                context.World.DefaultScene.Children.Add(node);
            }

            SyncTransform();
            SyncMaterials();
        }

        private void DetachDebugSceneFromWorld()
        {
            if (debugSceneNode == null)
            {
                return;
            }

            foreach (var scene in context.World.Scenes)
            {
                scene.Children.Remove(debugSceneNode);
            }

            debugSceneNode.Parent?.RemoveChild(debugSceneNode);
        }

        private ManaSceneNode EnsureDebugSceneNode()
        {
            if (debugSceneNode != null)
            {
                return debugSceneNode;
            }

            debugSceneNode = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = "PBR Debug Scene",
                Position = debugSceneCenter,
                Scale = Vector3.One * settings.DebugSceneScale,
                IncludeInEnvironmentCapture = false
            };

            BuildDebugScene(debugSceneNode);
            SyncMaterials();
            debugSceneNode.UpdateTransform();
            return debugSceneNode;
        }

        private void BuildDebugScene(ManaSceneNode root)
        {
            var shapeSamples = new (string Name, ManaMesh Mesh, Vector3 Rotation)[]
            {
                ("Cube", new ManaCube(context, Colors.White, Vector3.One), Vector3.Zero),
                ("Sphere", new ManaSphere(context, Colors.White, 0.5f), Vector3.Zero),
                ("Torus", new ManaTorus(context, Colors.White), new Vector3(65.0f.ToRadians(), 0.0f, 0.0f)),
                ("Icosahedron", new ManaIcosahedron(context, Colors.White, 0.6f), Vector3.Zero),
                ("Icosphere", new ManaIsoSphere(context, Colors.White, 0.55f), Vector3.Zero),
                ("Pyramid", new ManaPyramid(context, Colors.White, Vector3.One), Vector3.Zero)
            };

            var rowCenter = (Rows - 1) * 0.5f;
            var colCenter = (Columns - 1) * 0.5f;
            var shapeCenter = (shapeSamples.Length - 1) * 0.5f;

            var sampleGroup = CreateGroupNode("Sample", new Vector3(-(Columns + 3.5f) * Spacing, 0.0f, 0.0f));
            BuildDebugModeSamples(sampleGroup, shapeSamples[2].Mesh, shapeSamples[2].Rotation);
            root.AddChild(sampleGroup);

            for (int shapeIndex = 0; shapeIndex < shapeSamples.Length; ++shapeIndex)
            {
                var shape = shapeSamples[shapeIndex];
                var shapeOffset = new Vector3(0.0f, 0.0f, (shapeIndex - shapeCenter) * ShapeSpacing);
                var shapeGroup = CreateGroupNode($"{shape.Name}s", shapeOffset);
                root.AddChild(shapeGroup);

                for (int row = 0; row < Rows; ++row)
                {
                    float metallic = row / (float)(Rows - 1);

                    for (int col = 0; col < Columns; ++col)
                    {
                        float roughness = Math.Clamp(col / (float)(Columns - 1), 0.05f, 1.0f);
                        var rowColumnOffset = new Vector3((col - colCenter) * Spacing, (row - rowCenter) * Spacing, 0.0f);
                        var debugNode = CreateDebugNode(
                            $"{shape.Name}Grid M{metallic:0.##} R{roughness:0.##}",
                            shape.Mesh,
                            rowColumnOffset,
                            metallic,
                            roughness,
                            usePanelMaterial: false);

                        debugNode.Rotation = shape.Rotation;
                        shapeGroup.AddChild(debugNode);
                    }
                }
            }
        }

        private void BuildDebugModeSamples(ManaSceneNode sampleGroup, ManaMesh mesh, Vector3 rotation)
        {
            const int sampleColumns = 5;
            var sampleRows = (int)Math.Ceiling(DebugModeNames.Length / (float)sampleColumns);
            var columnCenter = (sampleColumns - 1) * 0.5f;
            var rowCenter = (sampleRows - 1) * 0.5f;

            for (var mode = 0; mode < DebugModeNames.Length; mode++)
            {
                var column = mode % sampleColumns;
                var row = mode / sampleColumns;
                var offset = new Vector3(
                    (column - columnCenter) * Spacing,
                    (rowCenter - row) * Spacing,
                    0.0f);

                var sample = CreateDebugNode(
                    $"{mode:00} {DebugModeNames[mode]}",
                    mesh,
                    offset,
                    settings.DebugMetallic,
                    settings.DebugRoughness,
                    usePanelMaterial: true,
                    pbrDebugMode: mode);
                sample.Rotation = rotation;
                sampleGroup.AddChild(sample);
            }
        }

        private ManaSceneNode CreateGroupNode(string name, Vector3 localPosition)
        {
            return new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = name,
                Position = localPosition
            };
        }

        private ManaMeshSceneNode CreateDebugNode(
            string name,
            ManaMesh mesh,
            Vector3 localPosition,
            float metallic,
            float roughness,
            bool usePanelMaterial,
            float? pbrDebugMode = null)
        {
            var node = new ManaMeshSceneNode(context, mesh)
            {
                Name = name,
                Position = localPosition
            };

            var material = new ManaMaterial(context)
            {
                Name = $"{name} Material"
            };

            node.MeshRenderer.Material = material;
            if (pbrDebugMode.HasValue)
            {
                material.SetShaderUniform("pbrDebugMode", pbrDebugMode.Value);
            }

            materialBindings.Add(new DebugMaterialBinding(material, metallic, roughness, usePanelMaterial));
            return node;
        }

        private static Vector4 TintDebugAlbedo(Vector4 baseAlbedo, float metallic, float roughness)
        {
            var hue = Math.Clamp(roughness, 0.0f, 1.0f) * 300.0f;
            var saturation = Math.Clamp(0.45f + metallic * 0.35f, 0.0f, 1.0f);
            var lightness = Math.Clamp(0.42f + roughness * 0.18f, 0.0f, 1.0f);
            var tint = new Vector4(hue, saturation, lightness, 1.0f).ToRgbFromHsl();
            return new Vector4(
                baseAlbedo.X * tint.X,
                baseAlbedo.Y * tint.Y,
                baseAlbedo.Z * tint.Z,
                baseAlbedo.W);
        }

        private sealed record DebugMaterialBinding(
            ManaMaterial Material,
            float Metallic,
            float Roughness,
            bool UsePanelMaterial);
    }
}
