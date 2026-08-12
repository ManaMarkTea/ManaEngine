using ManaTests;
using ManaUI;

// Headless test suite for ManaRenderer. One project for every system, so there is a single command to run
// and a single place to add coverage; suites are grouped by system and live in Code/<System>/.
//
// A plain console runner rather than a test framework: it adds no dependencies, and `dotnet run` returning
// non-zero is everything a CI step needs. Every check is an independent named assertion, so moving to
// xUnit later is mechanical.
//
// Nothing here needs a GPU or a window. For ManaUI that is by construction -- it has no graphics
// dependency -- which is what makes layout, input, binding and docking testable at all.

var failures = 0;
var total = 0;

void Check(string what, bool ok, string detail = "")
{
    total++;
    Console.WriteLine($"{(ok ? "  ok  " : " FAIL ")} {what}{(detail.Length > 0 ? "  -> " + detail : "")}");
    if (!ok)
    {
        failures++;
    }
}

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

// Add a system by appending its suites here; each one owns whatever setup it needs.
var suites = new (string System, string Section, Action Run)[]
{
    ("ManaUI", "layout, text and draw list", () => LayoutChecks.Run(fonts, Check)),
    ("ManaUI", "input", () => InputChecks.Run(fonts, Check)),
    ("ManaUI", "bindings", () => BindingChecks.Run(fonts, Check)),
    ("ManaUI", "scrolling and text editing", () => ScrollAndTextChecks.Run(fonts, Check)),
    ("ManaUI", "floating windows", () => WindowChecks.Run(fonts, Check)),
    ("ManaUI", "docking", () => DockingChecks.Run(fonts, Check)),
    ("ManaUI", "drag docking", () => DragDockingChecks.Run(fonts, Check)),
    ("ManaUI", "layout persistence", () => PersistenceChecks.Run(fonts, Check)),
    ("ManaUI", "real editor views", () => ShowcaseChecks.Run(fonts, Check))
};

// Optional filter: `dotnet run --project ManaTests -- ManaUI` runs one system's suites.
// Flags are ignored rather than treated as a filter, because the dotnet CLI forwards some of its own
// (--nologo among them) straight through to the app.
var filter = args.FirstOrDefault(arg => !arg.StartsWith('-'));
var ran = 0;

foreach (var suite in suites)
{
    if (filter != null && !suite.System.Contains(filter, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    ran++;
    Console.WriteLine();
    Console.WriteLine($"== {suite.System}: {suite.Section} ==");

    try
    {
        suite.Run();
    }
    catch (Exception ex)
    {
        // A suite that throws must not hide the suites after it.
        Check($"{suite.System}/{suite.Section} completed without throwing", false, ex.ToString());
    }
}

Console.WriteLine();

if (ran == 0)
{
    Console.WriteLine($"No suites matched '{filter}'.");
    return 2;
}

Console.WriteLine(failures == 0
    ? $"ALL {total} CHECKS PASSED"
    : $"{failures} of {total} CHECK(S) FAILED");

return failures == 0 ? 0 : 1;
