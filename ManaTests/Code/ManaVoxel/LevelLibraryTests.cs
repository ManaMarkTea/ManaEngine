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
    }
}
