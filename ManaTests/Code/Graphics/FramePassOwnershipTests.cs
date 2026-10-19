using System.Text.RegularExpressions;
using ManaUI;

namespace ManaTests;

// Pass encoders are frame-local native handles no context disposal can recover, so each one begun
// must be released before its variable is begun again (a split pass) or goes out of scope.
public static class FramePassOwnershipChecks
{
    private static readonly Regex Begin = new(@"\b(\w+)\s*=\s*_?[Aa]pi\.CommandEncoderBegin(Render|Compute)Pass\(", RegexOptions.Compiled);

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var directory = FindRendererSource();
        var begun = 0;
        var unreleased = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var matches = Begin.Matches(source);
            foreach (Match match in matches)
            {
                begun++;
                var name = match.Groups[1].Value;
                var kind = match.Groups[2].Value;
                var next = matches.FirstOrDefault(other => other.Index > match.Index && other.Groups[1].Value == name);
                var end = next?.Index ?? source.Length;
                var release = new Regex($@"{kind}PassEncoderRelease\(\s*{Regex.Escape(name)}\s*\)");
                if (!release.IsMatch(source[match.Index..end]))
                {
                    var line = source[..match.Index].Count(c => c == '\n') + 1;
                    unreleased.Add($"{Path.GetFileName(file)}:{line} {name}");
                }
            }
        }

        check("renderer source has pass encoders to check", begun >= 15, $"{begun} found in {directory}");
        check("every begun pass encoder is released before reuse or scope end", unreleased.Count == 0, string.Join(", ", unreleased));
    }

    private static string FindRendererSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "ManaGraphics", "Code", "Graphics", "WebGPU");
            if (Directory.Exists(path)) { return path; }
        }
        throw new DirectoryNotFoundException("ManaGraphics/Code/Graphics/WebGPU");
    }
}
