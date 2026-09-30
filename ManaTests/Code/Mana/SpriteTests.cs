using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// 2D pieces: a Sprite sized from its picture and showing the frame asked for, a SpriteAnimation stepping through a
/// sheet, and a PolygonCollider that turns any outline -- concave included -- into a solid.
/// </summary>
public static class SpriteChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        // The hero sheet: 128 x 32 pixels, four frames across, 32 pixels a metre -- so each frame is 1 m square.
        var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Hero" };
        context.World.DefaultScene.Children.Add(node);
        var sprite = new ManaSpriteComponent(node)
        {
            Texture = "Content/Sprites/hero.png", NormalMap = "Content/Sprites/hero_n.png", Columns = 4, PixelsPerUnit = 32
        };
        node.Components.Add(sprite);
        check("the test sheet is where the sample level expects it", ManaSpriteComponent.PngSize(ManaTexture.ResolveTexturePath(sprite.Texture)) == new Vector2(128, 32),
            $"{ManaSpriteComponent.PngSize(ManaTexture.ResolveTexturePath(sprite.Texture))}");

        sprite.Refresh();
        var renderer = node.GetComponent<MeshRendererComponent>();
        var mesh = renderer?.Mesh as ManaSpriteMesh;
        check("a sprite draws with a mesh renderer on its own node", renderer != null && mesh != null);
        check("sized from its picture: a 32-pixel frame at 32 pixels a metre is 1 m", mesh != null && Near(mesh.Size, new Vector2(1, 1)), $"{mesh?.Size}");
        check("with its feet on the node (pivot at the bottom middle)", mesh != null && mesh.GetOrCreateCpuData().Positions.Min(p => p.Y) == 0.0f
            && Near(new Vector2(mesh.GetOrCreateCpuData().Positions.Min(p => p.X), 0), new Vector2(-0.5f, 0)));
        check("pixel art by default: sampled nearest, so pixels stay square", mesh?.GetOrCreateCpuData().NearestFiltering == true);
        check("and its normal map with it", mesh?.GetOrCreateCpuData().NormalTexturePath?.EndsWith("hero_n.png", StringComparison.Ordinal) == true);

        sprite.Frame = 2;
        sprite.Refresh();
        check("frame 2 shows the third quarter of the sheet", renderer != null && Near(renderer.Material.UvScale, new Vector2(0.25f, 1)) && Near(renderer.Material.UvOffset, new Vector2(0.5f, 0)),
            $"{renderer?.Material.UvScale} {renderer?.Material.UvOffset}");
        sprite.FlipX = true;
        sprite.Refresh();
        check("and flipped, the same quarter mirrored", renderer != null && Near(renderer.Material.UvScale, new Vector2(-0.25f, 1)) && Near(renderer.Material.UvOffset, new Vector2(0.75f, 0)),
            $"{renderer?.Material.UvScale} {renderer?.Material.UvOffset}");
        sprite.Frame = 6;
        sprite.Refresh();
        check("a frame past the end wraps round", renderer != null && Near(renderer.Material.UvOffset, new Vector2(0.75f, 0)), $"{renderer?.Material.UvOffset}");

        var ledgeNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Tiled Ledge" };
        context.World.DefaultScene.Children.Add(ledgeNode);
        var tiled = new ManaSpriteComponent(ledgeNode) { Texture = "Content/Sprites/platform.png", PixelsPerUnit = 32, TiledSize = new Vector2(12, 1) };
        ledgeNode.Components.Add(tiled);
        tiled.Refresh();
        var tiledRenderer = ledgeNode.GetComponent<MeshRendererComponent>();
        check("a tiled sprite is the size asked for, the 4 m picture repeated three times across it",
            tiledRenderer?.Mesh is ManaSpriteMesh { } tiledMesh && Near(tiledMesh.Size, new Vector2(12, 1)) && Near(tiledRenderer.Material.UvScale, new Vector2(3, 1)),
            $"{(tiledRenderer?.Mesh as ManaSpriteMesh)?.Size} uv {tiledRenderer?.Material.UvScale}");

        // Kenney's platformer pack: pictures read straight out of the zip, frames given one picture each.
        const string pack = "zip:kenney_new-platformer-pack-1.1.zip/Sprites/";
        var packed = ManaSpritePictures.Resolve(pack + "Tiles/Default/coin_gold.png");
        check("a picture inside a pack is read out of the zip", packed != null && ManaSpriteComponent.PngSize(packed) == new Vector2(64, 64), packed ?? "not found");
        var listed = ManaSpritePictures.PackPictures();
        check("the picker lists pack pictures by the path a sprite stores",
            listed.Any(p => p.Reference == pack + "Tiles/Default/coin_gold.png" && p.Name == "coin_gold.png" && p.Folder == "kenney_new-platformer-pack-1.1"),
            $"{listed.Count} pictures");
        check("and costs nothing to look up once copied out", ManaSpritePictures.TryGetResolved(pack + "Tiles/Default/coin_gold.png") == packed);

        var walkerNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Walker" };
        context.World.DefaultScene.Children.Add(walkerNode);
        var walker = new ManaSpriteComponent(walkerNode)
        {
            Frames = [pack + "Characters/Default/character_green_idle.png", pack + "Characters/Default/character_green_walk_a.png", pack + "Characters/Default/character_green_jump.png"],
            PixelsPerUnit = 64
        };
        walkerNode.Components.Add(walker);
        walker.Frame = 1;
        walker.Refresh();
        var walkerRenderer = walkerNode.GetComponent<MeshRendererComponent>();
        var walkerMesh = walkerRenderer?.Mesh as ManaSpriteMesh;
        var strip = walkerMesh?.GetOrCreateCpuData().AlbedoTexturePath;
        check("separate frame pictures become one strip, a frame each, the sprite one frame's size",
            strip != null && ManaSpriteComponent.PngSize(strip) == new Vector2(384, 128) && walkerMesh != null && Near(walkerMesh.Size, new Vector2(2, 2))
            && walkerRenderer != null && Near(walkerRenderer.Material.UvScale, new Vector2(1.0f / 3.0f, 1)) && Near(walkerRenderer.Material.UvOffset, new Vector2(1.0f / 3.0f, 0)),
            $"{(strip == null ? "no strip" : ManaSpriteComponent.PngSize(strip))} size {walkerMesh?.Size} uv {walkerRenderer?.Material.UvScale} {walkerRenderer?.Material.UvOffset}");

        var groundNode = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Ground" };
        context.World.DefaultScene.Children.Add(groundNode);
        var ground = new ManaSpriteComponent(groundNode)
        {
            Texture = pack + "Tiles/Default/terrain_grass_block_center.png", LeftEnd = pack + "Tiles/Default/terrain_grass_block_left.png",
            RightEnd = pack + "Tiles/Default/terrain_grass_block_right.png", PixelsPerUnit = 64, TiledSize = new Vector2(6, 2)
        };
        groundNode.Components.Add(ground);
        ground.Refresh();
        var groundRenderer = groundNode.GetComponent<MeshRendererComponent>();
        var groundPicture = (groundRenderer?.Mesh as ManaSpriteMesh)?.GetOrCreateCpuData().AlbedoTexturePath;
        check("a tiled sprite with ends is drawn as one picture: an end, the middle between, an end, rows deep",
            groundPicture != null && ManaSpriteComponent.PngSize(groundPicture) == new Vector2(6 * 64, 2 * 64) && groundRenderer != null && Near(groundRenderer.Material.UvScale, Vector2.One),
            $"{(groundPicture == null ? "none" : ManaSpriteComponent.PngSize(groundPicture))} uv {groundRenderer?.Material.UvScale}");

        var animation = new ManaSpriteAnimationComponent(node) { FramesPerSecond = 4 };
        check("an animation steps a frame each 1/fps", animation.FrameAt(0.0, 4) == 0 && animation.FrameAt(0.26, 4) == 1 && animation.FrameAt(0.8, 4) == 3);
        check("and loops", animation.FrameAt(1.1, 4) == 0);
        animation.Loop = false;
        check("or, not looping, holds the last", animation.FrameAt(5.0, 4) == 3);
        animation.Loop = true;
        animation.FirstFrame = 1;
        animation.FrameCount = 2;
        check("a range plays just its frames", animation.FrameAt(0.0, 4) == 1 && animation.FrameAt(0.3, 4) == 2 && animation.FrameAt(0.55, 4) == 1);

        sprite.Frame = 0;
        node.Components.Add(animation);
        ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(0.3);
        animation.UpdateComponent();
        check("playing shows its frame without changing the saved one, so the level stays unchanged",
            sprite.PlayingFrame == 2 && sprite.Frame == 0, $"playing {sprite.PlayingFrame}, saved {sprite.Frame}");

        // An L: concave, six corners -- four triangles, and a solid of both faces plus the sides.
        var l = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) };
        var triangles = ManaPolygonColliderComponent.Triangulate(l);
        var area = triangles.Sum(t => MathF.Abs(Cross(l[t.A], l[t.B], l[t.C])) * 0.5f);
        check("a concave outline is cut into triangles covering exactly it", triangles.Count == 4 && MathF.Abs(area - 3.0f) < 1e-4f, $"{triangles.Count} triangles, area {area}");
        var solid = ManaPolygonColliderComponent.Extrude(l, 1.0f, Vector3.One);
        check("and made a solid: two faces and the sides", solid.Length == (4 * 2 + 6 * 2) * 3, $"{solid.Length / 3} triangles");

        var ledge = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Ledge" };
        context.World.DefaultScene.Children.Add(ledge);
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
