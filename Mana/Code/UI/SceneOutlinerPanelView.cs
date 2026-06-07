using ImGuiNET;
using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    public class SceneOutlinerPanelView
    {
        public void Draw(ManaWorld world)
        {
            var dockId = BaseGameMode.OutlinerDockId != 0
                ? BaseGameMode.OutlinerDockId
                : BaseGameMode.UtilityDockId;

            if (dockId != 0)
            {
                ImGui.SetNextWindowDockID(dockId, ImGuiCond.Always);
            }

            ImGui.Begin("Outliner");

            ImGui.Text($"Scenes: {world.Scenes.Count}");
            ImGui.SeparatorText("Camera");
            DrawNode(world.CameraNode);

            ImGui.SeparatorText("Scenes");
            for (int i = 0; i < world.Scenes.Count; ++i)
            {
                DrawScene(world.Scenes[i], i);
            }

            ImGui.End();
        }

        private void DrawScene(ManaScene scene, int sceneIndex)
        {
            ImGui.PushID($"scene_{sceneIndex}");

            var label = $"{GetDisplayName(scene, $"Scene {sceneIndex}")} ({scene.Children.Count})";
            if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (scene.Children.Count == 0)
                {
                    ImGui.TextDisabled("Empty");
                }

                foreach (var node in scene.Children)
                {
                    DrawNode(node);
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        private void DrawNode(ManaSceneNode node)
        {
            ImGui.PushID(node.GetHashCode());

            var hasContents = node.Components.Count > 0 || node.Children.Count > 0;
            var label = $"{GetDisplayName(node, node.GetType().Name)} ({node.GetType().Name})";

            if (!hasContents)
            {
                ImGui.BulletText(label);
                DrawNodeTooltip(node);
                ImGui.PopID();
                return;
            }

            if (ImGui.TreeNode(label))
            {
                DrawNodeTooltip(node);
                DrawComponents(node);

                foreach (var child in node.Children)
                {
                    DrawNode(child);
                }

                ImGui.TreePop();
            }
            else
            {
                DrawNodeTooltip(node);
            }

            ImGui.PopID();
        }

        private void DrawComponents(ManaSceneNode node)
        {
            if (node.Components.Count == 0)
            {
                return;
            }

            ImGui.PushID("components");
            if (ImGui.TreeNode($"Components ({node.Components.Count})"))
            {
                foreach (var component in node.Components)
                {
                    ImGui.PushID(component.GetHashCode());
                    DrawComponent(component);
                    ImGui.PopID();
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        private void DrawComponent(ManaComponent component)
        {
            if (component is MeshRendererComponent meshRenderer)
            {
                DrawMeshRendererComponent(meshRenderer);
                return;
            }

            if (component is ManaLightSourceComponent light)
            {
                DrawLightSourceComponent(light);
                return;
            }

            ImGui.BulletText(GetComponentLabel(component));
        }

        private void DrawMeshRendererComponent(MeshRendererComponent meshRenderer)
        {
            var material = meshRenderer.Material;
            var label = $"{GetComponentLabel(meshRenderer)} | Material: {GetDisplayName(material, "Default Material")}";

            if (ImGui.TreeNode(label))
            {
                ImGui.TextUnformatted($"Mesh: {meshRenderer.Mesh.GetType().Name}");
                ImGui.TextUnformatted($"Material: {GetDisplayName(material, "Default Material")}");
                ImGui.TextUnformatted($"Shader: {material.Shader.GetType().Name}");

                if (material.Channels.Count > 0 && ImGui.TreeNode($"Channels ({material.Channels.Count})"))
                {
                    foreach (var channel in material.Channels)
                    {
                        var channelLabel = string.IsNullOrWhiteSpace(channel.Value.Name)
                            ? channel.Key
                            : channel.Value.Name;
                        ImGui.BulletText(channelLabel);
                    }

                    ImGui.TreePop();
                }

                ImGui.TreePop();
            }
        }

        private void DrawLightSourceComponent(ManaLightSourceComponent light)
        {
            var label = $"{GetComponentLabel(light)} | Intensity: {light.Intensity:0.###}";

            if (ImGui.TreeNode(label))
            {
                ImGui.TextUnformatted($"Color: {FormatVector4(light.Color)}");
                ImGui.TextUnformatted($"Direction: {FormatVector3(light.Direction)}");
                ImGui.TextUnformatted($"Enabled: {light.Enabled}");
                ImGui.TreePop();
            }
        }

        private static void DrawNodeTooltip(ManaSceneNode node)
        {
            if (!ImGui.IsItemHovered())
            {
                return;
            }

            ImGui.BeginTooltip();
            ImGui.Text(node.GetType().Name);
            ImGui.Text($"Pos {FormatVector3(node.Position)}");
            ImGui.Text($"Rot {FormatVector3(node.RotationDegrees)}");
            ImGui.Text($"Scale {FormatVector3(node.Scale)}");
            ImGui.Text($"Children {node.Children.Count}");
            ImGui.Text($"Components {node.Components.Count}");
            ImGui.EndTooltip();
        }

        private static string GetComponentLabel(ManaComponent component)
        {
            var label = GetDisplayName(component, component.GetType().Name);
            if (component is MeshRendererComponent meshRenderer)
            {
                label += $" -> {meshRenderer.Mesh.GetType().Name}";
            }

            return label;
        }

        private static string GetDisplayName(ManaObject manaObject, string fallback)
        {
            return string.IsNullOrWhiteSpace(manaObject.Name) ? fallback : manaObject.Name;
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
        }

        private static string FormatVector4(Vector4 value)
        {
            return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}, {value.W:0.###})";
        }
    }
}
