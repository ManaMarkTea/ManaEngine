using ManaUI;

namespace ManaTests;

/// <summary>
/// The two controls the editor's tool panel needs that the framework did not have: a tab strip, and a
/// graph of a recent series.
/// </summary>
public static class TabsAndGraphChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        Tabs(fonts, Check);
        Graphs(fonts, Check);
    }

    private static void Tabs(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var root = new UiRoot(fonts);

        var editor = new TextBox().Value("half typed");
        var physics = new TextBlock().Text("physics");
        var debug = new TextBlock().Text("debug");

        var tabs = new TabView().AddRange(
            new TabItem("Editor", editor),
            new TabItem("Physics", physics),
            new TabItem("Debug", debug) { Tooltip = "Ray casts and counters" });

        root.Content = new Grid().Rows("*").Columns("*").Children(tabs);
        root.UpdateLayout(new Vector2(600, 400));

        Check("a tab view holds its tabs", tabs.Tabs.Count == 3, $"{tabs.Tabs.Count}");
        Check("the first is selected", tabs.SelectedIndex == 0 && tabs.SelectedTab?.Header == "Editor",
            tabs.SelectedTab?.Header ?? "none");

        Check("only the selected tab's content is shown",
            editor.Visibility == Visibility.Visible
                && physics.Visibility == Visibility.Collapsed
                && debug.Visibility == Visibility.Collapsed,
            $"{editor.Visibility}/{physics.Visibility}/{debug.Visibility}");

        // The strip has to be clickable, not just visible.
        var headers = FindAll<UiElement>(tabs).Where(e => e.GetType().Name == "TabHeader").ToList();
        Check("there is a header per tab", headers.Count == 3, $"{headers.Count} headers");

        Check("headers sit side by side",
            headers.Count == 3 && headers[0].Bounds.X < headers[1].Bounds.X && headers[1].Bounds.X < headers[2].Bounds.X,
            string.Join(", ", headers.Select(h => h.Bounds.X.ToString("F0"))));

        if (headers.Count == 3)
        {
            root.Input.InjectPointerMoved(headers[1].Bounds.Center);
            root.Input.InjectPointerPressed(headers[1].Bounds.Center, PointerButton.Left, 1.0);
            root.Input.InjectPointerReleased(headers[1].Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(600, 400));

            Check("clicking a header selects it", tabs.SelectedIndex == 1, $"{tabs.SelectedTab?.Header}");
            Check("and swaps the content",
                physics.Visibility == Visibility.Visible && editor.Visibility == Visibility.Collapsed,
                $"{editor.Visibility}/{physics.Visibility}");
        }

        // The reason for keeping every tab built rather than rebuilding on switch: a half-typed field in
        // a tab you flick away from and back should still be half typed.
        editor.Text = "still here";
        tabs.SelectedIndex = 0;
        root.UpdateLayout(new Vector2(600, 400));
        Check("switching away and back keeps a tab's state", editor.Text == "still here", editor.Text);

        // Restoring which tab was open last session is by name, since an index means nothing across a
        // change to the tab set.
        Check("a tab can be selected by header", tabs.SelectByHeader("Debug") && tabs.SelectedIndex == 2,
            tabs.SelectedTab?.Header ?? "none");
        Check("and an unknown header changes nothing",
            !tabs.SelectByHeader("Nope") && tabs.SelectedIndex == 2, $"{tabs.SelectedIndex}");

        var raised = new List<int>();
        tabs.SelectionChanged += (_, index) => raised.Add(index);
        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 0;
        Check("selection is reported once per actual change", raised.Count == 1, $"{raised.Count} events");

        // A disabled tab is one that does not apply yet, and it must not be reachable by clicking.
        var gated = false;
        var guarded = new TabView().AddRange(
            new TabItem("Always", new TextBlock().Text("a")),
            new TabItem("Sometimes", new TextBlock().Text("b")) { IsEnabledProvider = () => gated });

        root.Content = null;
        root.Content = new Grid().Rows("*").Columns("*").Children(guarded);
        root.UpdateLayout(new Vector2(600, 400));

        var guardedHeaders = FindAll<UiElement>(guarded).Where(e => e.GetType().Name == "TabHeader").ToList();
        if (guardedHeaders.Count == 2)
        {
            root.Input.InjectPointerMoved(guardedHeaders[1].Bounds.Center);
            root.Input.InjectPointerPressed(guardedHeaders[1].Bounds.Center, PointerButton.Left, 2.0);
            root.Input.InjectPointerReleased(guardedHeaders[1].Bounds.Center, PointerButton.Left);
            Check("a disabled tab cannot be selected", guarded.SelectedIndex == 0, $"{guarded.SelectedIndex}");

            gated = true;
            root.Input.InjectPointerPressed(guardedHeaders[1].Bounds.Center, PointerButton.Left, 3.0);
            root.Input.InjectPointerReleased(guardedHeaders[1].Bounds.Center, PointerButton.Left);
            Check("and can once it applies", guarded.SelectedIndex == 1, $"{guarded.SelectedIndex}");
        }

        Check("a tab view is still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");
    }

    private static void Graphs(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var root = new UiRoot(fonts);

        var samples = new List<float> { 10.0f, 20.0f, 30.0f, 40.0f, 50.0f };
        var graph = new Sparkline
        {
            Samples = samples,
            Minimum = 0.0f,
            Maximum = 100.0f,
            Width = 120.0f,
            Height = 40.0f
        };

        root.Content = new Canvas().Children(graph.At(10.0f, 10.0f));
        root.UpdateLayout(new Vector2(400, 200));

        var drawn = root.Render();
        Check("a graph renders", drawn.InstanceCount > samples.Count, $"{drawn.InstanceCount} instances");
        Check("and stays in one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // Columns are drawn bottom-up, so a taller sample must reach higher. Checked by comparing the top
        // edges of the drawn columns rather than trusting the loop.
        var columns = drawn.Instances.ToArray()
            .Where(i => i.Rect.X > graph.Bounds.X && i.Rect.Z < graph.Bounds.Right + 1.0f)
            .Where(i => i.Rect.W - i.Rect.Y > 1.0f && i.Rect.Z - i.Rect.X <= 6.0f)
            .OrderBy(i => i.Rect.X)
            .ToList();

        Check("one column per sample", columns.Count == samples.Count, $"{columns.Count} columns");

        if (columns.Count == samples.Count)
        {
            var rising = true;
            for (var i = 1; i < columns.Count; i++)
            {
                rising &= columns[i].Rect.Y < columns[i - 1].Rect.Y;
            }

            Check("a rising series draws rising columns", rising,
                string.Join(", ", columns.Select(c => (c.Rect.W - c.Rect.Y).ToString("F0"))));

            Check("and they all sit on the baseline",
                columns.All(c => MathF.Abs(c.Rect.W - columns[0].Rect.W) < 1.5f), "same bottom");
        }

        // The series is read at render time and never copied, so a caller graphing a buffer it refills
        // every frame pays nothing for it.
        samples[4] = 100.0f;
        var taller = root.Render().Instances.ToArray()
            .Where(i => i.Rect.X > graph.Bounds.X && i.Rect.Z < graph.Bounds.Right + 1.0f)
            .Where(i => i.Rect.W - i.Rect.Y > 1.0f && i.Rect.Z - i.Rect.X <= 6.0f)
            .OrderBy(i => i.Rect.X)
            .ToList();

        Check("changing the series changes the graph without re-assigning it",
            taller.Count == samples.Count && taller[^1].Rect.Y < columns[^1].Rect.Y,
            "read live");

        // An empty or absent series is a normal state -- the first frames of any counter.
        graph.Samples = null;
        root.UpdateLayout(new Vector2(400, 200));
        Check("an empty graph still draws its background", root.Render().InstanceCount > 0,
            $"{root.Render().InstanceCount} instances");
    }

    private static List<T> FindAll<T>(UiElement? element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;
    }

    private static void Walk<T>(UiElement? element, List<T> found) where T : UiElement
    {
        if (element == null)
        {
            return;
        }

        if (element is T match)
        {
            found.Add(match);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Walk(element.GetVisualChild(i), found);
        }
    }
}
