using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;
using ManaEditor.Code.UI.ManaUi;

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

        ManaScene? savedScene = null;
        var sceneDirty = false;
        var history = new ManaEditor.Code.UI.EditorUndoHistory();
        var scene = new ManaScene(context) { Name = "Default Scene", LevelPath = "C:/levels/default.manalevel" };
        var parent = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Parent" };
        var childA = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child A" };
        var childB = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child B" };
        var transient = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Transform Gizmo (Transient)",
            IsEditorTransient = true
        };

        parent.Children.Add(childA);
        parent.Children.Add(childB);
        parent.Children.Add(transient);
        scene.Children.Add(parent);
        world.Scenes.Add(scene);

        UiElement? view = null;
        try
        {
            view = SceneOutlinerManaView.Build(
                root.Theme,
                world,
                world.Selection,
                sceneToSave =>
                {
                    savedScene = sceneToSave;
                    sceneDirty = false;
                },
                sceneToCheck => sceneDirty && ReferenceEquals(sceneToCheck, scene), history);
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

        // Inverted from "the panel names itself": it used to draw an OUTLINER caption directly beneath a dock
        // tab already reading Outliner, which spent a row of panel height repeating itself. The scene count
        // below is the part that was worth the space, so that is what is checked instead.
        Check("the panel does not repeat its own tab title",
            !FindAll<TextBlock>(root).Any(t => t.Text == "OUTLINER"),
            string.Join(", ", FindAll<TextBlock>(root).Select(t => t.Text).Take(5)));

        Check("and still reports the scene count",
            FindAll<TextBlock>(root).Any(t => t.Text.Contains("scene", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", FindAll<TextBlock>(root).Select(t => t.Text).Take(5)));

        var drawn = root.Render();
        Check("outliner renders", drawn.InstanceCount > 5, $"{drawn.InstanceCount} instances");

        var saveButton = FindAll<Button>(root).FirstOrDefault(button => button.Text == "Save");
        Check("a level-backed scene offers a save button", saveButton != null);

        sceneDirty = true;
        root.UpdateLayout(new Vector2(360, 640));
        Check("a dirty level-backed scene marks its save button",
            FindAll<Button>(root).Any(button => button.Text == "Save*"));

        world.Selection.SelectedNode = parent;
        world.Selection.SelectedScene = null;
        root.UpdateLayout(new Vector2(360, 640));
        tree.Focus();
        root.Input.InjectKeyDown(UiKey.F2);
        var renameBox = root.FocusManager.Focused as TextBox;
        Check("F2 edits the selected node inline with its name selected",
            renameBox?.SelectedText == "Parent");
        if (renameBox != null)
        {
            renameBox.Text = "Renamed parent";
            root.Input.InjectKeyDown(UiKey.Enter);
            root.UpdateLayout(new Vector2(360, 640));
            Check("Enter commits rename and restores tree focus",
                parent.Name == "Renamed parent" && ReferenceEquals(root.FocusManager.Focused, tree));
            Check("the renamed label updates", FindAll<TextBlock>(tree).Any(t => t.Text == "Renamed parent"));
            history.Undo();
            Check("rename can be undone", parent.Name == "Parent");
            history.Redo();
            Check("rename can be redone", parent.Name == "Renamed parent");
            root.UpdateLayout(new Vector2(360, 640));
            root.Input.InjectKeyDown(UiKey.F2);
            if (root.FocusManager.Focused is TextBox cancelBox) cancelBox.Text = "Discard me";
            root.Input.InjectKeyDown(UiKey.Escape);
            Check("Escape cancels rename", parent.Name == "Renamed parent");
            root.Input.InjectKeyDown(UiKey.F2);
            if (root.FocusManager.Focused is TextBox blurBox) blurBox.Text = "Parent";
            tree.Focus();
            Check("leaving the field commits rename", parent.Name == "Parent");
            root.UpdateLayout(new Vector2(360, 640));
        }
        saveButton = FindAll<Button>(root).FirstOrDefault(button => button.Text == "Save*");

        if (saveButton != null)
        {
            root.Input.InjectPointerPressed(saveButton.Bounds.Center, PointerButton.Left, 1.0);
            root.Input.InjectPointerReleased(saveButton.Bounds.Center, PointerButton.Left);
        }

        Check("the scene save button reports its scene", ReferenceEquals(savedScene, scene));
        Check("saving from the outliner leaves node selection alone",
            ReferenceEquals(world.Selection.SelectedNode, parent),
            world.Selection.SelectedNode?.Name ?? "null");
        root.UpdateLayout(new Vector2(360, 640));
        Check("saving clears the dirty marker",
            FindAll<Button>(root).Any(button => button.Text == "Save"));

        // ---------------------------------------------------------------- hierarchy
        tree.Expand(scene);
        root.UpdateLayout(new Vector2(360, 640));
        Check("expanding a scene shows its nodes",
            tree.VisibleItems.Contains(parent), $"{tree.VisibleItems.Count} rows");

        tree.Expand(parent);
        root.UpdateLayout(new Vector2(360, 640));
        Check("editor transient nodes stay out of the hierarchy",
            !tree.VisibleItems.Contains(transient),
            string.Join(", ", tree.VisibleItems.Select(item => item.ToString())));
        Check("expanding a node shows its children",
            tree.VisibleItems.Contains(childA) && tree.VisibleItems.Contains(childB),
            $"{tree.VisibleItems.Count} rows");

        // ---------------------------------------------------------------- selection shared with the editor
        world.Selection.SelectedNode = null;
        world.Selection.SelectedScene = null;
        world.Selection.SelectedComponent = null;
        root.UpdateLayout(new Vector2(360, 640));

        var childBRow = tree.Children[3];
        root.Input.InjectPointerMoved(childBRow.Bounds.Center);
        root.Input.InjectPointerPressed(childBRow.Bounds.Center, PointerButton.Left, 2.0);
        root.Input.InjectPointerReleased(childBRow.Bounds.Center, PointerButton.Left);

        Check("clicking an outliner row selects its node",
            ReferenceEquals(world.Selection.SelectedNode, childB),
            world.Selection.SelectedNode?.Name ?? "null");
        Check("clicking an outliner row keeps the shared selection populated",
            world.Selection.SelectedNode != null || world.Selection.SelectedScene != null,
            DescribeItem(world.Selection.SelectedNode ?? (object?)world.Selection.SelectedScene ?? "null"));

        tree.SelectedItem = scene;
        Check("selecting a scene updates the shared scene selection",
            ReferenceEquals(world.Selection.SelectedScene, scene),
            world.Selection.SelectedScene?.Name ?? "null");
        Check("selecting a scene clears node selection",
            world.Selection.SelectedNode == null && world.Selection.SelectedComponent == null);

        tree.SelectedItem = childB;
        Check("selecting a node updates the shared selection",
            ReferenceEquals(world.Selection.SelectedNode, childB),
            world.Selection.SelectedNode?.Name ?? "null");
        Check("selecting a node clears any component selection",
            world.Selection.SelectedComponent == null);
        Check("selecting a node clears scene selection",
            world.Selection.SelectedScene == null);

        // Components are visible as compact icons on their owning node. The inspector owns component
        // editing, so they no longer spend full rows in the outliner hierarchy.
        var withComponent = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Lit" };
        var component = new ManaLightSourceComponent(withComponent);
        withComponent.Components.Add(component);
        scene.Children.Add(withComponent);

        root.UpdateLayout(new Vector2(360, 640));
        tree.Expand(withComponent);
        root.UpdateLayout(new Vector2(360, 640));

        Check("components do not appear as hierarchy rows",
            !tree.VisibleItems.Any(i => ReferenceEquals(i, component)),
            string.Join(", ", tree.VisibleItems.Select(DescribeItem)));
        Check("component presence is shown as an inline icon",
            FindAll<UiElement>(root).Any(element => element.Tooltip == "Light source"));

        var lightIcon = FindAll<UiElement>(root).FirstOrDefault(element =>
            element.Tooltip == "Light source" && !element.Bounds.IsEmpty);
        if (lightIcon != null)
        {
            world.Selection.SelectedNode = null;
            world.Selection.SelectedScene = null;
            world.Selection.SelectedComponent = null;
            root.Input.InjectPointerMoved(lightIcon.Bounds.Center);
            root.Input.InjectPointerPressed(lightIcon.Bounds.Center, PointerButton.Left, 3.0);
            root.Input.InjectPointerReleased(lightIcon.Bounds.Center, PointerButton.Left);
        }

        Check("clicking a component icon selects its owner node",
            ReferenceEquals(world.Selection.SelectedNode, withComponent),
            world.Selection.SelectedNode?.Name ?? "null");

        world.Selection.SelectedComponent = component;
        world.Selection.SelectedNode = withComponent;
        root.UpdateLayout(new Vector2(360, 640));
        Check("an external component selection reveals its owner node",
            ReferenceEquals(tree.SelectedItem, withComponent),
            DescribeItem(tree.SelectedItem ?? "null"));
        Check("external component selection clears scene selection",
            world.Selection.SelectedScene == null);

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

        // ---------------------------------------------------------------- dragging rows to re-parent
        // A tree of its own, linked through AddChild (the one above is built without parents).
        var dragRoot = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Drag Root" };
        var dragA = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Drag A" };
        var dragB = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Drag B" };
        dragRoot.AddChild(dragA);
        dragRoot.AddChild(dragB);
        scene.Children.Add(dragRoot);
        root.UpdateLayout(new Vector2(360, 640));
        tree.Expand(dragRoot);
        root.UpdateLayout(new Vector2(360, 640));

        UiRect RowOf(string name)
        {
            for (UiElement? element = FindAll<TextBlock>(root).First(text => text.Text == name); element != null; element = element.Parent)
            {
                if (element.GetType().Name == "OutlinerActionRow")
                {
                    return element.Bounds;
                }
            }
            return UiRect.Empty;
        }

        // Pressing selects the row, which rebuilds the tree: the layout runs between events, as it does a frame apart.
        void Drag(string name, Func<Vector2> to)
        {
            var from = RowOf(name).Center;
            root.Input.InjectPointerMoved(from);
            root.Input.InjectPointerPressed(from, PointerButton.Left, 10.0);
            root.UpdateLayout(new Vector2(360, 640));
            root.Input.InjectPointerMoved(from + new Vector2(0.0f, 8.0f));
            root.UpdateLayout(new Vector2(360, 640));
            var target = to();
            root.Input.InjectPointerMoved(target);
            root.Input.InjectPointerReleased(target, PointerButton.Left);
            root.UpdateLayout(new Vector2(360, 640));
        }

        Drag("Drag B", () => RowOf("Drag A").Center);
        Check("dropping a row on the middle of another puts it inside", dragB.Parent == dragA, dragB.Parent?.Name ?? "top");

        tree.Expand(dragA);
        root.UpdateLayout(new Vector2(360, 640));
        Drag("Drag B", () => new Vector2(RowOf("Drag Root").Center.X, RowOf("Drag Root").Y + 2.0f));
        Check("dropping on a row's top edge puts it before, at that level",
            dragB.Parent == null && scene.Children.IndexOf(dragB) >= 0 && scene.Children.IndexOf(dragB) == scene.Children.IndexOf(dragRoot) - 1,
            string.Join(", ", scene.Children.Select(node => node.Name)));

        Drag("Drag Root", () => RowOf("Drag A").Center);
        Check("a node cannot be dropped into its own child", dragRoot.Parent == null && dragA.Parent == dragRoot);
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
