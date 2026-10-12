using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManaUI;

namespace ManaTests;

/// <summary>A section as the GUI shows it: a header row plus its checks, filtered.</summary>
public partial class TestSectionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasFailures;

    [ObservableProperty]
    private int _failed;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private double _milliseconds;

    /// <summary>Open when something in it failed. A passing suite is one line until you ask for more.</summary>
    public bool StartsExpanded => HasFailures;

    public string Icon => HasFailures ? "❌" : "✅";

    public ObservableCollection<TestCheckViewModel> Checks { get; } = new();
}

public partial class TestCheckViewModel : ObservableObject
{
    public TestCheckViewModel(TestCheck check)
    {
        Name = check.Name;
        Passed = check.Passed;
        Detail = check.Detail;
    }

    public string Name { get; }
    public bool Passed { get; }
    public string Detail { get; }

    public string Label => Detail.Length > 0 ? $"{Name}  —  {Detail}" : Name;

    public string Icon => Passed ? "✅" : "❌";
}

/// <summary>
/// Drives the GUI test runner. Holds no UI types, exactly like any other view model -- which is the point:
/// the runner is built the way an editor panel would be, so it exercises the framework the same way.
/// </summary>
public partial class TestRunnerViewModel : ObservableObject
{
    private readonly ManaFontManager _fonts;
    private readonly string? _systemFilter;
    private TestRun? _lastRun;

    public TestRunnerViewModel(ManaFontManager fonts, string? systemFilter)
    {
        _fonts = fonts;
        _systemFilter = systemFilter;
    }

    [ObservableProperty]
    private string _summary = "Not run yet";

    [ObservableProperty]
    private string _timing = string.Empty;

    [ObservableProperty]
    private bool _allPassed = true;

    [ObservableProperty]
    private bool _failuresOnly;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private int _passedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private string _headline = "Not run yet";

    /// <summary>The banner emoji, which is the one thing readable from across the room.</summary>
    public string StatusIcon => AllPassed ? "🎉" : "💥";

    partial void OnAllPassedChanged(bool value) => OnPropertyChanged(nameof(StatusIcon));

    public ObservableCollection<TestSectionViewModel> Sections { get; } = new();

    partial void OnFailuresOnlyChanged(bool value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    [ObservableProperty]
    private bool _isRunning;

    // Written by the background run, read by Pump on the UI thread.
    private TestRun? _finished;
    private volatile string? _progress;
    private int _running;

    /// <summary>
    /// Starts a run in the background. The full suite takes minutes now, and running it before the window opened
    /// left nothing on screen for all that time. The suites never touch these view models; <see cref="Pump"/>
    /// applies what they report on the UI thread.
    /// </summary>
    [RelayCommand]
    public void RunTests()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        var total = TestRunner.Catalog.Count(s => _systemFilter == null
            || s.System.Contains(_systemFilter, StringComparison.OrdinalIgnoreCase));
        var started = 0;
        IsRunning = true;
        Headline = "Running...";
        Summary = $"0 of {total} suites";
        Task.Run(() =>
        {
            TestRun run;
            try
            {
                run = TestRunner.Run(_fonts, _systemFilter,
                    onSectionStarted: section => _progress = $"{Interlocked.Increment(ref started)} of {total} suites  ·  {section.Title}");
            }
            catch (Exception ex)
            {
                run = new TestRun();
                var section = new TestSection("ManaTests", "runner");
                section.Checks.Add(new TestCheck("the run completed without throwing", false, ex.ToString()));
                run.Sections.Add(section);
            }
            Volatile.Write(ref _finished, run);
        });
    }

    /// <summary>Called every frame on the UI thread: shows progress, then the results once the run is done.</summary>
    public void Pump()
    {
        if (_progress is { } progress)
        {
            _progress = null;
            Summary = progress;
        }
        if (Interlocked.Exchange(ref _finished, null) is { } run)
        {
            Load(run);
            IsRunning = false;
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// Shows a run this view model did not perform.
    /// <para>
    /// Separate from <see cref="RunTests"/> so the view can be exercised against results without executing
    /// anything -- which the suite's own coverage of this view needs, since a check that ran the suite
    /// would run itself.
    /// </para>
    /// </summary>
    public void Load(TestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _lastRun = run;

        AllPassed = _lastRun.Success;
        PassedCount = _lastRun.Passed;
        FailedCount = _lastRun.Failed;

        Headline = _lastRun.Success ? "All checks passed" : $"{_lastRun.Failed} failed";
        Summary = $"{_lastRun.Total} checks in {_lastRun.Sections.Count} suites";
        Timing = $"{_lastRun.Duration.TotalMilliseconds:F0} ms";

        Rebuild();
    }

    /// <summary>Rebuilds the visible rows from the last run, applying the search and failures-only filters.</summary>
    private void Rebuild()
    {
        Sections.Clear();

        if (_lastRun == null)
        {
            return;
        }

        foreach (var section in _lastRun.Sections)
        {
            var visible = section.Checks.Where(Matches).ToList();
            if (visible.Count == 0)
            {
                continue;
            }

            var model = new TestSectionViewModel
            {
                Header = section.Title,
                Summary = section.HasFailures
                    ? $"{section.Failed} of {section.Checks.Count} failed"
                    : $"{section.Checks.Count} · {section.Duration.TotalMilliseconds:F0} ms",
                HasFailures = section.HasFailures,
                Failed = section.Failed,
                Total = section.Checks.Count,
                Milliseconds = section.Duration.TotalMilliseconds
            };

            foreach (var check in visible)
            {
                model.Checks.Add(new TestCheckViewModel(check));
            }

            Sections.Add(model);
        }
    }

    private bool Matches(TestCheck check)
    {
        if (FailuresOnly && check.Passed)
        {
            return false;
        }

        if (Search.Length == 0)
        {
            return true;
        }

        return check.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || check.Detail.Contains(Search, StringComparison.OrdinalIgnoreCase);
    }
}
