using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

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

        // Which tab is showing is part of the layout, and nothing saves a layout that never reports having
        // changed. Without this the file only ever records whichever panel was docked last, so every
        // session reopens on that tab no matter what the user had selected.
        var selectionSignals = 0;
        host.LayoutChanged += (_, _) => selectionSignals++;

        var otherTab = group.SelectedIndex == 0 ? group.Items.Count - 1 : 0;
        group.SelectedIndex = otherTab;
        Check("changing the active tab reports a layout change", selectionSignals == 1,
            $"{selectionSignals} signals");

        selectionSignals = 0;
        group.SelectedIndex = otherTab;
        Check("re-selecting the same tab reports nothing", selectionSignals == 0,
            $"{selectionSignals} signals");

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

        // ---------------------------------------------------------------- layout versioning
        // Reproduces the reported case: a layout saved before a panel existed must not silently hide it
        // forever. The version is what tells an out-of-date file from a current one.
        var versioned = DockLayout.Capture(host, 7);
        Check("capture records the version", versioned!.LayoutVersion == 7, $"{versioned.LayoutVersion}");

        var versionedJson = DockLayout.ToJson(versioned);
        Check("version survives json", DockLayout.FromJson(versionedJson)!.LayoutVersion == 7,
            $"{DockLayout.FromJson(versionedJson)!.LayoutVersion}");

        var versionPath = Path.Combine(Path.GetTempPath(), $"manaui-version-{Guid.NewGuid():N}.json");
        try
        {
            // An older layout containing only one panel -- exactly the file that caused the report.
            var stale = new DockLayoutNode
            {
                LayoutVersion = 1,
                Type = "tabs",
                Items = new List<string> { "Scene" },
                SelectedIndex = 0
            };

            File.WriteAllText(versionPath, DockLayout.ToJson(stale));

            var staleHost = new DockHost();
            root.Content = null;
            root.Content = staleHost;

            Check("stale layout loads", DockLayout.Load(staleHost, versionPath, Resolve, out var loadedVersion));
            Check("stale version reported back", loadedVersion == 1, $"{loadedVersion}");
            Check("stale layout has only its own panel",
                staleHost.AllItems().Count() == 1, $"{staleHost.AllItems().Count()}");

            // What the application does with a version mismatch: merge in whatever is missing.
            var known = new[] { "Scene", "Outliner", "Inspector", "Timeline" };
            var present = staleHost.AllItems().Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var id in known.Where(id => !present.Contains(id)))
            {
                var item = Resolve(id);
                if (item != null)
                {
                    staleHost.Dock(item, DockSide.Center, DockHost.FindFirstGroup(staleHost.RootNode));
                }
            }

            root.UpdateLayout(new Vector2(1200, 700));
            Check("merging surfaces the missing panels",
                staleHost.AllItems().Count() == 4, $"{staleHost.AllItems().Count()}");
            Check("merging keeps the original panel",
                staleHost.AllItems().Any(i => i.Id == "Scene"),
                string.Join(", ", staleHost.AllItems().Select(i => i.Id)));

            // Saving with the current version means the next run takes the layout as-is.
            DockLayout.Save(staleHost, versionPath, 2);
            var currentHost = new DockHost();
            root.Content = null;
            root.Content = currentHost;

            Check("re-saved layout carries the new version",
                DockLayout.Load(currentHost, versionPath, Resolve, out var currentVersion) && currentVersion == 2,
                $"{currentVersion}");
            Check("current layout keeps every panel",
                currentHost.AllItems().Count() == 4, $"{currentHost.AllItems().Count()}");

            // A file with no version at all reads as 0, so pre-versioning layouts count as out of date.
            File.WriteAllText(versionPath, "{ \"type\": \"tabs\", \"items\": [\"Scene\"] }");
            var legacyHost = new DockHost();
            root.Content = null;
            root.Content = legacyHost;
            Check("unversioned layout reads as version 0",
                DockLayout.Load(legacyHost, versionPath, Resolve, out var legacyVersion) && legacyVersion == 0,
                $"{legacyVersion}");
        }
        finally
        {
            if (File.Exists(versionPath))
            {
                File.Delete(versionPath);
            }
        }
        // ---------------------------------------------------------------- workspace round trip
        // Against a scratch file rather than the user's real workspace. Nothing exercised save-and-reload
        // before, which is how a theme switch came to persist the theme it was leaving: the save ran
        // before the swap -- it has to, the columns it also records are about to be replaced -- and read
        // the outgoing value off the root.
        var scratch = Path.Combine(Path.GetTempPath(), $"manaui-workspace-{Guid.NewGuid():N}.json");

        try
        {
            var themeRoot = new UiRoot(fonts);
            var themeVm = new ManaUiShowcaseViewModel();

            themeRoot.Content = ManaUiDockDemo.Attach(
                themeRoot, themeVm, () => "frame", persist: true, layoutPath: scratch);
            themeRoot.UpdateLayout(new Vector2(1600, 900));

            Check("a fresh workspace starts dark", themeRoot.Theme.Kind == UiThemeKind.Dark,
                themeRoot.Theme.Kind.ToString());

            var themeMenu = FindFirst<MenuBar>(themeRoot)!
                .Menus.First(m => m.Text == "Appearance")
                .Items.First(i => i.Text == "Theme");

            themeMenu.Items.First(i => i.Text == "Light").Invoked?.Invoke();
            themeRoot.UpdateLayout(new Vector2(1600, 900));
            Check("switching applies light", themeRoot.Theme.Kind == UiThemeKind.Light,
                themeRoot.Theme.Kind.ToString());

            var written = ManaUiWorkspaceLayout.Load(scratch);
            Check("the file records the theme just chosen", written?.Theme == UiThemeKind.Light,
                written?.Theme.ToString() ?? "nothing written");

            // The real test: a fresh session against the same file.
            var reopened = new UiRoot(fonts);
            reopened.Content = ManaUiDockDemo.Attach(
                reopened, new ManaUiShowcaseViewModel(), () => "frame", persist: true, layoutPath: scratch);
            reopened.UpdateLayout(new Vector2(1600, 900));

            Check("reopening comes up light", reopened.Theme.Kind == UiThemeKind.Light,
                reopened.Theme.Kind.ToString());

            // And back again, which is the direction that was stuck: every switch wrote the outgoing
            // theme, so choosing dark saved light and the next run was light again.
            var reopenedTheme = FindFirst<MenuBar>(reopened)!
                .Menus.First(m => m.Text == "Appearance")
                .Items.First(i => i.Text == "Theme");

            reopenedTheme.Items.First(i => i.Text == "Dark").Invoked?.Invoke();
            reopened.UpdateLayout(new Vector2(1600, 900));

            Check("the file records dark too",
                ManaUiWorkspaceLayout.Load(scratch)?.Theme == UiThemeKind.Dark,
                ManaUiWorkspaceLayout.Load(scratch)?.Theme.ToString() ?? "nothing written");

            var third = new UiRoot(fonts);
            third.Content = ManaUiDockDemo.Attach(
                third, new ManaUiShowcaseViewModel(), () => "frame", persist: true, layoutPath: scratch);
            third.UpdateLayout(new Vector2(1600, 900));

            Check("and reopening comes up dark", third.Theme.Kind == UiThemeKind.Dark,
                third.Theme.Kind.ToString());
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (IOException)
            {
                // A scratch file left behind is not worth failing the suite over.
            }
        }

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

    private static T? FindFirst<T>(UiElement element) where T : UiElement
    {
        if (element is T typed)
        {
            return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            var child = element.GetVisualChild(i);
            if (child != null && FindFirst<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
