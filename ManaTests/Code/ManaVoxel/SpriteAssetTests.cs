using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaTests;

/// <summary>
/// Sprite assets as the editor makes and lists them: made from pictures picked in the Import panel, which
/// pictures count as sprites there, and every kind of user-made asset showing in the Project panel.
/// </summary>
public static class SpriteAssetChecks
{
    private const string Tiles = "zip:kenney_new-platformer-pack-1.1.zip/Sprites/Tiles/Default/";

    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        string[] walk = ["zip:p.zip/character_green_idle.png", "zip:p.zip/character_green_walk_a.png", "zip:p.zip/character_green_walk_b.png"];
        var sheet = SpriteAssetFactory.Sheet(walk);
        check("a sheet from picked pictures takes them as its frames, in order", sheet.Frames.SequenceEqual(walk));
        check("named from what their names share, in whole words", sheet.Name == "Character Green", sheet.Name);
        check("with an idle on the first frame and one playing them all",
            sheet.Animation("idle")?.Frames.SequenceEqual([0]) == true && sheet.Animation("play")?.Frames.SequenceEqual([0, 1, 2]) == true);

        string[] block = ["top_left", "top", "top_right", "left", "center", "right"];
        var slice = SpriteAssetFactory.Slice(block.Select(piece => $"{Tiles}terrain_grass_block_{piece}.png").ToList());
        check("a 9-slice from picked pictures puts each where its name says",
            slice.TopLeft.EndsWith("_top_left.png") && slice.Top.EndsWith("_top.png") && slice.TopRight.EndsWith("_top_right.png")
            && slice.Left.EndsWith("_left.png") && slice.Center.EndsWith("_center.png") && slice.Right.EndsWith("_right.png") && slice.Bottom.Length == 0,
            $"{slice.TopLeft} | {slice.Left} | {slice.Center}");
        check("named for the set without the tile words", slice.Name == "Terrain Grass", slice.Name);

        var platform = SpriteAssetFactory.Slice(new[] { "left", "middle", "right" }.Select(piece => $"{Tiles}terrain_grass_horizontal_{piece}.png").ToList());
        check("a one-row platform's pieces make the top row",
            platform.TopLeft.EndsWith("horizontal_left.png") && platform.Top.EndsWith("horizontal_middle.png") && platform.TopRight.EndsWith("horizontal_right.png"));

        check("a 2D pack's pictures are sprites", MeshAssetLibrary.IsSpritePicture("Sprites/Tiles/Default/coin_gold.png", packHasModels: false, looseFile: false));
        check("but a 3D pack's pictures are its models' textures", !MeshAssetLibrary.IsSpritePicture("Models/Textures/colormap.png", packHasModels: true, looseFile: false));
        check("and the pack's preview, its packed sheets and its double-size copies are left out",
            !MeshAssetLibrary.IsSpritePicture("Preview.png", false, false)
            && !MeshAssetLibrary.IsSpritePicture("Spritesheets/spritesheet-tiles-default.png", false, false)
            && !MeshAssetLibrary.IsSpritePicture("Sprites/Tiles/Double/coin_gold.png", false, false));
        check("a loose picture is a sprite when it sits in a Sprites folder",
            MeshAssetLibrary.IsSpritePicture("Game/Sprites/hero.png", false, looseFile: true) && !MeshAssetLibrary.IsSpritePicture("Textures/wood.png", false, looseFile: true));
        check("a pack picture is stored by the pack's name and its path inside",
            MeshAssetLibrary.SpritePicture(MeshAssetLibrary.EncodeReadableZip("kenney_new-platformer-pack-1.1.zip", "Sprites/Tiles/Default/coin_gold.png"))
                == Tiles + "coin_gold.png");

        var listed = ManaContentLibrary.List();
        check("the Project panel lists every kind: prefabs, sprite sheets, 9-slices and generators",
            listed.Any(e => e.Kind == "Prefab") && listed.Any(e => e.Kind == "Sprite Sheet" && e.Name == "Green Alien")
            && listed.Any(e => e.Kind == "9-Slice" && e.Name == "Grass Block") && listed.Any(e => e.Kind == "Generator" && e.Name == "Dungeon"),
            string.Join(", ", listed.GroupBy(e => e.Kind).Select(g => $"{g.Key} {g.Count()}")));
        check("and new ones can be made of the sprite kinds", ManaContentKinds.All.Where(k => k.CanCreate).Select(k => k.Name).SequenceEqual(["Sprite Sheet", "9-Slice"]));
    }
}
