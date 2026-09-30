using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>The sky's clock: hours to phases and back, holding still, running at a speed, and the wall clock.</summary>
public static class SkyClockChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        check("6 am is sunrise, noon a quarter turn on", Near(ManaSky.PhaseAt(6.0), 0.0f) && Near(ManaSky.PhaseAt(12.0), 0.25f), "");
        check("and an hour comes back the hour it was", Near(ManaSky.HoursAt(ManaSky.PhaseAt(17.85)), 17.85f) && Near(ManaSky.HoursAt(ManaSky.PhaseAt(0.0)), 0.0f),
            $"{ManaSky.HoursAt(ManaSky.PhaseAt(17.85))}");

        // Held: a fixed time stays put however many frames go by.
        var held = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.4f, TimeSpeed = 0.0f };
        ManaSky.Tick(held);
        Thread.Sleep(60);
        ManaSky.Tick(held);
        check("a held time of day does not move", Near(held.TimeOfDay, 0.4f), $"{held.TimeOfDay}");

        // Running: faster goes further in the same time.
        var slow = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.1f, TimeSpeed = 3600.0f };
        var fast = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.1f, TimeSpeed = 4.0f * 3600.0f };
        ManaSky.Tick(slow);
        ManaSky.Tick(fast);
        Thread.Sleep(80);
        ManaSky.Tick(slow);
        ManaSky.Tick(fast);
        var slowGone = slow.TimeOfDay - 0.1f;
        var fastGone = fast.TimeOfDay - 0.1f;
        check("a running day moves on, and 4 hours a second goes further than 1", slowGone > 0.0f && fastGone > slowGone * 2.5f,
            $"1 h/s {slowGone:0.0000}, 4 h/s {fastGone:0.0000}");

        // An hour a second is a day in 24 seconds: after a (clamped) quarter-second tick, a quarter of an hour.
        var hourly = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.0f, TimeSpeed = 3600.0f };
        ManaSky.Tick(hourly);
        Thread.Sleep(300);
        ManaSky.Tick(hourly);
        check("an hour a second moves the clock an hour each second", MathF.Abs(ManaSky.HoursAt(hourly.TimeOfDay) - 6.25f) < 0.02f,
            $"{ManaSky.HoursAt(hourly.TimeOfDay):0.000} h");

        // Only the cycle has a clock: a still day keeps its time.
        var day = new ManaIrradianceSettings { Sky = ManaSkyMode.Day, TimeOfDay = 0.3f, TimeSpeed = 4.0f };
        ManaSky.Tick(day);
        Thread.Sleep(30);
        ManaSky.Tick(day);
        check("a plain day sky has no clock running", Near(day.TimeOfDay, 0.3f), $"{day.TimeOfDay}");

        var real = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, RealTime = true };
        ManaSky.Tick(real);
        var expected = ManaSky.PhaseAt(DateTime.Now.TimeOfDay.TotalHours);
        check("real time follows the wall clock", MathF.Abs(real.TimeOfDay - expected) < 0.01f || MathF.Abs(real.TimeOfDay - expected) > 0.99f,
            $"{ManaSky.HoursAt(real.TimeOfDay):0.00} h against {ManaSky.HoursAt(expected):0.00} h");

        var noon = ManaSky.Evaluate(new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.25f });
        var midnight = ManaSky.Evaluate(new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, TimeOfDay = 0.75f });
        check("noon is full daylight and midnight is dark", noon.Daylight > 0.95f && midnight.Daylight < 0.15f && noon.TowardSun.Y > 0.9f,
            $"noon {noon.Daylight:0.00}, midnight {midnight.Daylight:0.00}");
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;
}
