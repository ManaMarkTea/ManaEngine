using Mana;
using Mana.Code.Components;
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

        public static UiElement Build(UiTheme theme, ManaWorld world, SceneSelectionState selection)
        {
            var tree = new TreeView
            {
                ItemsSource = world.Scenes,
                ChildrenSelector = ChildrenOf,

                // Answered without materialising anything. ChildrenOf has to combine a node's children and
                // its components into one list, and the tree asks every visible row whether it has any --
                // so without this, a planet's worth of rows allocates a list each, every rebuild.
                HasChildrenSelector = HasChildren,
                ItemTemplate = item => BuildRow(theme, item),

                // The scene graph raises nothing when it changes, so the tree watches a cheap summary
                // instead. Counting the top two levels is enough to notice nodes being added or removed
                // without walking the whole graph every frame.
                StructureSignature = () => StructureSignature(world)
            };

            tree.SelectionChanged += (_, item) => ApplySelection(selection, item);

            // A Grid, not a StackPanel: a vertical stack measures its children with unbounded height, so a
            // scroll viewer inside one grows to fit its content and never scrolls. The star row hands the
            // viewer whatever is left over, which is what gives it something to scroll against.
            return new Grid().Rows("Auto, Auto, *", theme.Spacing).Columns("*").Padding(8.0f).Children(
                new TextBlock()
                    .Text("OUTLINER")
                    .FontFamily(theme.FontFamilyMedium)
                    .FontSize(theme.FontSizeSmall)
                    .Foreground(theme.TextMuted)
                    .GridRow(0),

                new StackPanel().Horizontal(theme.Spacing).GridRow(1).Children(
                    new Button().Label("Collapse all").Variant(ButtonVariant.Subtle)
                        .OnClick(tree.CollapseAll),
                    new TextBlock()
                        .BindTextPolled(() => $"{world.Scenes.Count} scene{(world.Scenes.Count == 1 ? "" : "s")}")
                        .Foreground(theme.TextMuted)
                        .CenterV()),

                tree.Scrollable().Configure(viewer => viewer.MinHeight = 120.0f).GridRow(2));
        }

        private static bool HasChildren(object item)
        {
            return item switch
            {
                ManaScene scene => scene.Children.Count > 0,
                ManaSceneNode node => node.Children.Count > 0 || node.Components.Count > 0,
                _ => false
            };
        }

        /// <summary>
        /// Children of any item in the tree. A node contributes its child nodes and its components, so a
        /// component appears beneath the node that owns it.
        /// </summary>
        private static System.Collections.IEnumerable? ChildrenOf(object item)
        {
            switch (item)
            {
                case ManaScene scene:
                    return scene.Children;
                case ManaSceneNode node:
                {
                    if (node.Components.Count == 0)
                    {
                        return node.Children;
                    }

                    var combined = new List<object>(node.Children.Count + node.Components.Count);
                    combined.AddRange(node.Children);
                    combined.AddRange(node.Components);
                    return combined;
                }
                default:
                    return null;
            }
        }

        private static UiElement BuildRow(UiTheme theme, object item)
        {
            var (label, color, muted) = Describe(item);

            return new StackPanel().Horizontal(6.0f).Children(
                new Border().Fill(color).Rounded(2.0f).Size(10.0f, 10.0f).CenterV(),
                new TextBlock()
                    .Text(label)
                    .Foreground(muted ? theme.TextMuted : theme.Text)
                    .Ellipsis()
                    .CenterV());
        }

        private static (string Label, Vector4 Color, bool Muted) Describe(object item)
        {
            return item switch
            {
                ManaScene scene => (Fallback(scene.Name, "Scene"), SceneIconColor, false),
                ManaSceneNode node => (Fallback(node.Name, node.GetType().Name), NodeIconColor, false),
                MeshRendererComponent => ("Mesh Renderer", MeshRendererIconColor, true),
                ManaLightSourceComponent => ("Light Source", LightSourceIconColor, true),
                ManaComponent component => (component.GetType().Name, ComponentIconColor, true),
                _ => (item.ToString() ?? "?", ComponentIconColor, true)
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
                case ManaSceneNode node:
                    selection.SelectedComponent = null;
                    selection.SelectedNode = node;
                    break;
                case ManaComponent component:
                    selection.SelectedComponent = component;
                    selection.SelectedNode = component.Owner;
                    break;
                default:
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
    }
}
