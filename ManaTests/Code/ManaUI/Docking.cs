using ManaUI;

namespace ManaTests;

public static class DockingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var host = new DockHost();
        root.Content = host;

        static DockItem Item(string title) =>
            new(title, new Border().Fill(UiColor.White)) { Id = title };

        // ---------------------------------------------------------------- building a layout
        var scene = Item("Scene");
        var group = host.Dock(scene, DockSide.Center);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("first dock creates a group", host.RootNode is DockTabGroup, host.RootNode?.GetType().Name ?? "null");
        Check("group fills the host", group.Bounds.Size == new Vector2(1000, 600), group.Bounds.ToString());
        Check("item content is hosted", scene.Content.Bounds.Height > 0, scene.Content.Bounds.ToString());
        Check("item knows its owner", ReferenceEquals(scene.Owner, group));

        // Center docking tabs alongside.
        var outliner = Item("Outliner");
        host.Dock(outliner, DockSide.Center, group);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("center dock adds a tab", group.Items.Count == 2, $"{group.Items.Count}");
        Check("new tab is selected", group.SelectedItem == outliner, group.SelectedItem?.Title ?? "null");
        Check("tree stays a single group", host.RootNode is DockTabGroup);

        // Side docking splits.
        var inspector = Item("Inspector");
        var right = host.Dock(inspector, DockSide.Right, group);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("side dock creates a split", host.RootNode is DockSplit, host.RootNode?.GetType().Name ?? "null");
        var split = (DockSplit)host.RootNode!;
        Check("split is horizontal", split.Orientation == Orientation.Horizontal);
        Check("split has two panes", split.Children.Count == 2, $"{split.Children.Count}");
        Check("original group is first", ReferenceEquals(split.Children[0], group));
        Check("new group is second", ReferenceEquals(split.Children[1], right));
        Check("panes tile the width",
            MathF.Abs(group.Bounds.Width + right.Bounds.Width + 6.0f - 1000.0f) < 1.0f,
            $"{group.Bounds.Width} + {right.Bounds.Width}");
        Check("panes are full height", MathF.Abs(group.Bounds.Height - 600.0f) < 0.01f, $"{group.Bounds.Height}");

        // Docking left must place the new pane before, not after.
        var tools = Item("Tools");
        var left = host.Dock(tools, DockSide.Left, group);
        root.UpdateLayout(new Vector2(1000, 600));

        var topSplit = (DockSplit)host.RootNode!;
        Check("same-axis dock flattens rather than nests",
            topSplit.Children.Count == 3, $"{topSplit.Children.Count} panes");
        Check("left dock inserts before", ReferenceEquals(topSplit.Children[0], left),
            (topSplit.Children[0] as DockTabGroup)?.Items[0].Title ?? "?");
        Check("panes ordered left to right",
            left.Bounds.X < group.Bounds.X && group.Bounds.X < right.Bounds.X,
            $"{left.Bounds.X} < {group.Bounds.X} < {right.Bounds.X}");

        // Cross-axis dock does nest.
        var timeline = Item("Timeline");
        var bottom = host.Dock(timeline, DockSide.Bottom, group);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("cross-axis dock nests a split", group.ParentSplit is { Orientation: Orientation.Vertical },
            group.ParentSplit?.Orientation.ToString() ?? "null");
        Check("bottom pane is below", bottom.Bounds.Y > group.Bounds.Y,
            $"{bottom.Bounds.Y} vs {group.Bounds.Y}");
        Check("nested split keeps the row intact", topSplit.Children.Count == 3, $"{topSplit.Children.Count}");

        // ---------------------------------------------------------------- weights and splitters
        var weightsBefore = topSplit.Weights.ToArray();
        Check("weights sum to one", MathF.Abs(weightsBefore.Sum() - 1.0f) < 0.001f, $"{weightsBefore.Sum()}");

        var splitterX = left.Bounds.Right + 3.0f;
        var splitterY = left.Bounds.Center.Y;
        root.Input.InjectPointerMoved(new Vector2(splitterX, splitterY));
        Check("splitter wants a resize cursor", root.RequestedCursor == UiCursor.SizeWE,
            root.RequestedCursor.ToString());

        var leftWidthBefore = left.Bounds.Width;
        var rightWidthBefore = right.Bounds.Width;
        root.Input.InjectPointerPressed(new Vector2(splitterX, splitterY), PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(new Vector2(splitterX + 80.0f, splitterY));
        root.UpdateLayout(new Vector2(1000, 600));

        Check("splitter drag widens the left pane", left.Bounds.Width > leftWidthBefore,
            $"{leftWidthBefore} -> {left.Bounds.Width}");
        Check("splitter drag leaves the far pane alone",
            MathF.Abs(right.Bounds.Width - rightWidthBefore) < 1.0f,
            $"{rightWidthBefore} -> {right.Bounds.Width}");
        Check("weights still sum to one",
            MathF.Abs(topSplit.Weights.Sum() - 1.0f) < 0.001f, $"{topSplit.Weights.Sum()}");

        root.Input.InjectPointerReleased(new Vector2(splitterX + 80.0f, splitterY), PointerButton.Left);

        // A splitter cannot squeeze a pane out of existence.
        root.Input.InjectPointerPressed(new Vector2(left.Bounds.Right + 3.0f, splitterY), PointerButton.Left, 1.0);
        root.Input.InjectPointerMoved(new Vector2(-5000.0f, splitterY));
        root.UpdateLayout(new Vector2(1000, 600));
        Check("panes keep a minimum size", left.Bounds.Width >= 30.0f, $"{left.Bounds.Width}");
        root.Input.InjectPointerReleased(new Vector2(-5000.0f, splitterY), PointerButton.Left);

        // ---------------------------------------------------------------- tabs
        root.UpdateLayout(new Vector2(1000, 600));
        Check("tab strip sits at the top of the group",
            MathF.Abs(group.TabStripRect.Y - group.Bounds.Y) < 0.01f, group.TabStripRect.ToString());

        var sceneTab = group.TabRect(0);
        root.Input.InjectPointerMoved(sceneTab.Center);
        root.Input.InjectPointerPressed(sceneTab.Center, PointerButton.Left, 2.0);
        root.Input.InjectPointerReleased(sceneTab.Center, PointerButton.Left);
        Check("clicking a tab selects it", group.SelectedItem == scene, group.SelectedItem?.Title ?? "null");

        root.UpdateLayout(new Vector2(1000, 600));
        Check("selected content is shown", scene.Content.Bounds.Height > 0, scene.Content.Bounds.ToString());
        Check("unselected content is not laid out", outliner.Content.Bounds.IsEmpty,
            outliner.Content.Bounds.ToString());

        // ---------------------------------------------------------------- pruning
        var beforePrune = host.AllItems().Count();
        Check("all items enumerated", beforePrune == 5, $"{beforePrune}");

        // Emptying a group must collapse it and the split that held it.
        group.Remove(scene);
        group.Remove(outliner);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("emptied group leaves the tree", host.AllItems().All(i => i.Title != "Scene"));
        Check("single-child split collapsed",
            group.ParentSplit == null || group.ParentSplit.Children.Count > 1,
            "no one-child splits remain");

        var remaining = host.AllItems().Select(i => i.Title).OrderBy(t => t).ToArray();
        Check("surviving items intact", remaining.SequenceEqual(new[] { "Inspector", "Timeline", "Tools" }),
            string.Join(", ", remaining));

        root.UpdateLayout(new Vector2(1000, 600));
        Check("layout still tiles after pruning",
            NoOverlaps(host), "panes do not overlap");

        // Removing everything empties the host.
        foreach (var item in host.AllItems().ToList())
        {
            item.Owner?.Remove(item);
        }

        host.Prune();
        root.UpdateLayout(new Vector2(1000, 600));
        Check("emptying removes the root", host.RootNode == null, host.RootNode?.GetType().Name ?? "null");

        // ---------------------------------------------------------------- moving between groups
        var fresh = new DockHost();
        root.Content = null;
        root.Content = fresh;

        var a = Item("A");
        var b = Item("B");
        var groupA = fresh.Dock(a, DockSide.Center);
        var groupB = fresh.Dock(b, DockSide.Right, groupA);
        root.UpdateLayout(new Vector2(800, 400));

        // Re-docking an existing item must move it, not clone it.
        fresh.Dock(a, DockSide.Center, groupB);
        root.UpdateLayout(new Vector2(800, 400));

        Check("re-dock moves the item", fresh.AllItems().Count(i => i.Title == "A") == 1,
            $"{fresh.AllItems().Count(i => i.Title == "A")} copies");
        Check("item moved to the new group", ReferenceEquals(a.Owner, groupB), a.Owner?.Items.Count.ToString() ?? "null");
        Check("emptied source pruned away", fresh.RootNode is DockTabGroup,
            fresh.RootNode?.GetType().Name ?? "null");
        Check("moved content re-parented", a.Content.Parent != null, "content has a parent");

        // ---------------------------------------------------------------- rendering
        root.UpdateLayout(new Vector2(800, 400));
        var draws = root.Render();
        var glyphs = 0;
        foreach (var instance in draws.Instances)
        {
            if ((instance.Flags & (uint)ManaUiInstanceFlags.GlyphAtlas) != 0)
            {
                glyphs++;
            }
        }

        Check("dock layout renders chrome and content", draws.InstanceCount > 5, $"{draws.InstanceCount} instances");
        Check("tab titles drawn", glyphs > 0, $"{glyphs} glyph quads");
        Check("still one draw call", draws.Batches.Count == 1, $"{draws.Batches.Count} batches");

        // ---------------------------------------------------------------- bindings survive tab switches
        // The case that made binding suspension necessary: a hidden tab's content is detached, and its
        // bindings must resume -- with current values -- when the tab is selected again.
        var tabVm = new DemoViewModel { Title = "before" };
        var tabbed = new DockHost();
        root.Content = null;
        root.Content = tabbed;

        var boundLabel = new TextBlock().BindText(tabVm, v => v.Title);
        var first = new DockItem("First", boundLabel);
        var second = new DockItem("Second", new TextBlock().Text("other"));
        var tabGroup = tabbed.Dock(first, DockSide.Center);
        tabbed.Dock(second, DockSide.Center, tabGroup);
        root.UpdateLayout(new Vector2(600, 400));

        Check("second tab selected", tabGroup.SelectedItem == second, tabGroup.SelectedItem?.Title ?? "null");

        tabVm.Title = "changed while hidden";
        tabGroup.SelectedIndex = 0;
        root.UpdateLayout(new Vector2(600, 400));

        Check("binding resumes after tab switch", boundLabel.Text == "changed while hidden", boundLabel.Text);

        tabVm.Title = "changed while visible";
        Check("binding live again", boundLabel.Text == "changed while visible", boundLabel.Text);
    }

    /// <summary>Verifies sibling panes in every split tile without overlapping.</summary>
    private static bool NoOverlaps(DockHost host)
    {
        return Check(host.RootNode);

        static bool Check(UiElement? node)
        {
            if (node == null)
            {
                return true;
            }

            if (node is DockSplit split)
            {
                for (var i = 0; i < split.Children.Count; i++)
                {
                    for (var j = i + 1; j < split.Children.Count; j++)
                    {
                        if (split.Children[i].Bounds.IntersectsWith(split.Children[j].Bounds))
                        {
                            return false;
                        }
                    }
                }
            }

            var count = node.VisualChildCount;
            for (var i = 0; i < count; i++)
            {
                if (!Check(node.GetVisualChild(i)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
