namespace ManaTests;

/// <summary>Finds the repo's bundled fonts, then the platform's, so the suite runs from anywhere.</summary>
public static class TestFonts
{
    public static IEnumerable<string> SearchDirectories()
    {
        // Walks up from the binary looking for the repo's asset folder, so the suite does not depend on
        // the working directory or on assets being copied next to the test binary.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var bundled = Path.Combine(directory.FullName, "Mana", "Assets", "Fonts");
            if (Directory.Exists(bundled))
            {
                yield return bundled;

                // ManaUI ships its own fonts -- the colour emoji strike, which has no substitute on a
                // machine without one.
                var library = Path.Combine(directory.FullName, "ManaUI", "Assets", "Fonts");
                if (Directory.Exists(library))
                {
                    yield return library;
                }

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
