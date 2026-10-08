using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

public static class ScenesChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        string[] paths = ["Levels/Little Pond.manalevel", "Levels/Little Train Yard.manalevel", "Levels/2D Sample.manalevel"];
        check("blank search preserves library order", ScenesManaView.Filter(paths, "  ").SequenceEqual(paths), "");
        check("search matches all words regardless of case or order",
            ScenesManaView.Filter(paths, " YARD little ").SequenceEqual(new[] { paths[1] }), "");
        check("search excludes folder and extension names", ScenesManaView.Filter(paths, "Levels").Count == 0
            && ScenesManaView.Filter(paths, "manalevel").Count == 0, "");
        check("unmatched search is empty", ScenesManaView.Filter(paths, "missing").Count == 0, "");

        var folder = Path.Combine(Path.GetTempPath(), "ManaScenes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pictures = Path.Combine(folder, "Pictures");
            Directory.CreateDirectory(pictures);
            var level = Path.Combine(folder, "Little Pond.manalevel");
            check("missing preview uses placeholder", ScenesManaView.PreviewPath(level) == null, "");
            var jpg = Path.Combine(pictures, "Little Pond.jpg");
            File.WriteAllBytes(jpg, []);
            check("authored JPEG is found beside the level", ScenesManaView.PreviewPath(level) == jpg, "");
            var png = Path.Combine(pictures, "Little Pond.png");
            File.WriteAllBytes(png, []);
            check("PNG takes precedence over JPEG", ScenesManaView.PreviewPath(level) == png, "");
            check("another scene never borrows this preview",
                ScenesManaView.PreviewPath(Path.Combine(folder, "Other.manalevel")) == null, "");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
