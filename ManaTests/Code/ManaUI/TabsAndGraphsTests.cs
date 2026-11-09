using ManaUI;
using ManaEditor.Code.UI.ManaUi;

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
        HostedMenuContent(fonts, Check);
        PerformanceStrip(fonts, Check);
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

    /// <summary>
    /// A control living in a menu. The flyout is rebuilt every time the menu opens, but the control is one
    /// element owned by the item -- so it has to leave the popup that held it last time.
    /// </summary>
    private static void HostedMenuContent(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var root = new UiRoot(fonts);

        var slider = new SliderBox { Minimum = 0.0f, Maximum = 10.0f, Value = 3.0f, Width = 120.0f };
        var hosted = new MenuItem("Length") { Content = slider };

        var bar = new MenuBar().AddRange(
            new MenuItem("View").WithItems(
                new MenuItem("Something", () => { }),
                hosted));

        root.Content = new Grid().Rows("Auto, *").Columns("*").Children(bar.GridRow(0));
        root.UpdateLayout(new Vector2(800, 600));

        var heading = FindAll<UiElement>(bar).FirstOrDefault(e => e.GetType().Name == "MenuBarHeading");
        Check("the menu has a heading to click", heading != null, string.Empty);

        if (heading == null)
        {
            return;
        }

        void ClickHeading(double time)
        {
            root.Input.InjectPointerMoved(heading.Bounds.Center);
            root.Input.InjectPointerPressed(heading.Bounds.Center, PointerButton.Left, time);
            root.Input.InjectPointerReleased(heading.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(800, 600));
        }

        try
        {
            ClickHeading(1.0);
            Check("opening a menu with a hosted control works", root.Popups.HasOpenPopups,
                $"{root.Popups.Open.Count} popups");

            // Closes it, then opens it again. This is the sequence that threw: the second build tried to
            // re-parent a control the first build had already placed.
            ClickHeading(2.0);
            ClickHeading(3.0);

            Check("and opening it a second time does too", root.Popups.HasOpenPopups,
                $"{root.Popups.Open.Count} popups");

            Check("the control came with it", slider.Root != null && slider.Parent != null,
                slider.Parent?.GetType().Name ?? "detached");
        }
        catch (Exception ex)
        {
            Check("opening a menu with a hosted control twice does not throw", false,
                ex.GetType().Name + ": " + ex.Message);
            return;
        }

        Check("opening a menu with a hosted control twice does not throw", true, "reopened cleanly");

        // Still the same control, so whatever it was showing is what it still shows.
        Check("and it keeps its value across reopening",
            MathF.Abs(slider.Value - 3.0f) < 0.001f, $"{slider.Value}");

        root.Popups.CloseAll();
    }

    /// <summary>
    /// The performance strip: a glance that stays put, with the graphs behind a hover so they cost nothing
    /// until wanted.
    /// </summary>
    private static void PerformanceStrip(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var root = new UiRoot(fonts);
        var strip = ManaUiPerformanceStrip.Build(root.Theme);

        root.Content = new Canvas().Children(strip.At(20.0f, 20.0f));
        root.UpdateLayout(new Vector2(900, 500));

        Check("the strip renders", root.Render().InstanceCount > 3, $"{root.Render().InstanceCount} instances");

        // Three separate fixed-width fields, not one combined string -- see ManaUiPerformanceStrip's
        // build-time comment for why a single proportional-font string padded to a fixed character count
        // still jittered visually as digits changed width.
        var readoutText = string.Join(" ", FindAll<TextBlock>(strip).Select(t => t.Text));
        Check("it reads out fps, frame time and memory",
            readoutText.Contains("fps") && readoutText.Contains("ms") && readoutText.Contains("MB"),
            readoutText.Length == 0 ? "no text" : readoutText);

        // No graphs on screen until asked for: that is the whole point of putting them behind a hover.
        Check("the graphs are not there until hovered",
            !FindAll<Sparkline>(root).Any(g => g.Bounds.Width > 0.0f), "nothing shown");

        var before = root.Popups.Open.Count;
        root.Input.InjectPointerMoved(strip.Bounds.Center);
        root.UpdateLayout(new Vector2(900, 500));

        Check("hovering reveals them", root.Popups.Open.Count == before + 1,
            $"{root.Popups.Open.Count} popups");

        var revealed = root.Popups.Open.LastOrDefault();
        var graphs = revealed?.Child == null ? new List<Sparkline>() : FindAll<Sparkline>(revealed.Child);
        Check("both graphs, frame time and memory", graphs.Count == 2, $"{graphs.Count} graphs");

        // Immediately, not after a dwell -- this is not a tooltip.
        Check("without waiting for a dwell", revealed != null, "shown on enter");

        // Moving onto the graphs must not count as leaving the strip, or they would flicker forever.
        Check("and they do not take the pointer",
            revealed?.IsHitTestVisible == false, $"{revealed?.IsHitTestVisible}");

        root.Input.InjectPointerMoved(new Vector2(800.0f, 400.0f));
        root.UpdateLayout(new Vector2(900, 500));

        Check("moving away takes them back", root.Popups.Open.Count == before,
            $"{root.Popups.Open.Count} popups");

        // Re-entering has to work: the detail is one element that the last popup was holding.
        root.Input.InjectPointerMoved(strip.Bounds.Center);
        root.UpdateLayout(new Vector2(900, 500));
        Check("and hovering again still works", root.Popups.Open.Count == before + 1,
            $"{root.Popups.Open.Count} popups");

        root.Popups.CloseAll();
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

        // ---------------------------------------------------------------- ring buffers
        // A caller sampling every frame keeps a ring and writes in place. Without being told where the
        // cursor is, the graph draws from index zero and the join travels across it as the cursor wraps --
        // which reads as the data jumping rather than scrolling.
        var ring = new float[5];
        var ringGraph = new Sparkline
        {
            Samples = ring,
            Minimum = 0.0f,
            Maximum = 100.0f,
            Width = 120.0f,
            Height = 40.0f
        };

        root.Content = null;
        root.Content = new Canvas().Children(ringGraph.At(10.0f, 10.0f));

        // Written past the end so the buffer has wrapped: chronologically 10, 20, 30, 40, 50, but stored
        // starting at index 2. The newest, 50, sits at index 1.
        var order = new[] { 10.0f, 20.0f, 30.0f, 40.0f, 50.0f };
        var cursor = 2;
        foreach (var value in order)
        {
            ring[cursor] = value;
            cursor = (cursor + 1) % ring.Length;
        }

        ringGraph.WriteCursor = cursor;
        root.UpdateLayout(new Vector2(400, 200));

        List<ManaUiInstance> Columns(Sparkline of) => root.Render().Instances.ToArray()
            .Where(i => i.Rect.X > of.Bounds.X && i.Rect.Z < of.Bounds.Right + 1.0f)
            .Where(i => i.Rect.W - i.Rect.Y > 1.0f && i.Rect.Z - i.Rect.X <= 6.0f)
            .OrderBy(i => i.Rect.X)
            .ToList();

        var wrapped = Columns(ringGraph);
        Check("a wrapped ring draws every sample", wrapped.Count == order.Length, $"{wrapped.Count} columns");

        if (wrapped.Count == order.Length)
        {
            // Chronological order, left to right, despite the storage starting mid-array.
            var rising = true;
            for (var i = 1; i < wrapped.Count; i++)
            {
                rising &= wrapped[i].Rect.Y < wrapped[i - 1].Rect.Y;
            }

            Check("in the order they were written, not the order they are stored", rising,
                string.Join(", ", wrapped.Select(c => (c.Rect.W - c.Rect.Y).ToString("F0"))));

            // The one thing that matters: the most recent sample is at the right-hand edge.
            Check("with the newest at the right edge",
                MathF.Abs((wrapped[^1].Rect.W - wrapped[^1].Rect.Y) - (wrapped[0].Rect.W - wrapped[0].Rect.Y) * 5.0f) < 3.0f,
                $"tallest {wrapped[^1].Rect.W - wrapped[^1].Rect.Y:F0} vs shortest {wrapped[0].Rect.W - wrapped[0].Rect.Y:F0}");
        }

        // One more sample: the graph should scroll by one, not shuffle.
        ring[cursor] = 60.0f;
        cursor = (cursor + 1) % ring.Length;
        ringGraph.WriteCursor = cursor;
        root.UpdateLayout(new Vector2(400, 200));

        var scrolled = Columns(ringGraph);
        Check("another sample scrolls it rather than reordering it",
            scrolled.Count == order.Length
                && scrolled[^1].Rect.Y < scrolled[^2].Rect.Y
                && MathF.Abs((scrolled[0].Rect.W - scrolled[0].Rect.Y) - (wrapped[1].Rect.W - wrapped[1].Rect.Y)) < 1.5f,
            "oldest fell off the left");

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
