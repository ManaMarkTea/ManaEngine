using ImGuiNET;
using Mana;
using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    public class SceneOutlinerPanelView
    {
        private readonly SceneSelectionState selectionState;
        private ManaSceneNode? _lastRevealedSelection;
        private ManaComponent? _lastRevealedSelectionComponent;
        private HashSet<ManaSceneNode>? _pendingRevealAncestors;

        public SceneOutlinerPanelView(SceneSelectionState selectionState)
        {
            this.selectionState = selectionState;
        }

        // Small colored swatches distinguishing row kinds at a glance -- plain filled squares rather
        // than a text/emoji glyph, since the Outliner's own font (BarlowCondensedImGuiFont) doesn't have
        // an icon font merged in, so a glyph icon would likely render as a missing-character box.
        private static readonly Vector4 NodeIconColor = new(0.55f, 0.68f, 0.85f, 1.0f);
        private static readonly Vector4 MeshRendererIconColor = new(0.40f, 0.78f, 0.45f, 1.0f);
        private static readonly Vector4 LightSourceIconColor = new(0.95f, 0.80f, 0.25f, 1.0f);
        private static readonly Vector4 GenericComponentIconColor = new(0.60f, 0.60f, 0.65f, 1.0f);
        private static readonly Vector2 IconSize = new(10.0f, 10.0f);

        private static void DrawTypeIcon(Vector4 color)
        {
            ImGui.ColorButton(
                "##icon",
                color,
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoBorder | ImGuiColorEditFlags.NoAlpha,
                IconSize);
            ImGui.SameLine(0.0f, 6.0f);
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

                if (selectionState.SelectedNode != _lastRevealedSelection || selectionState.SelectedComponent != _lastRevealedSelectionComponent)
                {
                    _pendingRevealAncestors = BuildAncestorSet(selectionState.SelectedNode);
                    _lastRevealedSelection = selectionState.SelectedNode;
                    _lastRevealedSelectionComponent = selectionState.SelectedComponent;
                }
                else
                {
                    _pendingRevealAncestors = null;
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
            var flags = ImGuiTreeNodeFlags.DefaultOpen;
            if (selectionState.SelectedScene == scene)
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            var open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                selectionState.SelectedScene = scene;
                selectionState.SelectedNode = null;
                selectionState.SelectedComponent = null;
            }

            if (open)
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
            var isSelected = selectionState.SelectedNode == node;
            if (isSelected)
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            if (!hasContents)
            {
                flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            }
            else if (_pendingRevealAncestors != null && _pendingRevealAncestors.Contains(node))
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }

            DrawTypeIcon(NodeIconColor);
            var open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                selectionState.SelectedScene = null;
                selectionState.SelectedNode = node;
                selectionState.SelectedComponent = null;
            }

            if (DrawNodeContextMenu(node))
            {
                if (hasContents && open)
                {
                    ImGui.TreePop();
                }

                ImGui.PopID();
                return;
            }

            if (isSelected && _pendingRevealAncestors != null)
            {
                ImGui.SetScrollHereY();
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

        private bool DrawNodeContextMenu(ManaSceneNode node)
        {
            if (!ImGui.BeginPopupContextItem("node_context"))
            {
                return false;
            }

            try
            {
                if (ImGui.MenuItem("Add Light Source"))
                {
                    var light = new ManaLightSourceComponent(node);
                    lock (node.Context.World.SceneLock)
                    {
                        node.Components.Add(light);
                    }

                    selectionState.SelectedScene = null;
                    selectionState.SelectedNode = node;
                    selectionState.SelectedComponent = light;
                    selectionState.NotifySceneGraphChanged();
                }

                var canAddPickupMotion = !node.Components.OfType<ManaPickupMotionComponent>().Any();
                if (ImGui.MenuItem("Add Pickup Motion", string.Empty, false, canAddPickupMotion))
                {
                    var pickupMotion = new ManaPickupMotionComponent(node);
                    lock (node.Context.World.SceneLock)
                    {
                        node.Components.Add(pickupMotion);
                    }

                    selectionState.SelectedScene = null;
                    selectionState.SelectedNode = node;
                    selectionState.SelectedComponent = pickupMotion;
                    selectionState.NotifySceneGraphChanged();
                }

                ImGui.Separator();
                var canDelete = SceneEditorOperations.CanDeleteNode(node.Context.World, node);
                if (ImGui.MenuItem("Delete Node", string.Empty, false, canDelete))
                {
                    return SceneEditorOperations.DeleteNode(node.Context.World, selectionState, node);
                }

                return false;
            }
            finally
            {
                ImGui.EndPopup();
            }
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

            DrawTypeIcon(GenericComponentIconColor);
            ImGui.BulletText(GetDisplayName(component, component.GetType().Name));
        }

        private void DrawMeshRendererComponent(MeshRendererComponent meshRenderer)
        {
            var material = meshRenderer.Material;
            var label = GetDisplayName(meshRenderer, meshRenderer.GetType().Name);
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
            if (selectionState.SelectedComponent == meshRenderer)
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            DrawTypeIcon(MeshRendererIconColor);
            var open = ImGui.TreeNodeEx(label, flags);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                selectionState.SelectedScene = null;
                selectionState.SelectedComponent = meshRenderer;
                selectionState.SelectedNode = meshRenderer.Owner;
            }

            if (selectionState.SelectedComponent == meshRenderer && _pendingRevealAncestors != null)
            {
                ImGui.SetScrollHereY();
            }

            if (!open)
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
            DrawTypeIcon(LightSourceIconColor);
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

        private static HashSet<ManaSceneNode> BuildAncestorSet(ManaSceneNode? node)
        {
            var ancestors = new HashSet<ManaSceneNode>();
            var current = node?.Parent;
            while (current != null)
            {
                ancestors.Add(current);
                current = current.Parent;
            }

            return ancestors;
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
