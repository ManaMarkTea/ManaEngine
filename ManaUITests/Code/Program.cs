using ManaUI;
using ManaUITests;

// Headless test suite for ManaUI. A plain console runner rather than a test framework: the repo has no
// test infrastructure to fit into, this needs no dependencies, and `dotnet run` returning non-zero is
// everything a CI step needs. Converting to xUnit later is mechanical -- every check is already an
// independent assertion with a name.
//
// The point of ManaUI having no graphics dependency is that all of this runs without a GPU or a window:
// layout, input routing, binding, docking and draw-list recording are all verified here.

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

var sections = new (string Name, Action<ManaFontManager, Action<string, bool, string>> Run)[]
{
    ("layout, text and draw list", LayoutChecks.Run),
    ("input", InputChecks.Run),
    ("bindings", BindingChecks.Run),
    ("scrolling and text editing", ScrollAndTextChecks.Run),
    ("floating windows", WindowChecks.Run),
    ("docking", DockingChecks.Run),
    ("drag docking", DragDockingChecks.Run),
    ("layout persistence", PersistenceChecks.Run),
    ("real editor views", ShowcaseChecks.Run)
};

foreach (var section in sections)
{
    Console.WriteLine();
    Console.WriteLine($"== {section.Name} ==");

    try
    {
        section.Run(fonts, Check);
    }
    catch (Exception ex)
    {
        // A section that throws must not hide the sections after it.
        Check($"{section.Name} completed without throwing", false, ex.ToString());
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"ALL {total} CHECKS PASSED"
    : $"{failures} of {total} CHECK(S) FAILED");

return failures == 0 ? 0 : 1;

/// <summary>Finds the repo's bundled fonts, then the platform's, so the suite runs from anywhere.</summary>
internal static class TestFonts
{
    public static IEnumerable<string> SearchDirectories()
    {
        // Walk up from the binary looking for the repo's asset folder, so the suite does not depend on
        // the working directory or on assets being copied next to the test binary.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var bundled = Path.Combine(directory.FullName, "Mana", "Assets", "Fonts");
            if (Directory.Exists(bundled))
            {
                yield return bundled;
                break;
            }

            directory = directory.Parent;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/System/Library/Fonts/Supplemental";
        }
        else
        {
            yield return "/usr/share/fonts/truetype";
        }
    }
}
