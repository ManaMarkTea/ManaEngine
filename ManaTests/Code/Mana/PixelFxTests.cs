using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Content;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Pixel FX: sheets imported from pixelfx (Tools/PixelFx) played by the PixelFx script -- loops loop, one-shots play
/// once or again after a pause and show nothing between, the quad turns to face the camera, and a spawned one is
/// put in the world and taken away when it ends.
/// </summary>
public static class PixelFxChecks
{
    private static readonly ManaAssetRef Torch = new("b6f7a5a209c1489c9187cadb49e987b5", "Sprites/PixelFx/fire/torch.manasheet");
    private static readonly ManaAssetRef HitFlash = new("81afcd18cc9c482ab3f80310fbbc5690", "Sprites/PixelFx/impacts/hit-flash.manasheet");

    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        ManaPixelFxComponent Fx(string name, ManaAssetRef effect)
        {
            var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name };
            context.World.DefaultScene.Children.Add(node);
            var fx = new ManaPixelFxComponent(node) { Effect = effect };
            node.Components.Add(fx);
            return fx;
        }

        // ---- The imported sheets ------------------------------------------------------------------------------------
        var torch = Fx("Torch", Torch);
        var torchSheet = torch.Asset;
        check("an imported sheet says which pixelfx effect it came from", torchSheet is { IsPixelFx: true, Source: "pixelfx:fire/torch" }, torchSheet?.Source ?? "no sheet");
        check("with one animation at the effect's rate, looping as the effect does",
            torchSheet?.Animations is [{ FramesPerSecond: 30, Loop: true, Frames.Count: 60 }], $"{torchSheet?.Animations.Count}");

        torch.Refresh();
        var mesh = torch.Owner.GetComponent<MeshRendererComponent>()?.Mesh as ManaSpriteMesh;
        check("draws as a pixel-art sprite: nearest sampling, unlit", mesh?.GetOrCreateCpuData().NearestFiltering == true && mesh.Materials[0].Unlit);
        check("sized at 20 pixels a metre: a 72 x 113 frame is 3.6 x 5.65 m", mesh != null && Near(mesh.Size, new Vector2(3.6f, 5.65f)), $"{mesh?.Size}");

        // ---- Loops and one-shots ------------------------------------------------------------------------------------
        torch.Advance(0.5);
        check("a loop steps through its frames", Shown(torch) == 15, $"{Shown(torch)}");
        torch.Advance(2.0);
        check("and wraps round (2.5 s of a 2 s loop is frame 15 again)", Shown(torch) == 15 && !torch.Done, $"{Shown(torch)}");

        var flash = Fx("Hit", HitFlash);
        var flashFrames = flash.Asset?.Animations[0].Frames.Count ?? 0;
        var last = flashFrames - 1;
        check("a one-shot imports as one", flash.Asset?.Animations[0].Loop == false && flashFrames == 16, $"{flashFrames} frames");
        flash.Repeat = false;
        flash.Advance(0.2);
        check("plays its frames", Shown(flash) == 6 && !flash.Done, $"{Shown(flash)}");
        flash.Advance(1.0);
        check("once over, done, holding its empty last frame", flash.Done && Shown(flash) == last, $"{Shown(flash)}");

        var again = Fx("Hit Again", HitFlash);
        again.RepeatDelay = 1.0f;
        var length = 16.0 / 30.0;
        again.Advance(length + 0.5);
        check("a repeating one-shot shows nothing while it waits", Shown(again) == last && !again.Done, $"{Shown(again)}");
        again.Advance(0.6);
        check("then plays again", Shown(again) == 3, $"{Shown(again)}");

        var late = Fx("Late", HitFlash);
        late.Delay = 0.5f;
        late.Advance(0.25);
        check("a delayed one-shot shows nothing before it starts", Shown(late) == last, $"{Shown(late)}");
        late.Advance(0.37);
        check("and starts on time", Shown(late) == 3, $"{Shown(late)}");
        late.Restart();
        late.Advance(0.1);
        check("Restart plays it from the beginning, delay and all", Shown(late) == last, $"{Shown(late)}");

        // ---- Facing the camera --------------------------------------------------------------------------------------
        var face = Fx("Facing", Torch);
        face.Owner.Position = new Vector3(2, 0, 0);
        var facing = face.FacingCamera(new Vector3(12, 5, 0));
        check("turns about up so its face (+Z) looks at the camera", Near(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, facing)), Vector3.UnitX),
            $"{Vector3.TransformNormal(Vector3.UnitZ, facing)}");
        check("staying where it was placed", Near(facing.Translation, new Vector3(2, 0, 0)), $"{facing.Translation}");

        var turned = new ManaSceneNode(context, Matrix4x4.CreateRotationY(MathF.PI / 2)) { Name = "Turned" };
        turned.WorldTransform = Matrix4x4.CreateRotationY(MathF.PI / 2);
        var child = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child" };
        turned.AddChild(child);
        var childFx = new ManaPixelFxComponent(child) { Effect = Torch };
        var world = childFx.FacingCamera(new Vector3(0, 0, -10)) * turned.WorldTransform;
        check("under a turned parent it still faces the camera in the world", Near(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world)), -Vector3.UnitZ),
            $"{Vector3.TransformNormal(Vector3.UnitZ, world)}");

        // ---- Spawned from code --------------------------------------------------------------------------------------
        var holder = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Holder" };
        context.World.DefaultScene.Children.Add(holder);
        var spawned = ManaPixelFxSpawner.Spawn(holder, HitFlash, new Vector3(1, 2, 3), flip: true);
        check("a spawned effect waits for the end of the scene walk to join", holder.Children.Count == 0);
        RunPostUpdate(context.World);
        check("then joins its parent where it was asked, once, flipped and never saved",
            holder.Children.Count == 1 && spawned.Owner.Position == new Vector3(1, 2, 3) && spawned is { Repeat: false, FlipX: true } && spawned.Owner.IsEditorTransient);
        spawned.FaceCamera = false;
        spawned.Advance(1.0);
        spawned.UpdateComponent();
        check("and once over is taken away after the walk", holder.Children.Count == 1 && spawned.Done);
        RunPostUpdate(context.World);
        check("and is gone", holder.Children.Count == 0 && spawned.Owner.Parent == null);

        // ---- Drawn live, or from the sheet ---------------------------------------------------------------------------
        var live = Fx("Live Torch", Torch);
        live.FaceCamera = false;
        live.UpdateComponent();
        var quads = live.Owner.Components.OfType<MeshRendererComponent>().ToList();
        check("a ported effect draws live", live.LiveEffect?.Meta.Id == "fire/torch", live.LiveEffect?.Meta.Id ?? "from the sheet");
        check("as two live pictures: colour blended, light added over it",
            quads.Count == 2 && quads.All(q => q.Mesh.GetOrCreateCpuData().AlbedoDynamicTexture != null)
            && quads[0].Material.OpacityMode == ManaCore.Graphics.ManaOpacityMode.Blend && quads[1].Material.OpacityMode == ManaCore.Graphics.ManaOpacityMode.Additive,
            string.Join(", ", quads.Select(q => q.Material.OpacityMode)));
        var colour = quads[0].Mesh.GetOrCreateCpuData().AlbedoDynamicTexture;
        check("its picture is drawn (some of the torch is opaque)", colour != null && Enumerable.Range(0, colour.Pixels.Length / 4).Any(i => colour.Pixels[i * 4 + 3] == 255));
        live.Live = false;
        live.UpdateComponent();
        quads = live.Owner.Components.OfType<MeshRendererComponent>().ToList();
        check("off, it plays the sheet: its picture blended and its light added on the same frame",
            live.LiveEffect == null && quads.Count == 2 && quads[0].Material.OpacityMode == ManaCore.Graphics.ManaOpacityMode.Blend
            && quads[1].Material.OpacityMode == ManaCore.Graphics.ManaOpacityMode.Additive && quads[1].Material.UvOffset == quads[0].Material.UvOffset,
            string.Join(", ", quads.Select(q => q.Material.OpacityMode)));
        live.Live = true;
        live.Palette = "ice";
        live.UpdateComponent();
        check("and back live, recoloured", live.LiveEffect != null && live.Owner.Components.OfType<MeshRendererComponent>().Count() == 2);
    }

    private static int Shown(ManaPixelFxComponent fx) =>
        (int)typeof(ManaPixelFxComponent).GetProperty("CurrentFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fx)!;

    private static void RunPostUpdate(ManaWorld world)
    {
        var futures = world.PostUpdateFutures.ToList();
        world.PostUpdateFutures.Clear();
        foreach (var future in futures)
        {
            future();
        }
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
}
