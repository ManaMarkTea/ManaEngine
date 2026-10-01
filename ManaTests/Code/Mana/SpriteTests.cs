using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Content;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// 2D pieces: a Sprite (one picture, sized from its pixels, repeatable), a Sprite Sheet playing named animations
/// from a sheet asset, a Sprite 9-Slice laying a slice asset out as a block, and a PolygonCollider that turns any
/// outline -- concave included -- into a solid.
/// </summary>
public static class SpriteChecks
{
    private const string Pack = "zip:kenney_new-platformer-pack-1.1.zip/Sprites/";

    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        ManaSceneNode Node(string name)
        {
            var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name };
            context.World.DefaultScene.Children.Add(node);
            return node;
        }

        // ---- Sprite: one picture ----------------------------------------------------------------------------------
        var ledgeNode = Node("Ledge Picture");
        var picture = new ManaSpriteComponent(ledgeNode) { Texture = "Content/Sprites/platform.png", NormalMap = "Content/Sprites/platform_n.png", PixelsPerUnit = 32 };
        ledgeNode.Components.Add(picture);
        picture.Refresh();
        var pictureRenderer = ledgeNode.GetComponent<MeshRendererComponent>();
        var pictureMesh = pictureRenderer?.Mesh as ManaSpriteMesh;
        check("a sprite draws with a mesh renderer on its own node", pictureRenderer != null && pictureMesh != null);
        check("sized from its picture: 128 x 32 pixels at 32 a metre is 4 x 1 m", pictureMesh != null && Near(pictureMesh.Size, new Vector2(4, 1)), $"{pictureMesh?.Size}");
        check("standing on the node (pivot at the bottom middle)", pictureMesh != null && pictureMesh.GetOrCreateCpuData().Positions.Min(p => p.Y) == 0.0f
            && Near(new Vector2(pictureMesh.GetOrCreateCpuData().Positions.Min(p => p.X), 0), new Vector2(-2f, 0)));
        check("with its normal map", pictureMesh?.GetOrCreateCpuData().NormalTexturePath?.EndsWith("platform_n.png", StringComparison.Ordinal) == true);
        check("smooth by default; pixel art is asked for", pictureMesh?.GetOrCreateCpuData().NearestFiltering == false);
        picture.PixelArt = true;
        picture.Refresh();
        check("and then sampled nearest, so pixels stay square", (ledgeNode.GetComponent<MeshRendererComponent>()?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().NearestFiltering == true);

        picture.RepeatSize = new Vector2(12, 1);
        picture.Refresh();
        pictureRenderer = ledgeNode.GetComponent<MeshRendererComponent>();
        check("repeated across a size: the 4 m picture three times across 12 m",
            pictureRenderer?.Mesh is ManaSpriteMesh { } repeated && Near(repeated.Size, new Vector2(12, 1)) && Near(pictureRenderer.Material.UvScale, new Vector2(3, 1)),
            $"{(pictureRenderer?.Mesh as ManaSpriteMesh)?.Size} uv {pictureRenderer?.Material.UvScale}");

        // ---- Pictures in packs -------------------------------------------------------------------------------------
        var packed = ManaSpritePictures.Resolve(Pack + "Tiles/Default/coin_gold.png");
        check("a picture inside a pack is read out of the zip", packed != null && ManaSpriteBase.PngSize(packed) == new Vector2(64, 64), packed ?? "not found");
        var listed = ManaSpritePictures.PackPictures();
        check("the picker lists pack pictures by the path a sprite stores",
            listed.Any(p => p.Reference == Pack + "Tiles/Default/coin_gold.png" && p.Name == "coin_gold.png" && p.Folder == "kenney_new-platformer-pack-1.1"),
            $"{listed.Count} pictures");
        check("and costs nothing to look up once copied out", ManaSpritePictures.TryGetResolved(Pack + "Tiles/Default/coin_gold.png") == packed);

        // ---- Sprite Sheet: a grid cut from one picture ------------------------------------------------------------
        var blob = new ManaAssetRef("b1c9e27a0f3d4c5e8a6b7d9e0f1a2b3c", "Sprites/Blob.manasheet");
        check("a sheet asset is found by its path", ManaContentAssets.Load<ManaSpriteSheet>(blob, ManaSpriteSheet.Extension)?.Name == "Blob");
        check("and by its id alone, wherever it has moved to",
            ManaContentAssets.Resolve(blob with { AssetPath = "Somewhere/Else.manasheet" }, ManaSpriteSheet.Extension)?.EndsWith("Blob.manasheet", StringComparison.Ordinal) == true);

        var blobNode = Node("Blob");
        var sheet = new ManaSpriteSheetComponent(blobNode) { Sheet = blob, Frame = 2, PixelArt = true };
        blobNode.Components.Add(sheet);
        sheet.Refresh();
        var sheetRenderer = blobNode.GetComponent<MeshRendererComponent>();
        check("a sheet sprite is one frame big: 32 pixels at 32 a metre is 1 m",
            sheetRenderer?.Mesh is ManaSpriteMesh { } frameMesh && Near(frameMesh.Size, new Vector2(1, 1)), $"{(sheetRenderer?.Mesh as ManaSpriteMesh)?.Size}");
        check("frame 2 shows the third quarter of the sheet", sheetRenderer != null && Near(sheetRenderer.Material.UvScale, new Vector2(0.25f, 1)) && Near(sheetRenderer.Material.UvOffset, new Vector2(0.5f, 0)),
            $"{sheetRenderer?.Material.UvScale} {sheetRenderer?.Material.UvOffset}");
        sheet.FlipX = true;
        sheet.Refresh();
        check("and flipped, the same quarter mirrored", sheetRenderer != null && Near(sheetRenderer.Material.UvScale, new Vector2(-0.25f, 1)) && Near(sheetRenderer.Material.UvOffset, new Vector2(0.75f, 0)),
            $"{sheetRenderer?.Material.UvScale} {sheetRenderer?.Material.UvOffset}");
        check("and the sheet's normal map with it", (sheetRenderer?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().NormalTexturePath?.EndsWith("hero_n.png", StringComparison.Ordinal) == true);

        sheet.Animation = "walk";
        sheet.Advance(0.0);
        sheet.Advance(0.3);
        check("a named animation plays: walk at 4 a second is on its second frame after 0.3 s", sheet.PlayingFrame == 1, $"{sheet.PlayingFrame}");
        sheet.Advance(1.0);
        check("and loops round", sheet.PlayingFrame == 1, $"{sheet.PlayingFrame} at 1.3 s");
        check("without touching the saved frame, so the level stays unchanged", sheet.Frame == 2);
        sheet.Animation = "idle";
        sheet.Advance(0.1);
        check("a different one starts from its own beginning", sheet.PlayingFrame == 0);

        var animation = new ManaSpriteAnimation { Frames = [3, 5, 7], FramesPerSecond = 2, Loop = false };
        check("an animation holds its last frame when it does not loop", animation.FrameAt(0.0) == 3 && animation.FrameAt(0.6) == 5 && animation.FrameAt(9.0) == 7);

        // ---- Sprite Sheet: one picture per frame (the Green Alien) ------------------------------------------------
        var alienNode = Node("Alien");
        var alien = new ManaSpriteSheetComponent(alienNode) { Sheet = ManaPlatformer2DComponent.GreenAlien, Animation = "walk" };
        alienNode.Components.Add(alien);
        alien.Refresh();
        var alienRenderer = alienNode.GetComponent<MeshRendererComponent>();
        var strip = (alienRenderer?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().AlbedoTexturePath;
        check("a sheet of separate pictures becomes one strip, a frame each",
            strip != null && ManaSpriteBase.PngSize(strip) == new Vector2(9 * 128, 128) && alien.FrameCount == 9,
            $"{(strip == null ? "no strip" : ManaSpriteBase.PngSize(strip))}, {alien.FrameCount} frames");

        // ---- Sprite 9-Slice ---------------------------------------------------------------------------------------
        var block = ManaContentAssets.Load<ManaSpriteSlice>(Path.Combine(ManaContentAssets.Root, "Sprites", "Terrain", "Grass Block.manaslice"));
        check("a slice asset loads", block != null);
        if (block != null)
        {
            var layout = block.Layout(4, 3);
            check("laid out: rounded corners top and bottom, edges down the sides, the middle filling in",
                layout[0][0].EndsWith("block_top_left.png") && layout[0][1].EndsWith("block_top.png") && layout[0][3].EndsWith("block_top_right.png")
                && layout[1][0].EndsWith("block_left.png") && layout[1][2].EndsWith("block_center.png") && layout[2][3].EndsWith("block_bottom_right.png"),
                string.Join(" / ", layout.Select(row => string.Join(", ", row.Select(Path.GetFileNameWithoutExtension)))));
            var single = block.Layout(3, 1);
            check("and one tile high, just the top row", single.Count == 1 && single[0][0].EndsWith("block_top_left.png"));
        }

        var groundNode = Node("Ground");
        var ground = new ManaSprite9SliceComponent(groundNode)
        {
            Slice = ManaContentAssets.Reference(Path.Combine(ManaContentAssets.Root, "Sprites", "Terrain", "Grass Block.manaslice")), Size = new Vector2(6, 2)
        };
        groundNode.Components.Add(ground);
        ground.Refresh();
        var groundRenderer = groundNode.GetComponent<MeshRendererComponent>();
        var groundPicture = (groundRenderer?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().AlbedoTexturePath;
        check("a 9-slice is drawn as one picture of whole tiles, the size asked for",
            groundPicture != null && ManaSpriteBase.PngSize(groundPicture) == new Vector2(6 * 64, 2 * 64)
            && groundRenderer?.Mesh is ManaSpriteMesh { } groundMesh && Near(groundMesh.Size, new Vector2(6, 2)),
            $"{(groundPicture == null ? "none" : ManaSpriteBase.PngSize(groundPicture))}");

        groundNode.Scale = new Vector3(2.0f, 1.4f, 1.0f);
        ground.Size = new Vector2(6.0f, 2.2f);
        ground.Refresh();
        groundRenderer = groundNode.GetComponent<MeshRendererComponent>();
        groundPicture = (groundRenderer?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().AlbedoTexturePath;
        // 12 x 3 tiles in the world; the quad is divided back by the scale so none of them stretch.
        check("scaling a 9-slice adds whole tiles instead of stretching them",
            groundPicture != null && ManaSpriteBase.PngSize(groundPicture) == new Vector2(12 * 64, 3 * 64)
            && groundRenderer?.Mesh is ManaSpriteMesh { } scaledMesh && Near(scaledMesh.Size, new Vector2(12.0f / 2.0f, 3.0f / 1.4f)),
            $"{(groundPicture == null ? "none" : ManaSpriteBase.PngSize(groundPicture))}, {(groundRenderer?.Mesh as ManaSpriteMesh)?.Size}");
        groundNode.Scale = Vector3.One;
        ground.Size = new Vector2(6, 2);
        ground.Refresh();

        // ---- Polygon collider -------------------------------------------------------------------------------------
        // An L: concave, six corners -- four triangles, and a solid of both faces plus the sides.
        var l = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) };
        var triangles = ManaPolygonColliderComponent.Triangulate(l);
        var area = triangles.Sum(t => MathF.Abs(Cross(l[t.A], l[t.B], l[t.C])) * 0.5f);
        check("a concave outline is cut into triangles covering exactly it", triangles.Count == 4 && MathF.Abs(area - 3.0f) < 1e-4f, $"{triangles.Count} triangles, area {area}");
        var solid = ManaPolygonColliderComponent.Extrude(l, 1.0f, Vector3.One);
        check("and made a solid: two faces and the sides", solid.Length == (4 * 2 + 6 * 2) * 3, $"{solid.Length / 3} triangles");

        var ledge = Node("Ledge");
        var collider = new ManaPolygonColliderComponent(ledge) { Points = [-2, 0, 2, 0, 2, 1, -2, 1], Depth = 2 };
        ledge.Components.Add(collider);
        collider.Rebuild();
        check("a polygon collider gives its node a body", collider.Body != null && ledge.GetComponent<ManaCollisionComponent>() == collider.Body);
        var first = collider.Body;
        collider.Points = [-3, 0, 3, 0, 3, 1, -3, 1];
        collider.Rebuild();
        check("and rebuilds it when the outline changes, keeping just one", collider.Body != null && !ReferenceEquals(collider.Body, first)
            && ledge.Components.OfType<ManaCollisionComponent>().Count() == 1);
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-4f;

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
