using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The pure parts of ground movement: how far a step toward a target speed may go. The rest of the
/// feel -- how long the run-up takes on a real capsule -- needs the physics world and is judged by playing.
/// </summary>
public static class PawnMovementChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var stepped = ManaPawnNode.MoveTowards(Vector3.Zero, new Vector3(10.0f, 0.0f, 0.0f), 3.0f);
        Check("a step toward a target goes no further than allowed", MathF.Abs(stepped.X - 3.0f) < 0.001f, $"{stepped}");

        var arrived = ManaPawnNode.MoveTowards(new Vector3(9.0f, 0.0f, 0.0f), new Vector3(10.0f, 0.0f, 0.0f), 3.0f);
        Check("and lands on it rather than overshooting", arrived == new Vector3(10.0f, 0.0f, 0.0f));

        var braking = ManaPawnNode.MoveTowards(new Vector3(10.0f, 0.0f, 0.0f), Vector3.Zero, 4.0f);
        Check("slowing to a stop is the same step the other way", MathF.Abs(braking.X - 6.0f) < 0.001f);

        // Running one way and asking for the opposite: the vector shrinks through zero before it grows,
        // so a hard reversal brakes first instead of flipping at full speed.
        var reversing = ManaPawnNode.MoveTowards(new Vector3(10.0f, 0.0f, 0.0f), new Vector3(-10.0f, 0.0f, 0.0f), 4.0f);
        Check("a reversal brakes before it accelerates the other way", reversing.X > 0.0f && reversing.X < 10.0f, $"{reversing.X}");

        var pawn = new ManaPawnNode(new ManaContext(null!, null!, new ManaWorld()), Matrix4x4.Identity);
        Check("the run-up is quick but not instant", pawn.AccelerationSeconds is > 0.05f and < 0.5f, $"{pawn.AccelerationSeconds}s");
        Check("and stopping is crisper than starting", pawn.DecelerationSeconds < pawn.AccelerationSeconds);
        Check("the everyday pace is faster than it was", ManaPawnNode.DefaultWalkSpeed >= 6.5f && ManaPawnNode.DefaultRunSpeed > ManaPawnNode.DefaultWalkSpeed,
            $"{ManaPawnNode.DefaultWalkSpeed} / {ManaPawnNode.DefaultRunSpeed}");
    }
}
