using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Content;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaCore.Graphics;
using ManaCore.Imaging;
using ManaPixelFx;
using ManaPixelFx.Core;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The PixelFx script: a ported pixelfx effect drawn live as two pictures (colour blended, light added), its options
/// changed per instance, loops looping and one-shots playing once or again after a pause, standing on its node by
/// its lowest pixel, facing the camera and letting go of it, baked into a sprite sheet, and spawned from code.
/// </summary>
public static class PixelFxChecks
{
    // A one-shot to time against: half a second of a 4 x 4 square at the origin.
    private sealed class Flash : PixelEffect
    {
        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "test/flash", Name = "Flash", Size = (16, 16), Origin = (8, 12), Duration = 16 / 30.0, Fps = 30,
            Options = new Dictionary<string, object> { ["seed"] = 1.0 },
        };

        public override void Draw(PixelPainter p, double t, PixelFxOptions o) => p.Rect(-2, -4, 4, 4, PixelColor.White);
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        PixelEffects.Register(new Flash());
        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        ManaPixelFxComponent Fx(string name, string effect)
        {
            var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name };
            context.World.DefaultScene.Children.Add(node);
            var fx = new ManaPixelFxComponent(node) { Effect = effect, FaceCamera = false };
            node.Components.Add(fx);
            return fx;
        }
        List<MeshRendererComponent> Quads(ManaPixelFxComponent fx) => fx.Owner.Components.OfType<MeshRendererComponent>().ToList();

        // ---- drawn live ---------------------------------------------------------------------------------------------
        var torch = Fx("Torch", "fire/torch");
        torch.UpdateComponent();
        var quads = Quads(torch);
        check("a pixel effect is drawn live, from its code", torch.Definition?.Meta.Name == "Torch");
        check("as two live pictures: colour blended, light added over it",
            quads.Count == 2 && quads.All(q => q.Mesh.GetOrCreateCpuData().AlbedoDynamicTexture != null)
            && quads[0].Material.OpacityMode == ManaOpacityMode.Blend && quads[1].Material.OpacityMode == ManaOpacityMode.Additive,
            string.Join(", ", quads.Select(q => q.Material.OpacityMode)));
        var colour = quads[0].Mesh.GetOrCreateCpuData().AlbedoDynamicTexture;
        check("its picture is drawn (some of the torch is opaque)", colour != null && Enumerable.Range(0, colour.Pixels.Length / 4).Any(i => colour.Pixels[i * 4 + 3] == 255));

        // ---- standing on its node -----------------------------------------------------------------------------------
        var (_, anchorY) = torch.AnchorPixel();
        check("it stands on its lowest pixel, not pixelfx's origin (a wall torch's is its bracket)", anchorY > torch.Definition!.Meta.Origin.Y + 5, $"anchor row {anchorY}, origin {torch.Definition.Meta.Origin.Y}");
        torch.Anchor = PixelFxAnchor.Origin;
        check("or on pixelfx's origin when asked", torch.AnchorPixel() == (36, 94), $"{torch.AnchorPixel()}");
        torch.Anchor = PixelFxAnchor.Bottom;

        // ---- options ------------------------------------------------------------------------------------------------
        check("its options are the effect's own until changed", torch.OptionValue("seed") is 4.0 && !torch.IsOptionChanged("seed"), $"{torch.OptionValue("seed")}");
        var before = Quads(torch)[0];
        torch.SetOption("palette", PixelColor.Fit(PixelColor.Pal["ice"], 9).Select(PixelColor.ToHex).ToArray());
        torch.SetOption("seed", 9.0);
        torch.UpdateComponent();
        check("changed, they go to the effect and it is drawn again",
            torch.IsOptionChanged("seed") && torch.EffectOptions()["seed"] is 9.0 && !ReferenceEquals(before, Quads(torch)[0]) && Quads(torch).Count == 2);
        check("changes save as plain JSON", torch.Options["palette"].ValueKind == System.Text.Json.JsonValueKind.Array && torch.Options["seed"].GetDouble() == 9.0);
        torch.SetOption("seed", null);
        check("and reset to the effect's own", !torch.IsOptionChanged("seed") && torch.OptionValue("seed") is 4.0);

        // ---- timing -------------------------------------------------------------------------------------------------
        var once = Fx("Once", "test/flash");
        once.Repeat = false;
        once.Advance(0.2);
        check("a one-shot plays", once.ShownAt is { } at && Math.Abs(at - 0.2) < 1e-9 && !once.Done, $"{once.ShownAt}");
        once.Advance(1.0);
        check("once over, it is done and shows nothing", once.Done && once.ShownAt == null);

        var again = Fx("Again", "test/flash");
        again.RepeatDelay = 1.0f;
        var length = 16 / 30.0;
        again.Advance(length + 0.5);
        check("a repeating one-shot shows nothing while it waits", again.ShownAt == null && !again.Done);
        again.Advance(0.6);
        check("then plays again", again.ShownAt is { } next && Math.Abs(next - (length + 1.1 - (length + 1.0))) < 1e-6, $"{again.ShownAt}");

        var late = Fx("Late", "test/flash");
        late.Delay = 0.5f;
        late.Advance(0.25);
        check("a delayed one-shot shows nothing before it starts", late.ShownAt == null);
        late.Advance(0.35);
        check("and starts on time", late.ShownAt is { } started && Math.Abs(started - 0.1) < 1e-6, $"{late.ShownAt}");
        late.Restart();
        late.Advance(0.1);
        check("Restart plays it from the beginning, delay and all", late.ShownAt == null);

        // ---- facing the camera, and letting go ----------------------------------------------------------------------
        var face = Fx("Facing", "fire/torch");
        face.Owner.Position = new Vector3(2, 0, 0);
        var facing = face.FacingCamera(new Vector3(12, 5, 0));
        check("turns about up so its face (+Z) looks at the camera", Near(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, facing)), Vector3.UnitX),
            $"{Vector3.TransformNormal(Vector3.UnitZ, facing)}");
        check("staying where it was placed", Near(facing.Translation, new Vector3(2, 0, 0)), $"{facing.Translation}");

        var turned = new ManaSceneNode(context, Matrix4x4.CreateRotationY(MathF.PI / 2)) { Name = "Turned" };
        turned.WorldTransform = Matrix4x4.CreateRotationY(MathF.PI / 2);
        var child = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Child" };
        turned.AddChild(child);
        var childFx = new ManaPixelFxComponent(child) { Effect = "fire/torch" };
        var world = childFx.FacingCamera(new Vector3(0, 0, -10)) * turned.WorldTransform;
        check("under a turned parent it still faces the camera in the world", Near(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world)), -Vector3.UnitZ),
            $"{Vector3.TransformNormal(Vector3.UnitZ, world)}");

        context.World.CameraNode = new ManaSceneNode(context, Matrix4x4.CreateTranslation(20, 1, 0)) { Name = "Camera" };
        context.World.CameraNode.WorldTransform = Matrix4x4.CreateTranslation(20, 1, 0);
        face.Owner.Rotation = new Vector3(0, 0.5f, 0);
        face.FaceCamera = true;
        face.UpdateComponent();
        var faced = Vector3.TransformNormal(Vector3.UnitZ, face.Owner.LocalTransform);
        face.FaceCamera = false;
        face.UpdateComponent();
        var own = Vector3.TransformNormal(Vector3.UnitZ, face.Owner.LocalTransform);
        var placed = Vector3.TransformNormal(Vector3.UnitZ, Matrix4x4.CreateFromQuaternion(face.Owner.Rotation.ToQuaternionFromEuler()));
        check("turned off, it goes back to the turn it was placed with", Near(Vector3.Normalize(faced), Vector3.UnitX) && Near(own, placed), $"faced {faced}, now {own}, placed {placed}");

        // ---- removed ------------------------------------------------------------------------------------------------
        var removed = Fx("Removed", "fire/candle");
        removed.UpdateComponent();
        SceneEditorOperations.RemoveComponent(null, removed);
        check("removed, it takes its pictures with it", Quads(removed).Count == 0 && removed.Owner.Components.Count == 0, $"{removed.Owner.Components.Count} left");

        // ---- baked --------------------------------------------------------------------------------------------------
        check("a bake keeps every frame while the sheet fits", ManaPixelFxBaker.Layout(60, 72, 113, 30) is (1, _, _));
        check("drops every other one rather than make a sheet past 2048", ManaPixelFxBaker.Layout(163, 217, 226, 30) is (2, var c, var r) && c * 217 <= 4096 && r * 226 <= 4096,
            $"{ManaPixelFxBaker.Layout(163, 217, 226, 30)}");
        var folder = Path.Combine(Path.GetTempPath(), "mana-pixelfx-bake-" + Guid.NewGuid().ToString("N"));
        try
        {
            var baking = Fx("Baked Torch", "fire/torch");
            baking.SetOption("seed", 7.0);
            baking.UpdateComponent();
            var file = ManaPixelFxBaker.Bake(baking, folder, ManaPixelFxBaker.FreeName(folder, "Torch"));
            var sheet = ManaContentAssets.Load<ManaSpriteSheet>(file);
            var picture = ManaImageCodec.Load(Path.Combine(folder, "Torch.png"));
            check("baking writes the picture, its light and a sheet playing them at the effect's rate",
                sheet is { Source: "pixelfx:fire/torch", Animations: [{ FramesPerSecond: 30, Loop: true, Frames.Count: 60 }] }
                && File.Exists(Path.Combine(folder, "Torch.light.png")) && sheet.Light.EndsWith("Torch.light.png", StringComparison.Ordinal),
                sheet?.Light ?? "no sheet");
            check("cropped to what the effect draws", picture.Width / sheet!.Columns < 72 || picture.Height / sheet.Rows < 120, $"{picture.Width / sheet.Columns} x {picture.Height / sheet.Rows}");
            check("a second bake of the same name does not overwrite the first", ManaPixelFxBaker.FreeName(folder, "Torch") == "Torch 2");

            var replacement = ManaPixelFxBaker.SheetFor(baking, file);
            ManaPixelFxBaker.Swap(baking, replacement);
            var node = baking.Owner;
            check("and swaps the script for a Sprite Sheet playing it, taking the live pictures off",
                node.Components.Contains(replacement) && !node.Components.Contains(baking) && Quads(baking).Count == 0 && replacement.Animation == "play");
            ManaPixelFxBaker.Swap(replacement, baking);
            baking.UpdateComponent();
            check("which swaps back (undo), live again", node.Components.Contains(baking) && !node.Components.Contains(replacement) && Quads(baking).Count == 2);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        // ---- spawned from code --------------------------------------------------------------------------------------
        var holder = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Holder" };
        context.World.DefaultScene.Children.Add(holder);
        var spawned = ManaPixelFxSpawner.Spawn(holder, "test/flash", new Vector3(1, 2, 3), flip: true);
        check("a spawned effect waits for the end of the scene walk to join", holder.Children.Count == 0);
        RunPostUpdate(context.World);
        check("then joins its parent where it was asked, once, by its origin, flipped and never saved",
            holder.Children.Count == 1 && spawned.Owner.Position == new Vector3(1, 2, 3) && spawned is { Repeat: false, FlipX: true, Anchor: PixelFxAnchor.Origin } && spawned.Owner.IsEditorTransient);
        spawned.FaceCamera = false;
        spawned.Advance(1.0);
        spawned.UpdateComponent();
        check("and once over is taken away after the walk", holder.Children.Count == 1 && spawned.Done);
        RunPostUpdate(context.World);
        check("and is gone", holder.Children.Count == 0 && spawned.Owner.Parent == null);
    }

    private static void RunPostUpdate(ManaWorld world)
    {
        var futures = world.PostUpdateFutures.ToList();
        world.PostUpdateFutures.Clear();
        foreach (var future in futures)
        {
            future();
        }
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
}
