using ManaUI;

namespace ManaTests;

public static class PersistenceChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static DockItem Item(string title) =>
            new(title, new Border().Fill(UiColor.White)) { Id = title };

        // Fresh content each time, exactly as an application factory would supply.
        static DockItem? Resolve(string id) =>
            id is "Scene" or "Outliner" or "Inspector" or "Timeline" ? Item(id) : null;

        var root = new UiRoot(fonts);
        var host = new DockHost();
        root.Content = host;

        var scene = Item("Scene");
        var group = host.Dock(scene, DockSide.Center);
        host.Dock(Item("Outliner"), DockSide.Center, group);
        var rightGroup = host.Dock(Item("Inspector"), DockSide.Right, group);
        host.Dock(Item("Timeline"), DockSide.Bottom, group);
        root.UpdateLayout(new Vector2(1200, 700));

        // Set uneven weights so the saved layout is not the default, or the test would pass trivially.
        var topSplit = (DockSplit)host.RootNode!;
        topSplit.SetWeights(new[] { 0.7f, 0.3f });
        root.UpdateLayout(new Vector2(1200, 700));

        Check("SetWeights assigns exact proportions",
            MathF.Abs(topSplit.Weights[0] - 0.7f) < 0.001f,
            $"{string.Join(", ", topSplit.Weights.Select(w => w.ToString("F3")))}");

        var savedWeights = topSplit.Weights.ToArray();
        var savedTitles = host.AllItems().Select(i => i.Title).ToArray();
        var savedSelected = group.SelectedIndex;

        // ---------------------------------------------------------------- capture
        var layout = DockLayout.Capture(host);
        Check("layout captured", layout != null, layout?.Type ?? "null");
        Check("root captured as a split", layout!.Type == "split", layout.Type);
        Check("split records its children", layout.Children is { Count: 2 }, $"{layout.Children?.Count}");
        Check("split records its weights", layout.Weights is { Count: 2 }, $"{layout.Weights?.Count}");

        var json = DockLayout.ToJson(layout);
        Check("serialises to json", json.Contains("Scene") && json.Contains("split"), $"{json.Length} chars");

        // ---------------------------------------------------------------- restore into a fresh host
        var restoredHost = new DockHost();
        root.Content = null;
        root.Content = restoredHost;

        var parsed = DockLayout.FromJson(json);
        Check("round-trips through json", parsed != null && parsed.Type == "split", parsed?.Type ?? "null");
        Check("restore succeeds", DockLayout.Apply(restoredHost, parsed, Resolve));
        root.UpdateLayout(new Vector2(1200, 700));

        var restoredTitles = restoredHost.AllItems().Select(i => i.Title).ToArray();
        Check("all panels restored", restoredTitles.SequenceEqual(savedTitles),
            $"{string.Join(", ", restoredTitles)}");

        Check("structure restored", restoredHost.RootNode is DockSplit,
            restoredHost.RootNode?.GetType().Name ?? "null");

        var restoredSplit = (DockSplit)restoredHost.RootNode!;
        Check("orientation restored", restoredSplit.Orientation == topSplit.Orientation,
            restoredSplit.Orientation.ToString());
        Check("weights restored",
            restoredSplit.Weights.Count == savedWeights.Length
            && MathF.Abs(restoredSplit.Weights[0] - savedWeights[0]) < 0.001f,
            $"{string.Join(", ", restoredSplit.Weights.Select(w => w.ToString("F3")))}");

        var restoredGroup = DockHost.FindFirstGroup(restoredHost.RootNode)!;
        Check("tab selection restored", restoredGroup.SelectedIndex == savedSelected,
            $"{restoredGroup.SelectedIndex} vs {savedSelected}");

        // Geometry must come out the same, which is the real point of persisting weights.
        var originalWidths = new[] { 0.7f, 0.3f };
        var totalWidth = restoredSplit.Children.Sum(c => c.Bounds.Width);
        Check("restored panes have the saved proportions",
            MathF.Abs(restoredSplit.Children[0].Bounds.Width / totalWidth - originalWidths[0]) < 0.02f,
            $"{restoredSplit.Children[0].Bounds.Width / totalWidth:F3}");

        Check("restored layout renders", root.Render().InstanceCount > 10);

        // ---------------------------------------------------------------- resilience
        // A layout that outlives the panel it references must still load the rest.
        static DockItem? PartialResolve(string id) => id == "Inspector" ? null : Item(id);

        var partialHost = new DockHost();
        root.Content = null;
        root.Content = partialHost;

        Check("loads with a missing panel", DockLayout.Apply(partialHost, DockLayout.FromJson(json), PartialResolve));
        root.UpdateLayout(new Vector2(1200, 700));

        var partialTitles = partialHost.AllItems().Select(i => i.Title).OrderBy(t => t).ToArray();
        Check("missing panel skipped, rest intact",
            partialTitles.SequenceEqual(new[] { "Outliner", "Scene", "Timeline" }),
            string.Join(", ", partialTitles));
        Check("no empty panes left behind", NoEmptyNodes(partialHost), "tree has no empty nodes");

        // Nothing resolvable at all must leave the host untouched rather than blanking it.
        var untouchedHost = new DockHost();
        root.Content = null;
        root.Content = untouchedHost;
        untouchedHost.Dock(Item("Fallback"), DockSide.Center);

        Check("unresolvable layout reports failure",
            !DockLayout.Apply(untouchedHost, DockLayout.FromJson(json), _ => null));
        Check("failed restore keeps the existing layout",
            untouchedHost.AllItems().Any(i => i.Title == "Fallback"),
            string.Join(", ", untouchedHost.AllItems().Select(i => i.Title)));

        // Corrupt and empty input must be survivable -- this runs at startup.
        Check("corrupt json returns null", DockLayout.FromJson("{ not json") == null);
        Check("empty json returns null", DockLayout.FromJson(string.Empty) == null);
        Check("null layout reports failure", !DockLayout.Apply(new DockHost(), null, Resolve));

        // Items with no id cannot be restored, so they must not be recorded either.
        var anonHost = new DockHost();
        root.Content = null;
        root.Content = anonHost;
        anonHost.Dock(new DockItem("Anon", new Border()), DockSide.Center);
        Check("id-less panels are not recorded", DockLayout.Capture(anonHost) == null,
            DockLayout.Capture(anonHost)?.Type ?? "null");

        // ---------------------------------------------------------------- file round trip
        var path = Path.Combine(Path.GetTempPath(), $"manaui-layout-{Guid.NewGuid():N}.json");
        try
        {
            root.Content = null;
            root.Content = restoredHost;
            DockLayout.Save(restoredHost, path);
            Check("layout file written", File.Exists(path));

            var fileHost = new DockHost();
            root.Content = null;
            root.Content = fileHost;
            Check("layout file loaded", DockLayout.Load(fileHost, path, Resolve));
            Check("file round trip preserves panels",
                fileHost.AllItems().Select(i => i.Title).SequenceEqual(savedTitles),
                string.Join(", ", fileHost.AllItems().Select(i => i.Title)));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        Check("missing file reports failure",
            !DockLayout.Load(new DockHost(), Path.Combine(Path.GetTempPath(), "no-such-layout.json"), Resolve));
    }

    private static bool NoEmptyNodes(DockHost host)
    {
        return Check(host.RootNode);

        static bool Check(UiElement? node)
        {
            if (node is DockNode dock && dock.IsEmpty)
            {
                return false;
            }

            if (node is DockSplit split && split.Children.Count < 2)
            {
                return false;
            }

            if (node == null)
            {
                return true;
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
