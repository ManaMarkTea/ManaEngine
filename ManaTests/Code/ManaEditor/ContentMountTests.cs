using Mana.Code.Content;
using ManaUI;
using ManaEditor.Code.Assets;

namespace ManaTests;

/// <summary>Each game's Games/&lt;Name&gt;/Content folder is content too, written @Name/... in references.</summary>
public static class ContentMountChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var scratch = ManaProjectFolder.CanonicalPath(Path.Combine(Path.GetTempPath(), "mana-mount-tests-" + Guid.NewGuid().ToString("N")));
        var content = Path.Combine(scratch, "Content");
        var game = Path.Combine(scratch, "Games", "Mill", "Content");
        Directory.CreateDirectory(Path.Combine(content, "Prefabs"));
        Directory.CreateDirectory(Path.Combine(game, "Prefabs"));
        Directory.CreateDirectory(Path.Combine(scratch, "Games", "NoContent"));
        try
        {
            var shared = Path.Combine(content, "Prefabs", "Lamp.manaprefab");
            var wheel = Path.Combine(game, "Prefabs", "Wheel.manaprefab");
            File.WriteAllText(shared, "{\"format\":\"mana-prefab\",\"version\":1,\"id\":\"aaaa\",\"name\":\"Lamp\"}");
            File.WriteAllText(wheel, "{\"format\":\"mana-prefab\",\"version\":1,\"id\":\"bbbb\",\"name\":\"Wheel\"}");

            var mounts = ManaContentMounts.For(content);
            check("a game with a Content folder is mounted, one without is not",
                mounts.Count == 2 && mounts[0].IsMain && mounts[1].Name == "Mill", string.Join(", ", mounts.Select(m => m.Name)));
            check("@Name paths map into the game's folder", ManaContentMounts.FullPath(content, "@Mill/Prefabs/Wheel.manaprefab") == wheel, "");
            check("other paths stay in the main folder", ManaContentMounts.FullPath(content, "Prefabs/Lamp.manaprefab") == shared, "");
            check("an unknown mount maps nowhere", ManaContentMounts.FullPath(content, "@Nope/x.manaprefab") == null, "");
            check("a game's file reads back as @Name/...", ManaContentLibrary.Reference(wheel, content) == "@Mill/Prefabs/Wheel.manaprefab", ManaContentLibrary.Reference(wheel, content));
            check("a main file reads back as before", ManaContentLibrary.Reference(shared, content) == "Prefabs/Lamp.manaprefab", "");

            var listed = ManaContentLibrary.List(content);
            check("the library lists both folders", listed.Any(e => e.Path == wheel && e.RelativePath.StartsWith("@Mill")) && listed.Any(e => e.Path == shared), "");
            check("prefabs resolve by @Name path", ManaContentLibrary.ResolvePrefab("bbbb", "@Mill/Prefabs/Wheel.manaprefab", content) == wheel, "");
            check("prefabs resolve by id when moved", ManaContentLibrary.ResolvePrefab("bbbb", "Prefabs/Gone.manaprefab", content) == wheel, "");
            var escaped = false;
            try
            {
                ManaContentLibrary.ResolvePrefab("bbbb", "@Mill/../../../outside.manaprefab", content);
            }
            catch (InvalidDataException)
            {
                escaped = true;
            }
            check("a path climbing out of a mount is refused", escaped, "");
            check("new assets can be made in a game's folder", ManaContentLibrary.NewPath("Gear", "@Mill/Prefabs", ".manaprefab", content) == Path.Combine(game, "Prefabs", "Gear.manaprefab"), "");
            check("engine assets resolve by id across mounts",
                ManaContentAssets.Resolve(new ManaAssetRef("bbbb", "Prefabs/Wheel.manaprefab"), ".manaprefab", content) == wheel, "");
            check("engine assets resolve by @Name path",
                ManaContentAssets.Resolve(new ManaAssetRef("", "@Mill/Prefabs/Wheel.manaprefab"), ".manaprefab", content) == wheel, "");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }
}
