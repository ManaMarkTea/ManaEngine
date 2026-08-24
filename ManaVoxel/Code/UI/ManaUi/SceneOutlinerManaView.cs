using Mana;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The scene outliner rebuilt in ManaUI, over the live <see cref="ManaWorld"/> and sharing the editor's
    /// <see cref="SceneSelectionState"/> with the ImGui outliner, so selecting in either is reflected in
    /// both and in the viewport.
    /// <para>
    /// The tree mixes three unrelated types -- scenes, nodes and components -- which is why TreeView takes
    /// plain objects and a children selector rather than a node type of its own. Nothing here wraps the
    /// scene graph.
    /// </para>
    /// </summary>
    public static class SceneOutlinerManaView
    {
        private static readonly Vector4 SceneIconColor = new(0.72f, 0.72f, 0.78f, 1.0f);
        private static readonly Vector4 NodeIconColor = new(0.55f, 0.68f, 0.85f, 1.0f);
        private static readonly Vector4 MeshRendererIconColor = new(0.40f, 0.78f, 0.45f, 1.0f);
        private static readonly Vector4 LightSourceIconColor = new(0.95f, 0.80f, 0.25f, 1.0f);
        private static readonly Vector4 ComponentIconColor = new(0.60f, 0.60f, 0.65f, 1.0f);

        public static UiElement Build(
            UiTheme theme,
            ManaWorld world,
            SceneSelectionState selection,
            Action<ManaScene>? saveScene = null,
            Func<ManaScene, bool>? sceneHasChanges = null)
        {
            var tree = new TreeView
            {
                ItemsSource = world.Scenes,
                ChildrenSelector = ChildrenOf,

                // Answered without materialising anything. ChildrenOf has to combine a node's children and
                // its components into one list, and the tree asks every visible row whether it has any --
                // so without this, a planet's worth of rows allocates a list each, every rebuild.
                HasChildrenSelector = HasChildren,

                // The scene graph raises nothing when it changes, so the tree watches a cheap summary
                // instead. Counting the top two levels is enough to notice nodes being added or removed
                // without walking the whole graph every frame.
                RowHeight = MathF.Max(theme.ControlHeight, 28.0f),
                ItemTemplate = item => BuildRow(theme, item, saveScene, sceneHasChanges),
                StructureSignature = () => StructureSignature(world)
            };

            tree.BindPolled(t => t.RevealedSelectedItem, () => CurrentSelectionItem(selection));
            tree.SelectionChanged += (_, item) =>
            {
                if (!tree.IsApplyingBinding)
                {
                    ApplySelection(selection, item);
                }
            };

            // A Grid, not a StackPanel: a vertical stack measures its children with unbounded height, so a
            // scroll viewer inside one grows to fit its content and never scrolls. The star row hands the
            // viewer whatever is left over, which is what gives it something to scroll against.
            // No "OUTLINER" caption row: the dock tab immediately above says exactly that, and a panel that
            // repeats its own tab spends a row of height saying nothing. The tool row below carries the
            // scene count, which is the part that was actually worth reading.
            return new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Padding(5.0f).Children(
                new StackPanel().Horizontal(theme.Spacing).GridRow(0).Children(
                    new Button().Label("Collapse all").Variant(ButtonVariant.Subtle)
                        .OnClick(tree.CollapseAll),
                    new TextBlock()
                        .BindTextPolled(() => $"{world.Scenes.Count} scene{(world.Scenes.Count == 1 ? "" : "s")}")
                        .Foreground(theme.TextMuted)
                        .CenterV()),

                tree.Scrollable().Configure(viewer => viewer.MinHeight = 120.0f).GridRow(1));
        }

        private static bool HasChildren(object item)
        {
            return item switch
            {
                ManaScene scene => scene.Children.Count > 0,
                ManaSceneNode node => node.Children.Count > 0,
                _ => false
            };
        }

        private static object? CurrentSelectionItem(SceneSelectionState selection)
        {
            return (object?)selection.SelectedNode ?? selection.SelectedScene;
        }

        /// <summary>
        /// Children of any item in the tree. Components stay in the inspector; the outliner shows them as
        /// compact icons on their owning node instead of spending full hierarchy rows on them.
        /// </summary>
        private static System.Collections.IEnumerable? ChildrenOf(object item)
        {
            switch (item)
            {
                case ManaScene scene:
                    return scene.Children;
                case ManaSceneNode node:
                    return node.Children;
                default:
                    return null;
            }
        }

        private static UiElement BuildRow(
            UiTheme theme,
            object item,
            Action<ManaScene>? saveScene,
            Func<ManaScene, bool>? sceneHasChanges)
        {
            var (label, color, muted, icon) = Describe(item);

            var labelRow = new Grid().Columns("Auto, *, Auto", 6.0f).Rows("*");
            labelRow.Children.Add(new OutlinerIcon(icon, color)
                .CenterV()
                .WithTooltip(IconTooltip(item))
                .GridColumn(0));
            labelRow.Children.Add(new TextBlock()
                    .Text(label)
                    .Foreground(muted ? theme.TextMuted : theme.Text)
                    .Ellipsis()
                    .CenterV()
                    .GridColumn(1));

            if (InlineComponentIcons(item, theme) is { } inlineIcons)
            {
                labelRow.Children.Add(inlineIcons.GridColumn(2));
            }

            if (item is not ManaScene scene || saveScene == null || string.IsNullOrWhiteSpace(scene.LevelPath))
            {
                return labelRow;
            }

            var saveButton = new Button()
                .Label(SaveLabel(scene, sceneHasChanges))
                .Variant(ButtonVariant.Subtle)
                .Height(MathF.Max(20.0f, theme.ControlHeight - 6.0f))
                .Configure(button =>
                {
                    button.Padding = new Thickness(6.0f, 1.0f);
                })
                .WithTooltip("Save this scene to its level JSON.")
                .OnClick(() => saveScene(scene));

            saveButton.BindPolled(button => button.Text, () => SaveLabel(scene, sceneHasChanges));

            return new Grid().Columns("*, Auto", theme.Spacing).Rows("*").Children(
                labelRow.GridColumn(0),
                saveButton.GridColumn(1));
        }

        private static string SaveLabel(ManaScene scene, Func<ManaScene, bool>? sceneHasChanges)
        {
            return sceneHasChanges?.Invoke(scene) == true ? "Save*" : "Save";
        }

        private static (string Label, Vector4 Color, bool Muted, OutlinerIconKind Icon) Describe(object item)
        {
            return item switch
            {
                ManaScene scene => (Fallback(scene.Name, "Scene"), SceneIconColor, false, OutlinerIconKind.Scene),
                ManaSceneNode node => (
                    Fallback(node.Name, node.GetType().Name),
                    NodeColor(node),
                    false,
                    NodeIcon(node)),
                MeshRendererComponent => ("Mesh Renderer", MeshRendererIconColor, true, OutlinerIconKind.MeshRenderer),
                ManaLightSourceComponent => ("Light Source", LightSourceIconColor, true, OutlinerIconKind.Light),
                CameraComponent => ("Camera", ComponentIconColor, true, OutlinerIconKind.Camera),
                ManaComponent component => (component.GetType().Name, ComponentIconColor, true, OutlinerIconKind.Component),
                _ => (item.ToString() ?? "?", ComponentIconColor, true, OutlinerIconKind.Component)
            };
        }

        private static UiElement? InlineComponentIcons(object item, UiTheme theme)
        {
            if (item is not ManaSceneNode { Components.Count: > 0 } node)
            {
                return null;
            }

            const int maxIcons = 6;
            var count = Math.Min(node.Components.Count, maxIcons);
            var strip = new StackPanel().Horizontal(3.0f).CenterV();

            for (var i = 0; i < count; i++)
            {
                var component = node.Components[i];
                strip.Children.Add(new OutlinerIcon(ComponentIcon(component), ComponentColor(component), 10.0f)
                    .CenterV()
                    .WithTooltip(IconTooltip(component)));
            }

            if (node.Components.Count > maxIcons)
            {
                strip.Children.Add(new TextBlock()
                    .Text($"+{node.Components.Count - maxIcons}")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV()
                    .WithTooltip($"{node.Components.Count - maxIcons} more components"));
            }

            return strip;
        }

        private static OutlinerIconKind ComponentIcon(ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent => OutlinerIconKind.MeshRenderer,
                ManaLightSourceComponent => OutlinerIconKind.Light,
                CameraComponent => OutlinerIconKind.Camera,
                _ => OutlinerIconKind.Component
            };
        }

        private static Vector4 ComponentColor(ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent => MeshRendererIconColor,
                ManaLightSourceComponent => LightSourceIconColor,
                CameraComponent => ComponentIconColor,
                _ => ComponentIconColor
            };
        }

        private static Vector4 NodeColor(ManaSceneNode node)
        {
            return node.LevelSource?.Kind switch
            {
                ManaLevelSourceKind.Group => SceneIconColor,
                ManaLevelSourceKind.Shape => new Vector4(0.62f, 0.74f, 0.95f, 1.0f),
                ManaLevelSourceKind.Mesh => MeshRendererIconColor,
                ManaLevelSourceKind.Voxels => new Vector4(0.66f, 0.50f, 0.95f, 1.0f),
                ManaLevelSourceKind.PlayerStart => new Vector4(0.95f, 0.42f, 0.34f, 1.0f),
                _ => NodeIconColor
            };
        }

        private static OutlinerIconKind NodeIcon(ManaSceneNode node)
        {
            return node.LevelSource?.Kind switch
            {
                ManaLevelSourceKind.Group => OutlinerIconKind.Group,
                ManaLevelSourceKind.Shape => OutlinerIconKind.Shape,
                ManaLevelSourceKind.Mesh => OutlinerIconKind.Mesh,
                ManaLevelSourceKind.Voxels => OutlinerIconKind.Voxels,
                ManaLevelSourceKind.PlayerStart => OutlinerIconKind.PlayerStart,
                _ => OutlinerIconKind.Node
            };
        }

        private static string IconTooltip(object item)
        {
            return item switch
            {
                ManaScene scene when string.IsNullOrWhiteSpace(scene.LevelPath) => "Runtime scene",
                ManaScene => "Level scene",
                ManaSceneNode node when node.LevelSource != null => $"{node.LevelSource.Kind} node",
                ManaSceneNode => "Runtime node",
                MeshRendererComponent => "Mesh renderer",
                ManaLightSourceComponent => "Light source",
                CameraComponent => "Camera",
                ManaComponent component => component.GetType().Name,
                _ => item.GetType().Name
            };
        }

        private static string Fallback(string? name, string fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        /// <summary>
        /// Mirrors what the ImGui outliner does on click: a component selects itself and its owner, so
        /// node-level consumers keep working unchanged.
        /// </summary>
        private static void ApplySelection(SceneSelectionState selection, object? item)
        {
            switch (item)
            {
                case ManaScene scene:
                    selection.SelectedScene = scene;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = null;
                    break;
                case ManaSceneNode node:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = node;
                    break;
                case ManaComponent component:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = component;
                    selection.SelectedNode = component.Owner;
                    break;
                default:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = null;
                    break;
            }
        }

        /// <summary>
        /// A cheap stand-in for "has the graph changed". Deliberately shallow: walking the whole tree every
        /// frame would cost more than the rebuild it is trying to avoid, and adding or removing anything
        /// visible at the top two levels is what actually needs to be noticed.
        /// </summary>
        private static int StructureSignature(ManaWorld world)
        {
            var signature = world.Scenes.Count;

            foreach (var scene in world.Scenes)
            {
                signature = signature * 397 + scene.Children.Count;

                foreach (var node in scene.Children)
                {
                    signature = signature * 397 + node.Children.Count + node.Components.Count;
                }
            }

            return signature;
        }

        private enum OutlinerIconKind
        {
            Scene,
            Group,
            Shape,
            Mesh,
            Voxels,
            PlayerStart,
            Node,
            MeshRenderer,
            Light,
            Camera,
            Component
        }

        private sealed class OutlinerIcon : UiElement
        {
            private readonly OutlinerIconKind _kind;
            private readonly Vector4 _color;
            private readonly float _size;

            public OutlinerIcon(OutlinerIconKind kind, Vector4 color, float size = 14.0f)
            {
                _kind = kind;
                _color = color;
                _size = size;
            }

            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(_size, _size);

            protected override void OnRender(ManaUiDrawList drawList)
            {
                var box = Bounds.Deflate(new Thickness(1.0f));
                var soft = _color.WithAlpha(_color.W * 0.32f);

                switch (_kind)
                {
                    case OutlinerIconKind.Scene:
                        DrawScene(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Group:
                        DrawGroup(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Shape:
                        DrawShape(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Mesh:
                    case OutlinerIconKind.MeshRenderer:
                        DrawMesh(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Voxels:
                        DrawVoxels(drawList, box, _color);
                        break;
                    case OutlinerIconKind.PlayerStart:
                        DrawPlayerStart(drawList, box, _color);
                        break;
                    case OutlinerIconKind.Light:
                        DrawLight(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Camera:
                        DrawCamera(drawList, box, _color, soft);
                        break;
                    default:
                        DrawNode(drawList, box, _color, soft);
                        break;
                }
            }
        }

        private static Vector2 P(UiRect box, float x, float y)
        {
            return new Vector2(box.X + box.Width * x, box.Y + box.Height * y);
        }

        private static void DrawScene(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var page = UiRect.FromEdges(box.Left + 2.0f, box.Top + 1.0f, box.Right - 2.0f, box.Bottom - 1.0f);
            drawList.FillRect(page, soft, new CornerRadius(1.0f));
            drawList.DrawBorder(page, color, 1.2f, new CornerRadius(1.0f));
            drawList.DrawLine(P(page, 0.28f, 0.36f), P(page, 0.72f, 0.36f), color, 1.0f);
            drawList.DrawLine(P(page, 0.28f, 0.58f), P(page, 0.64f, 0.58f), color, 1.0f);
        }

        private static void DrawGroup(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var tab = UiRect.FromEdges(box.Left + 1.0f, box.Top + 3.0f, box.Left + box.Width * 0.48f, box.Top + 6.0f);
            var body = UiRect.FromEdges(box.Left + 1.0f, box.Top + 5.0f, box.Right - 1.0f, box.Bottom - 2.0f);
            drawList.FillRect(tab, soft, new CornerRadius(1.0f));
            drawList.FillRect(body, soft, new CornerRadius(1.5f));
            drawList.DrawBorder(body, color, 1.2f, new CornerRadius(1.5f));
        }

        private static void DrawShape(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var front = UiRect.FromEdges(box.Left + 3.0f, box.Top + 5.0f, box.Right - 3.0f, box.Bottom - 2.0f);
            drawList.FillRect(front, soft, new CornerRadius(1.0f));
            drawList.DrawBorder(front, color, 1.1f, new CornerRadius(1.0f));
            drawList.DrawLine(P(box, 0.28f, 0.38f), P(box, 0.5f, 0.16f), color, 1.1f);
            drawList.DrawLine(P(box, 0.72f, 0.38f), P(box, 0.5f, 0.16f), color, 1.1f);
            drawList.DrawLine(P(box, 0.28f, 0.38f), P(box, 0.72f, 0.38f), color, 1.1f);
        }

        private static void DrawMesh(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var a = P(box, 0.50f, 0.12f);
            var b = P(box, 0.86f, 0.50f);
            var c = P(box, 0.50f, 0.88f);
            var d = P(box, 0.14f, 0.50f);
            drawList.FillRect(UiRect.FromEdges(box.Left + 4.0f, box.Top + 4.0f, box.Right - 4.0f, box.Bottom - 4.0f), soft);
            drawList.DrawLine(a, b, color, 1.2f);
            drawList.DrawLine(b, c, color, 1.2f);
            drawList.DrawLine(c, d, color, 1.2f);
            drawList.DrawLine(d, a, color, 1.2f);
        }

        private static void DrawVoxels(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            var s = MathF.Max(2.0f, box.Width * 0.28f);
            drawList.FillRect(new UiRect(box.Left + 1.0f, box.Top + 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + box.Width - s - 1.0f, box.Top + 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + 1.0f, box.Top + box.Height - s - 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + box.Width - s - 1.0f, box.Top + box.Height - s - 1.0f, s, s), color, new CornerRadius(1.0f));
        }

        private static void DrawPlayerStart(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            drawList.DrawLine(P(box, 0.35f, 0.86f), P(box, 0.35f, 0.15f), color, 1.5f);
            drawList.DrawLine(P(box, 0.36f, 0.18f), P(box, 0.78f, 0.32f), color, 1.5f);
            drawList.DrawLine(P(box, 0.78f, 0.32f), P(box, 0.36f, 0.48f), color, 1.5f);
            drawList.FillRect(new UiRect(box.Left + 2.5f, box.Bottom - 2.5f, box.Width - 5.0f, 1.6f), color, new CornerRadius(0.8f));
        }

        private static void DrawLight(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var center = P(box, 0.5f, 0.5f);
            var radius = box.Width * 0.26f;
            var bulb = new UiRect(center.X - radius, center.Y - radius, radius * 2.0f, radius * 2.0f);
            drawList.FillRect(bulb, soft, new CornerRadius(radius));
            drawList.DrawBorder(bulb, color, 1.1f, new CornerRadius(radius));
            drawList.DrawLine(P(box, 0.5f, 0.05f), P(box, 0.5f, 0.20f), color, 1.0f);
            drawList.DrawLine(P(box, 0.5f, 0.80f), P(box, 0.5f, 0.95f), color, 1.0f);
            drawList.DrawLine(P(box, 0.05f, 0.5f), P(box, 0.20f, 0.5f), color, 1.0f);
            drawList.DrawLine(P(box, 0.80f, 0.5f), P(box, 0.95f, 0.5f), color, 1.0f);
        }

        private static void DrawCamera(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var body = UiRect.FromEdges(box.Left + 1.0f, box.Top + 4.0f, box.Right - 4.0f, box.Bottom - 3.0f);
            drawList.FillRect(body, soft, new CornerRadius(1.5f));
            drawList.DrawBorder(body, color, 1.1f, new CornerRadius(1.5f));
            drawList.DrawLine(P(box, 0.72f, 0.42f), P(box, 0.94f, 0.28f), color, 1.2f);
            drawList.DrawLine(P(box, 0.72f, 0.62f), P(box, 0.94f, 0.76f), color, 1.2f);
        }

        private static void DrawNode(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var node = box.Deflate(new Thickness(2.0f));
            drawList.FillRect(node, soft, new CornerRadius(2.0f));
            drawList.DrawBorder(node, color, 1.2f, new CornerRadius(2.0f));
        }
    }
}
