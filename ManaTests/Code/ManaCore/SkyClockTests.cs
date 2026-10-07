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

        var newMoon = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        var quarterMoon = newMoon.AddDays(29.530588853 / 4.0);
        var fullMoon = newMoon.AddDays(29.530588853 / 2.0);
        var lastQuarterMoon = newMoon.AddDays(29.530588853 * 0.75);
        check("lunar phase tracks new, quarter, and full moons",
            Near(ManaLunarPhase.PhaseAt(newMoon), 0.0f) && Near(ManaLunarPhase.PhaseAt(quarterMoon), 0.25f) && Near(ManaLunarPhase.PhaseAt(fullMoon), 0.5f),
            $"new {ManaLunarPhase.PhaseAt(newMoon):0.00}, quarter {ManaLunarPhase.PhaseAt(quarterMoon):0.00}, full {ManaLunarPhase.PhaseAt(fullMoon):0.00}");
        check("moon phases have picker emojis and names",
            ManaLunarPhase.EmojiAt(newMoon) == "🌑" && ManaLunarPhase.EmojiAt(quarterMoon) == "🌓" &&
            ManaLunarPhase.EmojiAt(fullMoon) == "🌕" && ManaLunarPhase.NameAt(fullMoon) == "Full moon" &&
            ManaLunarPhase.EmojiAt(lastQuarterMoon) == "🌗" && ManaLunarPhase.NameAt(lastQuarterMoon) == "Last quarter" &&
            ManaLunarPhase.NameAt(newMoon.AddDays(29.530588853 / 8.0)) == "Waxing crescent",
            $"new {ManaLunarPhase.EmojiAt(newMoon)}; crescent {ManaLunarPhase.EmojiAt(newMoon.AddDays(29.530588853 / 8.0))}; full {ManaLunarPhase.EmojiAt(fullMoon)}");
        check("calendar markers appear only on the four principal moon days",
            ManaLunarPhase.PrincipalPhaseEmojiAt(newMoon) == "🌑" &&
            ManaLunarPhase.PrincipalPhaseEmojiAt(quarterMoon) == "🌓" &&
            ManaLunarPhase.PrincipalPhaseEmojiAt(fullMoon) == "🌕" &&
            ManaLunarPhase.PrincipalPhaseEmojiAt(lastQuarterMoon) == "🌗" &&
            ManaLunarPhase.PrincipalPhaseEmojiAt(newMoon.AddDays(29.530588853 / 8.0)) == null,
            "new, first quarter, full, last quarter; none between quarters");
        check("moon illumination ranges from new to quarter to full",
            Near(ManaLunarPhase.Illumination(0.0f), 0.0f) && Near(ManaLunarPhase.Illumination(0.25f), 0.5f) && Near(ManaLunarPhase.Illumination(0.5f), 1.0f),
            $"new {ManaLunarPhase.Illumination(0.0f):0.00}, quarter {ManaLunarPhase.Illumination(0.25f):0.00}, full {ManaLunarPhase.Illumination(0.5f):0.00}");
        var selectedMoonSettings = new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, SkyDateTime = fullMoon };
        check("the sky moon follows the selected simulation date",
            Near(ManaSky.Evaluate(selectedMoonSettings).MoonPhase, ManaLunarPhase.PhaseAt(fullMoon)),
            $"{ManaSky.Evaluate(selectedMoonSettings).MoonPhase:0.00}");

        var siderealEpoch = new DateTimeOffset(2000, 1, 1, 6, 0, 0, TimeSpan.Zero);
        check("a sidereal phase completes after 23 h 56 m 4 s",
            Near(ManaSky.SiderealPhaseAt(siderealEpoch), 0.0f) && Near(ManaSky.SiderealPhaseAt(siderealEpoch.AddSeconds(ManaSky.SiderealDaySeconds)), 0.0f),
            $"{ManaSky.SiderealPhaseAt(siderealEpoch.AddSeconds(ManaSky.SiderealDaySeconds)):0.0000}");

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

        var backwardDate = newMoon.AddDays(2.0);
        var backwards = new ManaIrradianceSettings
        {
            Sky = ManaSkyMode.DayNightCycle,
            SkyDateTime = backwardDate,
            TimeOfDay = 0.001f,
            TimeSpeed = -3600.0f
        };
        ManaSky.Tick(backwards);
        Thread.Sleep(80);
        ManaSky.Tick(backwards);
        check("a negative time speed runs the day backwards across sunrise", backwards.TimeOfDay > 0.95f,
            $"{ManaSky.HoursAt(backwards.TimeOfDay):0.00} h");
        check("negative sky speed moves the simulated date backward", backwards.SkyDateTime < backwardDate,
            backwards.SkyDateTime.ToString("yyyy-MM-dd HH:mm"));

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

        var summerNoon = new DateTimeOffset(2026, 6, 21, 13, 0, 0, TimeSpan.FromHours(-7));
        var noon = ManaSky.Evaluate(new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, SkyDateTime = summerNoon });
        var midnight = ManaSky.Evaluate(new ManaIrradianceSettings { Sky = ManaSkyMode.DayNightCycle, SkyDateTime = summerNoon.AddHours(12.0) });
        check("noon is full daylight and midnight is dark", noon.Daylight > 0.95f && midnight.Daylight < 0.15f && noon.TowardSun.Y > 0.9f,
            $"noon {noon.Daylight:0.00}, midnight {midnight.Daylight:0.00}");

        var daytimeQuarterMoon = ManaSky.Evaluate(new ManaIrradianceSettings
        {
            Sky = ManaSkyMode.DayNightCycle,
            SkyDateTime = new DateTimeOffset(2023, 7, 25, 15, 0, 0, TimeSpan.FromHours(-7))
        });
        check("a first-quarter moon can be above Seattle in daylight",
            daytimeQuarterMoon.Daylight > 0.9f && daytimeQuarterMoon.TowardMoon.Y > 0.1f,
            $"sun {daytimeQuarterMoon.TowardSun}, moon {daytimeQuarterMoon.TowardMoon}");
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;
}
