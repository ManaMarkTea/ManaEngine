using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaTests;

public static class TextureLibraryChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        Check("the texture library scans the project", ManaUiTextureLibrary.Assets.Count > 0,
            $"{ManaUiTextureLibrary.Assets.Count} textures under {Directory.GetCurrentDirectory()}");
        Check("and stores relative paths",
            ManaUiTextureLibrary.Assets.All(a => !Path.IsPathRooted(a.RelativePath)),
            "portable across machines");

        Check("texture picker excludes icons and model images", ManaUiTextureLibrary.Assets.All(asset =>
            !asset.RelativePath.Contains("/Icons/", StringComparison.OrdinalIgnoreCase)
            && !asset.RelativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase)));
    }
}
