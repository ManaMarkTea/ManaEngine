using ImGuiNET;
using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    public class SceneOutlinerPanelView
    {
        private readonly SceneSelectionState selectionState;

        public SceneOutlinerPanelView(SceneSelectionState selectionState)
        {
            this.selectionState = selectionState;
        }

        public void Draw(ManaWorld world)
        {
            var dockId = BaseGameMode.OutlinerDockId != 0
                ? BaseGameMode.OutlinerDockId
                : BaseGameMode.UtilityDockId;

            if (dockId != 0)
            {
                ImGui.SetNextWindowDockID(dockId, ImGuiCond.Always);
            }

            var outlinerFont = world.Context.GameMode.BarlowCondensedImGuiFont;
            var useOutlinerFont = !outlinerFont.Equals(default(ImFontPtr));
            if (useOutlinerFont)
            {
                ImGui.PushFont(outlinerFont);
            }

            var visible = ImGui.Begin("Outliner");
            try
            {
                if (!visible)
                {
                    return;
                }

                ImGui.Text($"Scenes: {world.Scenes.Count}");
                ImGui.SeparatorText("Camera");
                DrawNode(world.CameraNode);

                ImGui.SeparatorText("Scenes");
                for (int i = 0; i < world.Scenes.Count; ++i)
                {
                    DrawScene(world.Scenes[i], i);
                }
            }
            finally
            {
                ImGui.End();
                if (useOutlinerFont)
                {
                    ImGui.PopFont();
                }
            }
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
            var label = GetDisplayName(node, node.GetType().Name);
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
            if (selectionState.SelectedNode == node)
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            if (!hasContents)
            {
                flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            }

            var open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                selectionState.SelectedNode = node;
            }

            DrawNodeTooltip(node);

            if (hasContents && open)
            {
                DrawComponents(node);

                foreach (var child in node.Children)
                {
                    DrawNode(child);
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        private void DrawComponents(ManaSceneNode node)
        {
            for (var i = 0; i < node.Components.Count; i++)
            {
                var component = node.Components[i];
                ImGui.PushID($"component_{i}_{component.GetHashCode()}");
                DrawComponent(component);
                ImGui.PopID();
            }
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

            ImGui.BulletText(GetDisplayName(component, component.GetType().Name));
        }

        private void DrawMeshRendererComponent(MeshRendererComponent meshRenderer)
        {
            var material = meshRenderer.Material;
            var label = GetDisplayName(meshRenderer, meshRenderer.GetType().Name);

            if (!ImGui.TreeNode(label))
            {
                return;
            }

            var detailIndent = ImGui.GetTreeNodeToLabelSpacing();
            ImGui.Indent(detailIndent);
            ImGui.TextUnformatted($"Mesh: {meshRenderer.Mesh.GetType().Name}");
            ImGui.TextUnformatted($"Material: {GetDisplayName(material, "Default Material")}");
            ImGui.TextUnformatted($"Shader: {material.Shader?.GetType().Name ?? "None"}");

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

            ImGui.Unindent(detailIndent);
            ImGui.TreePop();
        }

        private void DrawLightSourceComponent(ManaLightSourceComponent light)
        {
            var label = GetDisplayName(light, light.GetType().Name);
            if (!ImGui.TreeNode(label))
            {
                return;
            }

            var detailIndent = ImGui.GetTreeNodeToLabelSpacing();
            ImGui.Indent(detailIndent);
            ImGui.TextUnformatted($"Intensity: {light.Intensity:0.###}");
            ImGui.TextUnformatted($"Color: {FormatVector4(light.Color)}");
            ImGui.TextUnformatted($"Direction: {FormatVector3(light.Direction)}");
            ImGui.TextUnformatted($"Enabled: {light.Enabled}");
            ImGui.Unindent(detailIndent);
            ImGui.TreePop();
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
