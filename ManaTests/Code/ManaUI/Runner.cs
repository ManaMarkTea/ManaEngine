using ManaUI;

namespace ManaTests;

/// <summary>
/// The GUI runner's own view, exercised headlessly like any other panel.
/// <para>
/// Slightly odd to have the suite test the thing that displays the suite, but it is the same argument as
/// everywhere else here: the runner is written the way an editor panel is written, so if it lays out and
/// binds correctly then so does the framework underneath it. It is also the widest view in the repo -- a
/// few hundred rows of real data -- which makes it the best available check that ItemsControl and
/// Expander hold up at scale.
/// </para>
/// </summary>
public static class RunnerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);

        // Fed a synthetic run rather than executing anything. Running the suite from here would run this
        // check again, and again -- every suite carries the same system name, so no filter excludes only
        // this one. It also lets the data contain a failure, which the real suite never does.
        var vm = new TestRunnerViewModel(fonts, null);
        vm.Load(new TestRun());

        var view = TestRunnerView.Build(root.Theme, vm);
        root.Content = view;

        // The window the runner opens: tall and narrow, which is the shape a list of check names wants.
        root.UpdateLayout(new Vector2(760, 940));

        Check("the runner view builds", view != null);
        Check("it renders", root.Render().InstanceCount > 10, $"{root.Render().InstanceCount} instances");
        Check("still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // ---------------------------------------------------------------- fits the narrow window
        // Everything has to fit the column without a horizontal scroll bar, which is the failure the old
        // fixed-width search box produced when the window shrank.
        var scrollers = FindAll<ScrollViewer>(view);
        Check("the results scroll", scrollers.Count > 0, $"{scrollers.Count} viewers");

        foreach (var scroller in scrollers)
        {
            Check("nothing overflows horizontally", !scroller.CanScrollHorizontally,
                $"extent {scroller.Extent.X:F0} in viewport {scroller.Viewport.X:F0}");
        }

        var overflowing = FindAll<UiElement>(view)
            .Count(e => !e.Bounds.IsEmpty && e.Bounds.Right > 760.5f);
        Check("no element runs past the window", overflowing == 0, $"{overflowing} elements");

        // ---------------------------------------------------------------- with results
        var populated = new TestRunnerViewModel(fonts, null);
        populated.Load(SampleRun());

        Check("a run reports its checks", populated.PassedCount + populated.FailedCount == 130,
            $"{populated.PassedCount} passed, {populated.FailedCount} failed");
        Check("the headline reflects the outcome",
            populated.Headline.Length > 0, populated.Headline);
        Check("the banner icon follows the outcome",
            populated.StatusIcon == (populated.AllPassed ? "\U0001F389" : "\U0001F4A5"),
            populated.StatusIcon);

        var populatedView = TestRunnerView.Build(root.Theme, populated);
        root.Content = null;
        root.Content = populatedView;
        root.UpdateLayout(new Vector2(760, 940));

        var sections = FindAll<Expander>(populatedView);
        Check("each suite is a collapsible section", sections.Count == populated.Sections.Count,
            $"{sections.Count} expanders for {populated.Sections.Count} suites");

        // Passing suites come up closed. At this many checks that is the difference between a screen of
        // results and a screen of scrolling.
        Check("a passing suite starts collapsed",
            populated.Sections.All(s => s.HasFailures || !s.StartsExpanded),
            $"{populated.Sections.Count(s => s.StartsExpanded)} start open");

        Check("a failing suite would start open",
            populated.Sections.All(s => !s.HasFailures || s.StartsExpanded),
            "failures are visible without hunting");

        // Rows carry an emoji rather than a colour bar, so the status survives a screenshot in greyscale.
        Check("a passing check is ticked", populated.Sections
            .SelectMany(s => s.Checks)
            .All(c => c.Icon == (c.Passed ? "✅" : "❌")), "icons match outcomes");

        Check("the populated view renders", root.Render().InstanceCount > 50,
            $"{root.Render().InstanceCount} instances");
        Check("and is still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // ---------------------------------------------------------------- filtering
        var before = populated.Sections.Sum(s => s.Checks.Count);
        populated.FailuresOnly = true;
        var after = populated.Sections.Sum(s => s.Checks.Count);

        Check("failures-only filters the list", after < before, $"{before} -> {after} checks");

        populated.FailuresOnly = false;
        populated.Search = "check 3";
        Check("search narrows the list",
            populated.Sections.Sum(s => s.Checks.Count) < before,
            $"{populated.Sections.Sum(s => s.Checks.Count)} matching");

        populated.Search = "zzz-no-such-check";
        Check("a search matching nothing empties it", populated.Sections.Count == 0,
            $"{populated.Sections.Count} sections");

        root.Content = null;
        root.Content = TestRunnerView.Build(root.Theme, populated);
        root.UpdateLayout(new Vector2(760, 940));
        Check("the empty state still renders", root.Render().InstanceCount > 5,
            $"{root.Render().InstanceCount} instances");
    }

    /// <summary>
    /// A run with a mix of outcomes: several passing suites and one that fails, which is the arrangement
    /// the view is designed around -- passing suites collapsed, the failing one open.
    /// </summary>
    private static TestRun SampleRun()
    {
        var run = new TestRun();

        for (var s = 0; s < 5; s++)
        {
            var section = new TestSection("ManaUI", $"sample suite {s}");
            var fails = s == 3;

            for (var c = 0; c < 26; c++)
            {
                var passed = !fails || c % 13 != 0;
                section.Checks.Add(new TestCheck($"check {c} in suite {s}", passed, passed ? "" : "expected 1, got 2"));
            }

            section.Duration = TimeSpan.FromMilliseconds(10 + s);
            run.Sections.Add(section);
        }

        return run;
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
