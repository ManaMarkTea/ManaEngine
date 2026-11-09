using System.Text.Json;
using Mana.Code.Content;
using ManaUI;
using ManaEditor.Code.Assets;
using ManaEditor.Code.Levels;

namespace ManaTests;

public static class SpriteAssetDependencyChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, options);
        var picture = "zip:art.zip/Sprites/hero \"blue\" 雪.png";
        var script = new ManaLevelScript
        {
            Type = "Sprite", Properties = new()
            {
                ["TEXTURE"] = Json(picture), ["normalMap"] = Json("Content/Sprites/hero_n.png"),
                ["notes"] = Json("zip:unrelated.zip/notes.png")
            }
        };
        var pictures = ManaSpriteAssetDependencies.Pictures(script);
        check("sprite dependencies preserve escaped and Unicode picture paths",
            pictures.SequenceEqual(new[] { picture, "Content/Sprites/hero_n.png" }), string.Join(';', pictures));
        check("pack preloading uses the full pack reference", ManaSpriteAssetDependencies.PackOf(picture) == "zip:art.zip/", "");

        var scratch = Path.Combine(Path.GetTempPath(), "mana-dependencies-" + Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(scratch, "first");
        var secondRoot = Path.Combine(scratch, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        try
        {
            var sheet = new ManaSpriteSheet { Id = "shared-sheet", Frames = [picture, picture], NormalMap = "zip:art.zip/normal.png" };
            File.WriteAllText(Path.Combine(firstRoot, "Moved.manasheet"), JsonSerializer.Serialize(sheet, options));
            sheet.Frames = ["zip:other.zip/frame.png"];
            File.WriteAllText(Path.Combine(secondRoot, "Moved.manasheet"), JsonSerializer.Serialize(sheet, options));
            var reference = new ManaLevelScript
            {
                Type = "SpriteSheet", Properties = new()
                {
                    ["sheet"] = Json(new ManaAssetRef("shared-sheet", "Old.manasheet"))
                }
            };
            var first = ManaSpriteAssetDependencies.Pictures(reference, firstRoot);
            var second = ManaSpriteAssetDependencies.Pictures(reference, secondRoot);
            check("moved sheets resolve by ID and include deduplicated frames and normals",
                first.SequenceEqual(new[] { picture, "zip:art.zip/normal.png" }), string.Join(';', first));
            check("dependency resolution does not share asset IDs across content roots",
                second.Contains("zip:other.zip/frame.png") && !second.Contains(picture), string.Join(';', second));

            var slice = new ManaSpriteSlice { Id = "slice", TopLeft = picture, Center = "Content/Sprites/tile.png" };
            File.WriteAllText(Path.Combine(firstRoot, "Tile.manaslice"), JsonSerializer.Serialize(slice, options));
            reference.Type = "Sprite9Slice";
            reference.Properties = new() { ["slice"] = Json(new ManaAssetRef("slice", "Tile.manaslice")) };
            check("slice dependencies include authored pieces and loose pictures",
                ManaSpriteAssetDependencies.Pictures(reference, firstRoot).SequenceEqual(new[] { picture, "Content/Sprites/tile.png" }), "");
            reference.Type = "Unknown";
            check("unrelated component text is not treated as a sprite dependency",
                ManaSpriteAssetDependencies.Pictures(reference, firstRoot).Count == 0, "");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
        check("default bow sheets preload without explicit serialized properties",
            ManaSpriteAssetDependencies.Pictures(new ManaLevelScript { Type = "Bow" }, ManaContentLibrary.Root).Count > 0, "");
    }
}
