using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Side-on play: a pawn held to its plane goes nowhere when pushed into the screen and runs when pushed
/// sideways, and a Platformer2D swaps the model for a sprite while playing and puts it all back after.
/// </summary>
public static class Platformer2DChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        var physics = new ManaPhysics(context);
        context.Physics = physics;
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        var floor = new ManaSceneNode(context, Matrix4x4.Identity);
        floor.UpdateTransform();
        physics.AddBoxBody(floor, new Vector3(1000.0f, 1.0f, 1000.0f), new Vector3(0.0f, -0.5f, 0.0f));

        var pawn = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false, Position = new Vector3(0.0f, 0.1f, 5.0f) };
        pawn.EnablePhysics(radius: 0.36f, height: 2.0f);
        pawn.PlaneZ = 5.0f;

        void run(Vector3 drive, int frames)
        {
            pawn.DriveDirection = drive;
            for (var frame = 0; frame < frames; frame++)
            {
                ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(1.0 / 60.0);
                pawn.UpdateTransform();
                physics.Update();
            }
        }

        run(-Vector3.UnitZ, 60);
        var feet = pawn.Body!.FootPosition;
        check("held to its plane, pushing into the screen goes nowhere", MathF.Abs(feet.Z - 5.0f) < 0.02f && MathF.Abs(feet.X) < 0.02f, $"{feet}");
        run(Vector3.Normalize(new Vector3(1, 0, -1)), 60);
        feet = pawn.Body.FootPosition;
        check("and pushing sideways runs along it, at full speed", feet.X > 4.0f && MathF.Abs(feet.Z - 5.0f) < 0.02f, $"{feet}");

        // Play: the model is hidden, a sprite rides on the pawn, and stopping puts the model back.
        var model = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Model" };
        var modelRenderer = new MeshRendererComponent(model, new Mana.Code.Shapes.ManaSpriteMesh(context, "Content/Sprites/hero.png", null, Vector2.One, Vector2.Zero, Vector4.One));
        model.Components.Add(modelRenderer);
        pawn.AddChild(model);
        pawn.PlaneZ = null;

        var start = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Player Start" };
        var platformer = new ManaPlatformer2DComponent(start);
        start.Components.Add(platformer);
        world.DefaultScene.Children.Add(start);
        world.GameState.Player = pawn;
        world.BeginPlay();

        check("playing, the player is held to the plane it stands on", pawn.PlaneZ is { } z && MathF.Abs(z - pawn.Position.Z) < 1e-4f, $"{pawn.PlaneZ}");
        check("its model is hidden", !modelRenderer.IsVisible);
        var sprite = platformer.SpriteNode?.GetComponent<ManaSpriteComponent>();
        check("and a sprite rides on it instead", sprite != null && ReferenceEquals(platformer.SpriteNode?.Parent, pawn));

        pawn.Rotation = new Vector3(0.0f, 1.2f, 0.0f);
        pawn.Scale = new Vector3(0.5f, 0.5f, 0.5f);
        run(Vector3.Zero, 1);
        var spriteWorld = platformer.SpriteNode?.WorldTransform ?? default;
        check("square to the camera however the pawn is turned or scaled", Near(spriteWorld.M11, 1) && Near(spriteWorld.M33, 1) && Near(spriteWorld.M22, 1)
            && Vector3.Distance(spriteWorld.Translation, pawn.WorldTransform.Translation) < 1e-3f, $"{spriteWorld}");

        check("frames: idle standing, running while moving, jumping in the air",
            platformer.FrameFor(true, 0.0f, 0.0) == platformer.IdleFrame
            && platformer.FrameFor(true, 5.0f, 0.25) == platformer.RunFirstFrame + 2
            && platformer.FrameFor(false, 5.0f, 0.25) == platformer.JumpFrame);

        world.EndPlay();
        check("stopping shows the model again, drops the sprite and frees the plane",
            modelRenderer.IsVisible && platformer.SpriteNode == null && !pawn.Children.Any(c => c.Name == "2D Player Sprite") && pawn.PlaneZ == null);
    }

    /// <summary>A parallax layer follows that share of the camera's travel, and goes back where it was on stop.</summary>
    public static void RunParallax(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        var layer = new ManaSceneNode(context, Matrix4x4.Identity) { Position = new Vector3(10, 0, -5) };
        var parallax = new ManaParallaxComponent(layer) { Factor = new Vector2(0.75f, 0.25f) };
        check("a layer follows its share of the camera: three quarters across, a quarter up",
            parallax.OffsetFor(layer.Position, new Vector3(8, 4, 3)) == new Vector3(16, 1, -5), $"{parallax.OffsetFor(layer.Position, new Vector3(8, 4, 3))}");
        parallax.BeginPlay();
        layer.Position = new Vector3(99, 99, 99);
        parallax.EndPlay();
        check("and is back where it was placed when play stops", layer.Position == new Vector3(10, 0, -5), $"{layer.Position}");
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;
}
