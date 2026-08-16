using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Drives the ported scene outliner against a real ManaWorld. Like the palette suite, the value is in
/// exercising the production view over the production model rather than a stand-in.
/// </summary>
public static class OutlinerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var world = new ManaWorld();

        // Scene objects only store their context, so a bare one is enough to build a graph without a
        // window or a graphics device -- which is what lets the real outliner be driven headlessly.
        var context = new ManaContext(null!, null!, world);

        var scene = new ManaScene(context) { Name = "Default Scene" };
        var parent = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Parent" };
        var childA = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child A" };
        var childB = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child B" };

        parent.Children.Add(childA);
        parent.Children.Add(childB);
        scene.Children.Add(parent);
        world.Scenes.Add(scene);

        UiElement? view = null;
        try
        {
            view = SceneOutlinerManaView.Build(root.Theme, world, world.Selection);
            Check("outliner constructs over a real world", true);
        }
        catch (Exception ex)
        {
            Check("outliner constructs over a real world", false, ex.Message);
            return;
        }

        root.Content = view;
        root.UpdateLayout(new Vector2(360, 640));

        var tree = FindFirst<TreeView>(root);
        Check("outliner contains a tree", tree != null);
        Check("scenes are the roots", tree!.VisibleItems.Count == 1, $"{tree.VisibleItems.Count}");
        Check("root is the scene", ReferenceEquals(tree.VisibleItems[0], scene));

        Check("the panel names itself",
            FindAll<TextBlock>(root).Any(t => t.Text == "OUTLINER"),
            string.Join(", ", FindAll<TextBlock>(root).Select(t => t.Text).Take(5)));

        var drawn = root.Render();
        Check("outliner renders", drawn.InstanceCount > 5, $"{drawn.InstanceCount} instances");

        // ---------------------------------------------------------------- hierarchy
        tree.Expand(scene);
        root.UpdateLayout(new Vector2(360, 640));
        Check("expanding a scene shows its nodes",
            tree.VisibleItems.Contains(parent), $"{tree.VisibleItems.Count} rows");

        tree.Expand(parent);
        root.UpdateLayout(new Vector2(360, 640));
        Check("expanding a node shows its children",
            tree.VisibleItems.Contains(childA) && tree.VisibleItems.Contains(childB),
            $"{tree.VisibleItems.Count} rows");

        // ---------------------------------------------------------------- selection shared with the editor
        tree.SelectedItem = childB;
        Check("selecting a node updates the shared selection",
            ReferenceEquals(world.Selection.SelectedNode, childB),
            world.Selection.SelectedNode?.Name ?? "null");
        Check("selecting a node clears any component selection",
            world.Selection.SelectedComponent == null);

        // A component selects itself and its owner, matching what the ImGui outliner does.
        var withComponent = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Lit" };
        var component = new ManaLightSourceComponent(withComponent);
        withComponent.Components.Add(component);
        scene.Children.Add(withComponent);

        root.UpdateLayout(new Vector2(360, 640));
        tree.Expand(withComponent);
        root.UpdateLayout(new Vector2(360, 640));

        Check("components appear beneath their node",
            tree.VisibleItems.Any(i => ReferenceEquals(i, component)),
            string.Join(", ", tree.VisibleItems.Select(DescribeItem)));

        tree.SelectedItem = component;
        Check("selecting a component selects it", ReferenceEquals(world.Selection.SelectedComponent, component));
        Check("selecting a component also selects its owner",
            ReferenceEquals(world.Selection.SelectedNode, withComponent),
            world.Selection.SelectedNode?.Name ?? "null");

        // ---------------------------------------------------------------- live graph
        // The scene graph announces nothing, so the tree watches a cheap signature instead.
        var before = tree.VisibleItems.Count;
        var added = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Added Later" };
        scene.Children.Add(added);

        root.UpdateLayout(new Vector2(360, 640));
        Check("a node added at runtime appears", tree.VisibleItems.Count > before,
            $"{before} -> {tree.VisibleItems.Count}");
        Check("the new node is the one added",
            tree.VisibleItems.Any(i => ReferenceEquals(i, added)));

        scene.Children.Remove(added);
        root.UpdateLayout(new Vector2(360, 640));
        Check("a removed node disappears",
            !tree.VisibleItems.Any(i => ReferenceEquals(i, added)),
            $"{tree.VisibleItems.Count} rows");

        // Expansion survives a rebuild, so a graph change does not collapse what the user opened.
        Check("expansion survives a rebuild", tree.IsExpanded(parent) && tree.IsExpanded(scene),
            "still expanded");

        root.UpdateLayout(new Vector2(360, 640));
        var after = root.Render();
        Check("outliner still renders after edits", after.InstanceCount > 5, $"{after.InstanceCount}");
        Check("still one draw call", after.Batches.Count == 1, $"{after.Batches.Count} batches");
    }

    private static string DescribeItem(object item)
    {
        return item switch
        {
            ManaScene scene => $"Scene({scene.Name})",
            ManaSceneNode node => $"Node({node.Name})",
            _ => item.GetType().Name
        };
    }

    private static T? FindFirst<T>(UiElement element) where T : UiElement
    {
        if (element is T typed)
        {
            return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            var found = FindFirst<T>(element.GetVisualChild(i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        Collect(element, found);
        return found;
    }

    private static void Collect<T>(UiElement element, List<T> found) where T : UiElement
    {
        if (element is T typed)
        {
            found.Add(typed);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Collect(element.GetVisualChild(i), found);
        }
    }
}
