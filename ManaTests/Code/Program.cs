using ManaTests;
using ManaUI;

if (args.Contains("--popout-smoke")) return DesktopWindowSmoke.Run();
if (args.Contains("--audio-smoke")) return AudioDeviceSmoke.Run();

// Headless test suite for ManaRenderer. One project for every system, so there is a single command to run
// and a single place to add coverage; suites are grouped by system and live in Code/<System>/.
//
// A plain console runner rather than a test framework: it adds no dependencies, and `dotnet run` returning
// non-zero is everything a CI step needs. Every check is an independent named assertion, so moving to
// xUnit later is mechanical.
//
//   dotnet run --project ManaTests              all suites, console
//   dotnet run --project ManaTests -- ManaUI    one system
//   dotnet run --project ManaTests -- -gui      results in a window, drawn with ManaUI itself
//
// Nothing here needs a GPU or a window except -gui. For ManaUI that is by construction -- it has no
// graphics dependency -- which is what makes layout, input, binding and docking testable at all.

var useGui = args.Any(arg => arg.Equals("-gui", StringComparison.OrdinalIgnoreCase)
    || arg.Equals("--gui", StringComparison.OrdinalIgnoreCase));

// Flags are ignored when matching the system filter, because the dotnet CLI forwards some of its own
// (--nologo among them) straight through to the app.
var filter = args.FirstOrDefault(arg => !arg.StartsWith('-'));

var fonts = new ManaFontManager();
foreach (var directory in TestFonts.SearchDirectories())
{
    fonts.AddSearchDirectory(directory);
}

try
{
    fonts.Get("Roboto", 15.0f);
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL: no usable font found. {ex.Message}");
    Console.WriteLine("Searched: " + string.Join("; ", fonts.SearchDirectories));
    return 2;
}

if (useGui)
{
    return TestRunnerWindow.Show(fonts, filter);
}

TestSection? current = null;

var run = TestRunner.Run(
    fonts,
    filter,
    onCheck: (section, check) =>
    {
        Console.WriteLine(
            $"{(check.Passed ? "  ok  " : " FAIL ")} {check.Name}"
            + (check.Detail.Length > 0 ? $"  -> {check.Detail}" : string.Empty));
    },
    onSectionStarted: section =>
    {
        current = section;
        Console.WriteLine();
        Console.WriteLine($"== {section.Title} ==");
    });

Console.WriteLine();

if (run.Total == 0)
{
    Console.WriteLine($"No suites matched '{filter}'.");
    return 2;
}

Console.WriteLine(run.Success
    ? $"ALL {run.Total} CHECKS PASSED in {run.Duration.TotalSeconds:F1}s"
    : $"{run.Failed} of {run.Total} CHECK(S) FAILED");

return run.Success ? 0 : 1;
