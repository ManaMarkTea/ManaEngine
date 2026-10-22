using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class LevelLibraryChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ManaScenes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pictures = Path.Combine(folder, "Pictures");
            Directory.CreateDirectory(pictures);
            var level = Path.Combine(folder, "Little Pond.manalevel");
            File.WriteAllText(level, "{}");
            var nestedFolder = Path.Combine(folder, "Tests", "Water");
            Directory.CreateDirectory(nestedFolder);
            var nested = Path.Combine(nestedFolder, "Little Pond.manalevel");
            File.WriteAllText(nested, "{}");
            var siblingFolder = Path.Combine(folder, "Tests Extra");
            Directory.CreateDirectory(siblingFolder);
            var sibling = Path.Combine(siblingFolder, "Other.manalevel");
            File.WriteAllText(sibling, "{}");
            var discovered = ManaLevelLibrary.List(folder);
            check("level discovery includes arbitrarily nested folders", discovered.Count == 3 && discovered.Contains(nested), "");
            var hierarchy = ManaLevelFolder.Build(discovered, folder);
            var testsFolder = hierarchy.Find("Tests")!;
            check("folder selection includes descendants and excludes similarly named siblings",
                testsFolder.DescendantLevels().SequenceEqual(new[] { nested }), "");
            check("root scenes and duplicate scene names keep their distinct paths", hierarchy.Levels.Single() == level
                && hierarchy.DescendantLevels().Count() == 3, "");
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        // A project laid out as the repo: Content/Levels, and a game's own Games/<Name>/Content/Levels beside it.
        var project = Path.Combine(Path.GetTempPath(), "ManaProject-" + Guid.NewGuid().ToString("N"));
        try
        {
            var main = Path.Combine(project, "Content", "Levels");
            var game = Path.Combine(project, "Games", "Demo", "Content", "Levels");
            Directory.CreateDirectory(main);
            Directory.CreateDirectory(game);
            var own = Path.Combine(main, "Course.manalevel");
            var games = Path.Combine(game, "Arena.manalevel");
            File.WriteAllText(own, "{}");
            File.WriteAllText(games, "{}");
            var listed = ManaLevelLibrary.List(main);
            check("a game's own levels are listed with the main folder's", listed.Count == 2 && listed.Contains(games), string.Join(", ", listed));
            var tree = ManaLevelFolder.Build(listed, main);
            check("and come in under Games/<Name>", tree.Find(Path.Combine("Games", "Demo"))?.Levels.SequenceEqual(new[] { games }) == true
                && tree.Levels.SequenceEqual(new[] { own }), "");
        }
        finally
        {
            Directory.Delete(project, true);
        }
    }
}
