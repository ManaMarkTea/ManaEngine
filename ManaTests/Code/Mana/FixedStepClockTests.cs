using Mana.Code.GameModes;
using ManaUI;

namespace ManaTests;

/// <summary>ManaFixedStepClock: fixed ticks against real time, sped up, paused, capped after a hitch, with the fraction between ticks.</summary>
public static class FixedStepClockChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var clock = new ManaFixedStepClock(0.1f);
        var ticks = 0;
        for (var frame = 0; frame < 60; frame++)
        {
            clock.Advance(1.0f / 60.0f, () => ticks++);
        }
        check("a second of frames runs a second of ticks", ticks == 10, $"{ticks} ticks");

        clock.Advance(0.05f, () => ticks++);
        check("half a tick in, alpha is a half", MathF.Abs(clock.Alpha - 0.5f) < 1e-3f, $"{clock.Alpha:F3}");

        clock.CycleSpeed();
        var fast = clock.Advance(0.2f, () => { });
        check("at 2x a frame runs twice the ticks", clock.Speed == 2 && fast == 4 || clock.Speed == 2 && fast == 5, $"speed {clock.Speed}, {fast} ticks");
        clock.CycleSpeed();
        clock.CycleSpeed();
        check("speeds cycle 1, 2, 4 and back to 1", clock.Speed == 1, $"speed {clock.Speed}");

        clock.Paused = true;
        check("a paused clock runs nothing", clock.Advance(1.0f, () => ticks++) == 0, "");
        clock.Paused = false;

        var capped = new ManaFixedStepClock(0.001f) { MaxTicksPerFrame = 40 };
        check("a hitch is capped, not caught up", capped.Advance(5.0f, () => { }) == 40, "");
    }
}
