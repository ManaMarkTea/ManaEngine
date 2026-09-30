using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The jump, played out on a real body: held it tops out at JumpHeight, tapped it is a hop, from a run it goes
/// higher, and a spring's launch -- not a jump -- still reaches exactly the height it was asked for.
/// </summary>
public static class PawnJumpChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var held = Apex(pawn => pawn.PressJump(), holdFrames: int.MaxValue, out var heldPawn);
        check("a held jump tops out at its jump height", MathF.Abs(held - heldPawn.JumpHeight) < heldPawn.JumpHeight * 0.06f,
            $"{held:0.00} for {heldPawn.JumpHeight:0.00}");

        var tapped = Apex(pawn => pawn.PressJump(), holdFrames: 2, out _);
        check("a tap is a hop, well under half of it", tapped < held * 0.45f && tapped > 0.2f, $"{tapped:0.00} against {held:0.00}");

        var middling = Apex(pawn => pawn.PressJump(), holdFrames: 12, out _);
        check("and holding longer goes higher, in between", middling > tapped && middling < held, $"{tapped:0.00} < {middling:0.00} < {held:0.00}");

        var running = Apex(pawn => pawn.PressJump(), holdFrames: int.MaxValue, out var runner, runFirst: true);
        check("a jump from a full run goes higher than from standing", running > held * (1.0f + runner.RunJumpBonus * 0.8f),
            $"{running:0.00} against {held:0.00}");

        var launched = Apex(pawn => pawn.Bounce(pawn.VelocityForHeight(6.0f)), holdFrames: 0, out _);
        check("a spring's launch is not a jump: it reaches just the height it asked for", MathF.Abs(launched - 6.0f) < 0.35f, $"{launched:0.00} for 6");

        var fall = FallSpeed(out var faller);
        check("a long fall stops speeding up at the fall limit", MathF.Abs(fall - faller.MaxFallSpeed) < 0.5f, $"{fall:0.0} for {faller.MaxFallSpeed}");
    }

    // The highest the feet get after `start` on the first frame, with the button held for `holdFrames` frames.
    private static float Apex(Action<ManaPawnNode> start, int holdFrames, out ManaPawnNode pawn, bool runFirst = false)
    {
        pawn = Spawn(0.0f, out var physics);
        for (var frame = 0; frame < 30; frame++)
        {
            Step(pawn, physics);
        }

        if (runFirst)
        {
            pawn.DriveDirection = Vector3.UnitX;
            pawn.WantsToRun = true;
            for (var frame = 0; frame < 60; frame++)
            {
                Step(pawn, physics);
            }
        }

        var floor = pawn.Body!.FootPosition.Y;
        start(pawn);
        var highest = floor;
        for (var frame = 0; frame < 240; frame++)
        {
            if (frame == holdFrames)
            {
                pawn.ReleaseJump();
            }
            Step(pawn, physics);
            highest = MathF.Max(highest, pawn.Body.FootPosition.Y);
        }
        return highest - floor;
    }

    private static float FallSpeed(out ManaPawnNode pawn)
    {
        pawn = Spawn(400.0f, out var physics);
        var speed = 0.0f;
        for (var frame = 0; frame < 180; frame++)
        {
            Step(pawn, physics);
            speed = -pawn.Body!.Velocity.Y;
        }
        return speed;
    }

    private static ManaPawnNode Spawn(float height, out ManaPhysics physics)
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        physics = new ManaPhysics(context);
        context.Physics = physics;
        var floor = new ManaSceneNode(context, Matrix4x4.Identity);
        floor.UpdateTransform();
        physics.AddBoxBody(floor, new Vector3(4000.0f, 1.0f, 4000.0f), new Vector3(0.0f, -0.5f, 0.0f));

        var pawn = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false, Position = new Vector3(0.0f, height + 0.05f, 0.0f), FallResetHeight = -1000.0f };
        pawn.EnablePhysics(radius: 0.36f, height: 2.0f);
        return pawn;
    }

    private static void Step(ManaPawnNode pawn, ManaPhysics physics)
    {
        ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(1.0 / 60.0);
        pawn.UpdateTransform();
        physics.Update();
    }
}
