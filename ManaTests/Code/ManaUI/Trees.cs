using ManaUI;

namespace ManaTests;

/// <summary>A model item deliberately overriding Equals, to prove expansion tracks identity not equality.</summary>
public sealed class TreeItem
{
    public TreeItem(string name, params TreeItem[] children)
    {
        Name = name;
        Children = children.ToList();
    }

    public string Name { get; }
    public List<TreeItem> Children { get; }

    public override bool Equals(object? obj) => obj is TreeItem other && other.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();
}

public static class TreeChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        var leafA = new TreeItem("Mesh");
        var leafB = new TreeItem("Light");
        var child = new TreeItem("Group", leafA, leafB);
        var sibling = new TreeItem("Camera");
        var scene = new TreeItem("Scene", child, sibling);
        var roots = new List<TreeItem> { scene };

        var tree = new TreeView()
            .Tree(roots, item => item.Children, item => new TextBlock().Text(item.Name))
            .Scrollable()
            .Size(320.0f, 400.0f);

        root.Content = tree;
        root.UpdateLayout(new Vector2(400, 500));

        var view = (TreeView)tree.Child!;

        Check("only roots visible when collapsed", view.VisibleItems.Count == 1,
            $"{view.VisibleItems.Count}");
        Check("root row laid out", view.Children.Count == 1, $"{view.Children.Count}");

        // ---------------------------------------------------------------- expanding
        view.Expand(scene);
        root.UpdateLayout(new Vector2(400, 500));
        Check("expanding shows children", view.VisibleItems.Count == 3,
            string.Join(", ", view.VisibleItems.Cast<TreeItem>().Select(i => i.Name)));
        Check("grandchildren stay hidden",
            !view.VisibleItems.Cast<TreeItem>().Any(i => i.Name == "Mesh"), "Group not expanded");

        view.Expand(child);
        root.UpdateLayout(new Vector2(400, 500));
        Check("nested expand shows grandchildren", view.VisibleItems.Count == 5,
            string.Join(", ", view.VisibleItems.Cast<TreeItem>().Select(i => i.Name)));
        Check("depth-first order",
            view.VisibleItems.Cast<TreeItem>().Select(i => i.Name)
                .SequenceEqual(new[] { "Scene", "Group", "Mesh", "Light", "Camera" }),
            string.Join(", ", view.VisibleItems.Cast<TreeItem>().Select(i => i.Name)));

        // Rows indent with depth.
        var rows = view.Children.ToList();
        Check("children indent past their parent",
            rows[1].Bounds.X <= rows[2].Bounds.X && rows[0].Bounds.X <= rows[1].Bounds.X,
            "indent increases with depth");

        // Collapsing removes the rows rather than hiding them, which is what keeps deep trees cheap.
        view.Collapse(child);
        root.UpdateLayout(new Vector2(400, 500));
        Check("collapsing removes rows", view.Children.Count == 3, $"{view.Children.Count}");

        // ---------------------------------------------------------------- identity, not equality
        var twinA = new TreeItem("Duplicate");
        var twinB = new TreeItem("Duplicate");
        var twinParent = new TreeItem("Twins", twinA, twinB);

        // Equal by value, distinct references -- expanding one must not expand the other.
        Check("model items compare equal by value", twinA.Equals(twinB), "Equals overridden");

        var twinRoots = new List<TreeItem> { twinParent };
        var twinView = new TreeView()
            .Tree(twinRoots, i => i.Children, i => new TextBlock().Text(i.Name));

        root.Content = null;
        root.Content = twinView;
        root.UpdateLayout(new Vector2(400, 500));

        twinView.Expand(twinParent);
        twinView.Expand(twinA);
        Check("expansion tracks reference identity",
            twinView.IsExpanded(twinA) && !twinView.IsExpanded(twinB),
            "only the expanded instance is expanded");

        // ---------------------------------------------------------------- selection
        root.Content = null;
        root.Content = tree;
        view.Expand(scene);
        view.Expand(child);
        root.UpdateLayout(new Vector2(400, 500));

        object? selected = null;
        view.SelectionChanged += (_, item) => selected = item;

        var meshRow = view.Children[2];
        root.Input.InjectPointerMoved(meshRow.Bounds.Center);
        root.Input.InjectPointerPressed(meshRow.Bounds.Center, PointerButton.Left, time);
        root.Input.InjectPointerReleased(meshRow.Bounds.Center, PointerButton.Left);

        Check("clicking a row selects it", ReferenceEquals(view.SelectedItem, leafA),
            (view.SelectedItem as TreeItem)?.Name ?? "null");
        Check("selection raises the event", ReferenceEquals(selected, leafA));

        // Clicking the expander toggles instead of selecting.
        var groupRow = view.Children[1];
        var expanderPoint = new Vector2(groupRow.Bounds.X + 20.0f, groupRow.Bounds.Center.Y);
        root.Input.InjectPointerMoved(expanderPoint);
        root.Input.InjectPointerPressed(expanderPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(expanderPoint, PointerButton.Left);
        root.UpdateLayout(new Vector2(400, 500));

        Check("clicking the expander collapses", !view.IsExpanded(child), "Group collapsed");
        Check("expander click does not change selection", ReferenceEquals(view.SelectedItem, leafA),
            (view.SelectedItem as TreeItem)?.Name ?? "null");

        // ---------------------------------------------------------------- keyboard
        view.Expand(child);
        root.UpdateLayout(new Vector2(400, 500));
        view.SelectedItem = scene;
        view.Focus();

        root.Input.InjectKeyDown(UiKey.Down);
        Check("down moves to the next visible row", ReferenceEquals(view.SelectedItem, child),
            (view.SelectedItem as TreeItem)?.Name ?? "null");

        root.Input.InjectKeyDown(UiKey.Up);
        Check("up moves back", ReferenceEquals(view.SelectedItem, scene),
            (view.SelectedItem as TreeItem)?.Name ?? "null");

        view.SelectedItem = child;
        root.Input.InjectKeyDown(UiKey.Left);
        root.UpdateLayout(new Vector2(400, 500));
        Check("left collapses an expanded row", !view.IsExpanded(child), "collapsed");

        root.Input.InjectKeyDown(UiKey.Left);
        Check("left again selects the parent", ReferenceEquals(view.SelectedItem, scene),
            (view.SelectedItem as TreeItem)?.Name ?? "null");

        view.SelectedItem = child;
        root.Input.InjectKeyDown(UiKey.Right);
        root.UpdateLayout(new Vector2(400, 500));
        Check("right expands a collapsed row", view.IsExpanded(child), "expanded");

        root.Input.InjectKeyDown(UiKey.Right);
        Check("right again steps into the first child", ReferenceEquals(view.SelectedItem, leafA),
            (view.SelectedItem as TreeItem)?.Name ?? "null");

        object? activated = null;
        view.ItemActivated += (_, item) => activated = item;
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("enter activates the selection", ReferenceEquals(activated, leafA));

        // ---------------------------------------------------------------- ExpandTo
        view.CollapseAll();
        root.UpdateLayout(new Vector2(400, 500));
        Check("collapse all leaves only roots", view.VisibleItems.Count == 1, $"{view.VisibleItems.Count}");

        view.ExpandTo(leafB);
        root.UpdateLayout(new Vector2(400, 500));
        Check("ExpandTo reveals a deep item",
            view.VisibleItems.Any(i => ReferenceEquals(i, leafB)),
            string.Join(", ", view.VisibleItems.Cast<TreeItem>().Select(i => i.Name)));
        Check("ExpandTo does not expand the item itself", !view.IsExpanded(leafB), "leaf stays collapsed");

        // ---------------------------------------------------------------- live model
        // A scene graph announces nothing, so the tree rebuilds off a cheap signature instead.
        var counter = 0;
        view.StructureSignature = () => counter;
        root.UpdateLayout(new Vector2(400, 500));
        var before = view.VisibleItems.Count;

        scene.Children.Add(new TreeItem("Added"));
        root.UpdateLayout(new Vector2(400, 500));
        Check("no rebuild while the signature is unchanged", view.VisibleItems.Count == before,
            $"{view.VisibleItems.Count}");

        counter++;
        root.UpdateLayout(new Vector2(400, 500));
        Check("signature change rebuilds the tree", view.VisibleItems.Count == before + 1,
            $"{view.VisibleItems.Count}");

        // ---------------------------------------------------------------- rendering and culling
        var deep = new TreeItem("Big", Enumerable.Range(0, 200)
            .Select(i => new TreeItem($"Node {i}")).ToArray());

        var bigView = new TreeView()
            .Tree(new List<TreeItem> { deep }, i => i.Children, i => new TextBlock().Text(i.Name));

        var scroller = bigView.Scrollable().Size(320.0f, 200.0f);
        root.Content = null;
        root.Content = scroller;
        bigView.Expand(deep);
        root.UpdateLayout(new Vector2(400, 500));

        Check("large tree builds every expanded row", bigView.Children.Count == 201,
            $"{bigView.Children.Count}");

        var drawn = root.Render();
        Check("offscreen rows culled", drawn.InstanceCount < 60, $"{drawn.InstanceCount} instances");
        Check("still one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");
    }
}
