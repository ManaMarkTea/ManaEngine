using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// The start screen's list of levels (the phone app, --play-menu): only the published ones, in their order, each with
/// its picture -- the same set the web selector offers -- and a level that is not published or not a level is left out.
/// </summary>
public static class PlayableLevelsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var folder = FindLevels();
        var levels = ManaPlayableLevels.List(folder);
        var names = levels.Select(level => level.Name).ToList();
        check("published levels are listed", levels.Count >= 5, string.Join(", ", names));
        check("the Sample Course comes first (order 1)", names.FirstOrDefault() == "Sample Course", string.Join(", ", names));
        check("an unpublished level is left out (Planet Showcase)", !names.Contains("Planet Showcase"), string.Join(", ", names));
        check("ordered levels come before unordered ones",
            levels.SkipWhile(level => level.Order != int.MaxValue).All(level => level.Order == int.MaxValue), string.Join(", ", levels.Select(l => $"{l.Name}={l.Order}")));
        var course = levels.First(level => level.Name == "Sample Course");
        check("a level's picture is found beside it", course.Picture != null && File.Exists(course.Picture), course.Picture ?? "none");
        check("its description comes from the web block", !string.IsNullOrWhiteSpace(course.Description), course.Description ?? "none");

        // A scratch folder: one published, one not, one broken file.
        var scratch = Directory.CreateTempSubdirectory("mana-playable-");
        try
        {
            File.WriteAllText(Path.Combine(scratch.FullName, "B.manalevel"), """{ "web": { "publish": true, "order": 2 } }""");
            File.WriteAllText(Path.Combine(scratch.FullName, "A.manalevel"), """{ "web": { "publish": true, "order": 1, "description": "first" } }""");
            File.WriteAllText(Path.Combine(scratch.FullName, "Hidden.manalevel"), """{ "web": { "publish": false } }""");
            File.WriteAllText(Path.Combine(scratch.FullName, "Broken.manalevel"), "{ not json");
            var listed = ManaPlayableLevels.List(scratch.FullName);
            check("a scratch folder lists its published levels in order, skipping hidden and broken ones",
                listed.Select(level => level.Name).SequenceEqual(new[] { "A", "B" }), string.Join(", ", listed.Select(level => level.Name)));
            check("no folder is no levels", ManaPlayableLevels.List(Path.Combine(scratch.FullName, "missing")).Count == 0, "");
        }
        finally
        {
            scratch.Delete(true);
        }
    }

    private static string FindLevels()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Content", "Levels");
            if (File.Exists(Path.Combine(path, "Sample Course.manalevel")))
            {
                return path;
            }
        }
        throw new DirectoryNotFoundException("Content/Levels");
    }
}
