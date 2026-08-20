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

        // The gutter between two panes has to match the panel chrome around it (Surface), not the shell
        // colour outside the dock entirely (Background) -- that mismatch is what read as a gap in the
        // panel rather than a divider inside it, even though every pixel was opaque.
        var gutterFill = root.Render().Instances.ToArray()
            .Where(i => splitterX >= i.Rect.X && splitterX <= i.Rect.Z && splitterY >= i.Rect.Y && splitterY <= i.Rect.W)
            .Select(i => UiColor.Unpack(i.FillColor))
            .FirstOrDefault(color => Vector4.Distance(color, root.Theme.Surface) < 0.01f
                || Vector4.Distance(color, root.Theme.Background) < 0.01f);

        Check("the splitter gutter matches the surrounding panel surface, not the outer shell",
            Vector4.Distance(gutterFill, root.Theme.Surface) < 0.01f,
            $"sampled {gutterFill} vs surface {root.Theme.Surface} / background {root.Theme.Background}");

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

        // ---------------------------------------------------------------- a crowded strip
        // Tabs laid out at their natural width run off the end of a narrow group, and a tab past the edge
        // is not merely ugly -- it cannot be clicked at all. That is how the Inspector disappeared from a
        // 400px dock once it became the sixth panel.
        var crowded = new DockTabGroup();
        for (var i = 0; i < 6; i++)
        {
            crowded.Add(new DockItem($"Long Panel Name {i}", new TextBlock().Text($"Body {i}")));
        }

        var crowdedHost = new DockHost { RootNode = crowded };
        root.Content = null;
        root.Content = crowdedHost;
        root.UpdateLayout(new Vector2(400, 400));

        var lastCrowded = crowded.Items.Count - 1;

        // Adding a panel selects it, so the sixth is the selected one here -- and a selected tab is always
        // visible, which would otherwise make "which tabs are hidden" depend on that. Pinned to the first so
        // the rest of this block reads against a known strip.
        crowded.SelectedIndex = 0;
        root.UpdateLayout(new Vector2(400, 400));

        Check("visible tabs stay inside the strip",
            Enumerable.Range(0, crowded.Items.Count)
                .Select(crowded.TabRect)
                .Where(tab => !tab.IsEmpty)
                .All(tab => tab.Right <= crowded.Bounds.Right + 0.5f),
            $"strip ends at {crowded.Bounds.Right:F1}");

        Check("adjacent visible tabs meet without overlapping",
            MathF.Abs(crowded.TabRect(1).X - crowded.TabRect(0).Right) < 0.5f,
            $"{crowded.TabRect(0)} then {crowded.TabRect(1)}");

        Check("and are wide enough to hit", crowded.TabRect(0).Width > 20.0f,
            $"{crowded.TabRect(0).Width:F1}px");

        // A visible tab is selected by clicking it, as always.
        var firstCrowded = crowded.TabRect(0);
        root.Input.InjectPointerMoved(firstCrowded.Center);
        root.Input.InjectPointerPressed(firstCrowded.Center, PointerButton.Left, 3.0);
        root.Input.InjectPointerReleased(firstCrowded.Center, PointerButton.Left);
        Check("clicking a visible tab selects it", crowded.SelectedIndex == 0, $"{crowded.SelectedIndex}");

        // Six long titles cannot all fit 400px, so the tail is behind the overflow button rather than
        // squeezed to an ellipsis. Unreachable is the failure this guards; hidden is the design.
        Check("the tail is in the overflow rather than squeezed",
            crowded.OverflowedIndices().Contains(lastCrowded),
            $"hidden: {string.Join(", ", crowded.OverflowedIndices())}");

        // Selecting from the overflow menu is what the flyout does, and it has to bring the tab into view.
        crowded.SelectedIndex = lastCrowded;
        root.UpdateLayout(new Vector2(400, 400));
        Check("selecting it from the overflow shows it",
            !crowded.TabRect(lastCrowded).IsEmpty, $"{crowded.TabRect(lastCrowded)}");

        // Given room, tabs go back to their natural widths rather than staying squeezed.
        root.UpdateLayout(new Vector2(1600, 400));
        Check("a roomy strip uses natural widths",
            crowded.TabRect(0).Width > crowded.TabRect(0).Height * 2.0f,
            $"{crowded.TabRect(0).Width:F1}px");

        // ---------------------------------------------------------------- a toolbar-shaped single tab
        // A dock host that only ever holds one panel (the top toolbar strip) does not need a tab header
        // naming the one thing it could possibly be -- opt-in, so every other panel keeps its title.
        var soloGroup = new DockTabGroup { HideStripWhenSingle = true };
        soloGroup.Add(new DockItem("Voxel Tools", new TextBlock().Text("Toolbar")));

        var soloHost = new DockHost { RootNode = soloGroup };
        root.Content = null;
        root.Content = soloHost;
        root.UpdateLayout(new Vector2(400, 100));

        Check("a lone tab with HideStripWhenSingle set collapses its strip",
            MathF.Abs(soloGroup.TabStripRect.Height) < 0.01f, $"{soloGroup.TabStripRect.Height:F1}px");

        soloGroup.Add(new DockItem("Second", new TextBlock().Text("Body")));
        root.UpdateLayout(new Vector2(400, 100));

        Check("the strip returns once a second tab joins",
            soloGroup.TabStripRect.Height > 10.0f, $"{soloGroup.TabStripRect.Height:F1}px");

        var defaultGroup = new DockTabGroup();
        defaultGroup.Add(new DockItem("Alone", new TextBlock().Text("Body")));

        var defaultHost = new DockHost { RootNode = defaultGroup };
        root.Content = null;
        root.Content = defaultHost;
        root.UpdateLayout(new Vector2(400, 100));

        Check("a lone tab keeps its strip when the flag is not set",
            defaultGroup.TabStripRect.Height > 10.0f, $"{defaultGroup.TabStripRect.Height:F1}px");

        root.Content = null;
        root.Content = host;
        root.UpdateLayout(new Vector2(1000, 600));

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

/// <summary>
/// The tab strip's multi-row wrapping. Every check here is on geometry rather than on the item list: a tab
/// that exists but is laid out past the strip's edge, or under another tab, is a panel the user cannot get
/// back to.
/// </summary>
public static class TabRowChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static DockItem Item(string title) => new(title, new Border().Fill(UiColor.White)) { Id = title };

        static DockTabGroup Fill(UiRoot root, DockHost host, Vector2 size, params string[] titles)
        {
            var group = host.Dock(Item(titles[0]), DockSide.Center);
            for (var i = 1; i < titles.Length; i++)
            {
                host.Dock(Item(titles[i]), DockSide.Center, group);
            }

            root.UpdateLayout(size);
            return group;
        }

        // A few tabs in a wide dock: unchanged from a single-row strip, packed left at their natural widths
        // rather than stretched across the whole strip.
        var wide = new UiRoot(fonts);
        var wideHost = new DockHost();
        wide.Content = wideHost;

        var few = Fill(wide, wideHost, new Vector2(1000, 600), "Scene", "Outliner", "Inspector");

        Check("a few tabs stay on one row", few.RowCount == 1, $"{few.RowCount} rows");
        Check("and do not stretch to fill", few.TabRect(2).Right < 900.0f, $"ends at {few.TabRect(2).Right:F0}");
        Check("tabs sit left to right", few.TabRect(0).Right <= few.TabRect(1).X + 0.5f);

        // Eleven panels in a narrow dock -- the editor's actual left column, and what made the strip
        // unreadable when every tab was shrunk to fit one row.
        var narrow = new UiRoot(fonts);
        var narrowHost = new DockHost();
        narrow.Content = narrowHost;

        var many = Fill(
            narrow,
            narrowHost,
            new Vector2(400, 800),
            "Scene", "Outliner", "Inspector", "Assets", "Materials", "Lighting",
            "Terrain", "Regions", "Placement", "Import", "About");

        // The contract this suite used to assert was the opposite: that the strip WRAPS onto extra rows.
        // Wrapping was readable but cost up to 78px of a dock's height on labels alone, which is height a
        // docked panel does not have to spare -- so a crowded strip now stays one row and moves the tabs that
        // do not fit behind an overflow button. What is checked is that nothing becomes unreachable in the
        // process, which is the risk that trade introduces.
        Check("a crowded strip stays one row", many.RowCount == 1, $"{many.RowCount} rows");

        var strip = many.TabStripRect;
        Check("so the strip is exactly one row tall",
            MathF.Abs(strip.Height - many.TabRect(0).Height) < 0.5f, $"{strip.Height:F0}px");

        var overflow = many.OverflowRect;
        Check("and offers an overflow button", !overflow.IsEmpty, overflow.ToString());
        Check("which sits inside the strip",
            overflow.Right <= strip.Right + 0.5f && overflow.Y >= strip.Y - 0.5f
                && overflow.Bottom <= strip.Bottom + 0.5f,
            $"{overflow} in {strip}");

        var hidden = many.OverflowedIndices().ToList();
        var shown = Enumerable.Range(0, many.Items.Count).Count(i => !many.TabRect(i).IsEmpty);
        Check("every tab is either shown or in the overflow menu",
            hidden.Count + shown == many.Items.Count,
            $"{shown} shown + {hidden.Count} hidden of {many.Items.Count}");

        Check("and the hidden ones are genuinely hidden",
            hidden.TrueForAll(index => many.TabRect(index).IsEmpty), $"{hidden.Count} hidden");

        // The point of the trade: the tabs that ARE shown are full width rather than ellipsis stubs. Eleven
        // shrunk to fit 400px would be about 36px each.
        var narrowest = float.MaxValue;
        var escaped = 0;

        for (var i = 0; i < many.Items.Count; i++)
        {
            var tab = many.TabRect(i);
            if (tab.IsEmpty)
            {
                continue;
            }

            narrowest = MathF.Min(narrowest, tab.Width);

            if (tab.X < strip.X - 0.5f || tab.Right > strip.Right + 0.5f
                || tab.Y < strip.Y - 0.5f || tab.Bottom > strip.Bottom + 0.5f)
            {
                escaped++;
            }
        }

        Check("every visible tab is inside the strip", escaped == 0, $"{escaped} escaped");
        Check("and wide enough to read", narrowest >= 80.0f, $"narrowest {narrowest:F0}px");

        // Visible tabs must not overlap each other; empty rectangles are skipped, since an overflowed tab
        // has no geometry to collide with.
        var overlaps = 0;
        for (var i = 0; i < many.Items.Count; i++)
        {
            for (var j = i + 1; j < many.Items.Count; j++)
            {
                var a = many.TabRect(i);
                var b = many.TabRect(j);

                if (a.IsEmpty || b.IsEmpty)
                {
                    continue;
                }

                if (a.X < b.Right - 0.5f && b.X < a.Right - 0.5f
                    && a.Y < b.Bottom - 0.5f && b.Y < a.Bottom - 0.5f)
                {
                    overlaps++;
                }
            }
        }

        Check("no two visible tabs overlap", overlaps == 0, $"{overlaps} overlapping pairs");

        // Visible tabs run edge to edge up to the overflow button, so a ragged end does not read as broken.
        var lastVisibleRight = 0.0f;
        for (var i = 0; i < many.Items.Count; i++)
        {
            var tab = many.TabRect(i);
            if (!tab.IsEmpty)
            {
                lastVisibleRight = MathF.Max(lastVisibleRight, tab.Right);
            }
        }

        Check("the visible tabs fill the strip up to the button",
            MathF.Abs(lastVisibleRight - overflow.X) < 1.0f,
            $"tabs end {lastVisibleRight:F0}, button at {overflow.X:F0}");

        // A one-row strip still has to cost the panel that row, or the two overlap.
        var content = many.SelectedItem?.Content;
        Check("content starts below the strip",
            content != null && MathF.Abs(content.Bounds.Y - strip.Bottom) < 1.0f,
            content?.Bounds.ToString() ?? "none");

        // The failure mode worth guarding: selecting a panel from the overflow menu and then not being able
        // to see which one is selected. The selected tab takes a visible slot no matter where it sits.
        Check("the selected tab is visible to begin with", !many.TabRect(many.SelectedIndex).IsEmpty,
            $"tab {many.SelectedIndex}");

        var lastIndex = many.Items.Count - 1;
        many.SelectedIndex = lastIndex;
        narrow.UpdateLayout(new Vector2(400, 800));

        Check("and still visible after picking the last panel from the overflow",
            !many.TabRect(lastIndex).IsEmpty, $"tab {lastIndex} at {many.TabRect(lastIndex)}");

        Check("which does not push the strip onto a second row", many.RowCount == 1, $"{many.RowCount} rows");

        Check("and leaves the rest reachable",
            many.OverflowedIndices().Any(), $"{many.OverflowedIndices().Count()} still hidden");

        // Pressing the button opens a menu rather than selecting anything, so the selection must not move --
        // an overflow button that quietly changed panels on the way to its own menu would be its own bug.
        var beforeOverflowPress = many.SelectedIndex;
        var buttonPoint = many.OverflowRect.Center;

        narrow.Input.InjectPointerMoved(buttonPoint);
        narrow.Input.InjectPointerPressed(buttonPoint, PointerButton.Left, 0.0);
        narrow.Input.InjectPointerReleased(buttonPoint, PointerButton.Left);

        Check("pressing the overflow button does not change the selection",
            many.SelectedIndex == beforeOverflowPress,
            $"selected {many.SelectedIndex}, was {beforeOverflowPress}");

        Check("and it opens a menu listing the hidden panels",
            narrow.Popups.HasOpenPopups, $"{narrow.Popups.Open.Count} popups");

        // Forty panels in a 300px dock: the case that used to hit the three-row cap and then fall back to
        // shrinking every tab to an unreadable stub. One row now holds it regardless of how absurd the count.
        var crowdedRoot = new UiRoot(fonts);
        var crowdedRootHost = new DockHost();
        crowdedRoot.Content = crowdedRootHost;

        var titles = new string[40];
        for (var i = 0; i < titles.Length; i++)
        {
            titles[i] = $"Panel {i}";
        }

        var capped = Fill(crowdedRoot, crowdedRootHost, new Vector2(300, 800), titles);

        Check("forty panels still make one row", capped.RowCount == 1, $"{capped.RowCount} rows");
        Check("and the strip costs the panel one row, not a third of it",
            capped.TabStripRect.Height < capped.Bounds.Height * 0.1f,
            $"{capped.TabStripRect.Height:F0} of {capped.Bounds.Height:F0}");
        Check("with the remainder behind the button",
            capped.OverflowedIndices().Count() > 30, $"{capped.OverflowedIndices().Count()} hidden");
    }
}
