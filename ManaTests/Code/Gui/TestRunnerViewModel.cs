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

    public ObservableCollection<TestSectionViewModel> Sections { get; } = new();

    partial void OnFailuresOnlyChanged(bool value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    [RelayCommand]
    public void RunTests()
    {
        // Synchronous on purpose. The whole suite is well under a second, and running it off-thread would
        // mean the view models it mutates were being touched from somewhere other than the UI thread.
        _lastRun = TestRunner.Run(_fonts, _systemFilter);

        AllPassed = _lastRun.Success;
        Summary = _lastRun.Success
            ? $"All {_lastRun.Total} checks passed"
            : $"{_lastRun.Failed} of {_lastRun.Total} checks failed";
        Timing = $"{_lastRun.Sections.Count} suites in {_lastRun.Duration.TotalMilliseconds:F0} ms";

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
                    ? $"{section.Failed} failed / {section.Checks.Count}"
                    : $"{section.Checks.Count} passed  ·  {section.Duration.TotalMilliseconds:F0} ms",
                HasFailures = section.HasFailures
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
