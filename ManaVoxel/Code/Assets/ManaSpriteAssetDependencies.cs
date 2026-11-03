using System.Text.Json;
using Mana.Code.Components;
using Mana.Code.Content;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.Assets;

/// <summary>Pictures consumed by the sprite components, including their referenced sheets and slices.</summary>
public static class ManaSpriteAssetDependencies
{
    public static IReadOnlyList<string> Pictures(ManaLevelScript script, string? contentRoot = null)
    {
        var pictures = new List<string>();
        var root = contentRoot ?? ManaContentAssets.Root;
        JsonElement Value(string name) => script.Properties.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        string Text(string name) => Value(name) is var value && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
        ManaAssetRef Reference(string name, ManaAssetRef fallback)
        {
            var value = Value(name);
            if (value.ValueKind == JsonValueKind.Undefined)
            {
                return fallback;
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                return ManaAssetRef.None;
            }
            string Field(string field) => value.EnumerateObject().FirstOrDefault(property =>
                string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase)).Value is var item
                && item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
            return new ManaAssetRef(Field("assetId"), Field("assetPath"));
        }
        void Sheet(ManaAssetRef reference)
        {
            if (ManaContentAssets.Resolve(reference, ManaSpriteSheet.Extension, root) is { } file
                && ManaContentAssets.Load<ManaSpriteSheet>(file) is { } sheet)
            {
                pictures.AddRange(sheet.Frames);
                pictures.Add(sheet.Texture);
                pictures.Add(sheet.NormalMap);
                pictures.Add(sheet.Light);
            }
        }
        switch (script.Type.ToUpperInvariant())
        {
            case "SPRITE":
                pictures.Add(Text(nameof(ManaSpriteComponent.Texture)));
                pictures.Add(Text(nameof(ManaSpriteComponent.NormalMap)));
                break;
            case "SPRITESHEET":
                Sheet(Reference(nameof(ManaSpriteSheetComponent.Sheet), ManaAssetRef.None));
                break;
            case "PIXELFX":
                Sheet(Reference(nameof(ManaPixelFxComponent.Effect), ManaAssetRef.None));
                break;
            case "PLATFORMER2D":
                Sheet(Reference(nameof(ManaPlatformer2DComponent.Sheet), ManaPlatformer2DComponent.GreenAlien));
                break;
            case "BOW":
                Sheet(Reference(nameof(BowComponent.BowSheet), BowComponent.WoodenBow));
                Sheet(Reference(nameof(BowComponent.ArrowSheet), BowComponent.WoodenArrow));
                break;
            case "SPRITE9SLICE":
                var reference = Reference(nameof(ManaSprite9SliceComponent.Slice), ManaAssetRef.None);
                if (ManaContentAssets.Resolve(reference, ManaSpriteSlice.Extension, root) is { } file
                    && ManaContentAssets.Load<ManaSpriteSlice>(file) is { } slice)
                {
                    pictures.AddRange([slice.TopLeft, slice.Top, slice.TopRight, slice.Left, slice.Center,
                        slice.Right, slice.BottomLeft, slice.Bottom, slice.BottomRight]);
                }
                break;
        }
        return pictures.Where(picture => !string.IsNullOrWhiteSpace(picture)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? PackOf(string picture)
    {
        if (!picture.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var end = picture.IndexOf(".zip/", StringComparison.OrdinalIgnoreCase);
        return end >= 4 ? picture[..(end + 5)] : null;
    }
}
