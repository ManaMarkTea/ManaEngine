using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// A physics pawn landing: it has to reach the floor in the fall, not hang above it. The ground probe
/// reaches a quarter of a unit below the feet, and a character counted as standing the moment the floor was
/// in reach used to have its fall stopped there -- then crept the rest of the way down a frame of gravity
/// at a time, which on a one-metre character reads as sinking into place.
/// </summary>
public static class PawnLandingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        foreach (var (label, height, drop, walking) in new[]
        {
            ("a one-metre character", 1.0f, 3.0f, false),
            ("a one-metre character walking", 1.0f, 3.0f, true),
            ("a two-metre character", 2.0f, 6.0f, false)
        })
        {
            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            world.Context = context;
            var physics = new ManaPhysics(context);
            context.Physics = physics;
            var floor = new ManaSceneNode(context, Matrix4x4.Identity);
            floor.UpdateTransform();
            physics.AddBoxBody(floor, new Vector3(1000.0f, 1.0f, 1000.0f), new Vector3(0.0f, -0.5f, 0.0f));

            var pawn = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false };
            pawn.Position = new Vector3(0.0f, drop, 0.0f);
            pawn.EnablePhysics(radius: height * 0.18f, height: height);
            if (walking)
            {
                pawn.DriveDirection = Vector3.UnitX;
            }

            int? inReach = null;
            int? settled = null;
            var lowest = float.MaxValue;
            for (var frame = 0; frame < 240 && settled == null; frame++)
            {
                ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(1.0 / 60.0);
                pawn.UpdateTransform();
                physics.Update();
                var feet = pawn.Body!.FootPosition.Y;
                lowest = MathF.Min(lowest, feet);
                if (inReach == null && feet < 0.25f)
                {
                    inReach = frame;
                }

                if (inReach != null && feet < 0.035f)
                {
                    settled = frame;
                }
            }

            var frames = settled - inReach;
            check($"{label} lands on its feet in the fall, rather than sinking the last stretch",
                frames is <= 2, $"{frames?.ToString() ?? "never"} frames from a quarter unit up to the floor");
            check($"{label} does not sink into the floor landing", lowest > -0.05f, $"lowest {lowest:0.###}");
        }
    }
}
