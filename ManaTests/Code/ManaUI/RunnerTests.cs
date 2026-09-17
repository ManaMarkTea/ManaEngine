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

        // ---------------------------------------------------------------- clipboard isolation
        // The text box checks copy, paste and cut, and TextBox.Clipboard is a static. Harmless while it is
        // an in-memory one; not harmless once the GUI runner has replaced it with the platform's, which is
        // how running the suite replaced whatever the user had copied with the leftovers of a cut test.
        // Asserted from inside a run, which is the only place it can be: the question is not whether the
        // isolation works when called, but whether the runner calls it.
        Check("a run never holds the platform clipboard", TextBox.Clipboard is UiMemoryClipboard,
            TextBox.Clipboard.GetType().Name);

        var platform = new RecordingClipboard { Stored = "something the user copied" };
        var hostClipboard = TextBox.Clipboard;
        TextBox.Clipboard = platform;

        try
        {
            using (ClipboardIsolation.Begin())
            {
                Check("isolation swaps the platform clipboard out",
                    TextBox.Clipboard is UiMemoryClipboard, TextBox.Clipboard.GetType().Name);

                TextBox.Clipboard.Text = "SeaSea";
                Check("a check writing the clipboard stays in process",
                    TextBox.Clipboard.Text == "SeaSea" && platform.Writes == 0,
                    $"{platform.Writes} writes reached the platform");
            }

            Check("the platform clipboard comes back", ReferenceEquals(TextBox.Clipboard, platform),
                TextBox.Clipboard.GetType().Name);

            // Never touched, not merely left holding the same value. Isolation used to save the contents
            // and write them back, which looked harmless while the clipboard held text -- it put the same
            // string back. But the value it round-tripped was text, and a clipboard holding an image reads
            // as empty, so the write emptied it. That is what was erasing screenshots.
            Check("and the user's clipboard was neither read nor written",
                platform.Stored == "something the user copied" && platform.Reads == 0
                && platform.Writes == 0,
                $"{platform.Reads} reads, {platform.Writes} writes");
        }
        finally
        {
            TextBox.Clipboard = hostClipboard;
        }

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

        var totalChecks = populated.Sections.Sum(s => s.Checks.Count);
        Check("a run reports its checks", populated.PassedCount + populated.FailedCount == totalChecks,
            $"{populated.PassedCount} passed, {populated.FailedCount} failed, {totalChecks} total");
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
    /// Stands in for the platform clipboard, counting access rather than only holding a value. Isolation
    /// is meant to leave it strictly alone, and "unchanged" is a weaker claim than "untouched" -- a
    /// round-trip that writes the same string back would satisfy the first and not the second.
    /// </summary>
    private sealed class RecordingClipboard : IUiClipboard
    {
        /// <summary>Read directly by checks, so asserting does not count as access.</summary>
        public string Stored = string.Empty;

        public int Reads;
        public int Writes;

        public string Text
        {
            get
            {
                Reads++;
                return Stored;
            }

            set
            {
                Writes++;
                Stored = value;
            }
        }
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
