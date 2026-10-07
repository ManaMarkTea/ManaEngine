using System.Numerics;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>The world's wind: headings, gusts that stay in bounds, and the drift shaders scroll by.</summary>
public static class WindChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        check("a heading of 0 blows toward -Z, 90 toward +X",
            Near(ManaWind.DirectionFor(0.0f), new Vector2(0.0f, -1.0f)) && Near(ManaWind.DirectionFor(90.0f), new Vector2(1.0f, 0.0f)),
            $"{ManaWind.DirectionFor(0.0f)} {ManaWind.DirectionFor(90.0f)}");

        var minimum = 1.0f;
        var maximum = 0.0f;
        for (var second = 0.0; second < 600.0; second += 0.25)
        {
            var gust = ManaWind.GustAt(second);
            minimum = MathF.Min(minimum, gust);
            maximum = MathF.Max(maximum, gust);
        }
        check("gusts come and go within 0..1 over ten minutes", minimum == 0.0f && maximum > 0.9f && maximum <= 1.0f, $"{minimum:0.00}..{maximum:0.00}");

        check("a steady wind blows at its set speed whatever the gust",
            ManaWind.SpeedAt(4.0f, 0.0f, 1.0f) == 4.0f && ManaWind.SpeedAt(4.0f, 0.0f, 0.0f) == 4.0f, "");
        check("a squally one lulls under it and gusts to about twice it",
            ManaWind.SpeedAt(4.0f, 1.0f, 0.0f) < 4.0f && ManaWind.SpeedAt(4.0f, 1.0f, 1.0f) > 8.0f,
            $"{ManaWind.SpeedAt(4.0f, 1.0f, 0.0f):0.0} .. {ManaWind.SpeedAt(4.0f, 1.0f, 1.0f):0.0}");

        var settings = new ManaIrradianceSettings { WindSpeed = 5.0f, WindDirection = 90.0f, Gustiness = 0.0f };
        var state = ManaWind.Advance(settings, 0.0, 0.0f);
        for (var i = 1; i <= 10; i++)
        {
            state = ManaWind.Advance(settings, i * 0.1, 0.1f);
        }
        check("the air carries things downwind at its speed", Near(state.Drift, new Vector2(5.0f, 0.0f)) && MathF.Abs(state.Distance - 5.0f) < 1e-3f,
            $"drift {state.Drift}, distance {state.Distance}");

        settings.WindSpeed = 0.0f;
        var still = ManaWind.Advance(settings, 2.0, 0.1f);
        check("and a still day leaves it where it was", still.Speed == 0.0f && Near(still.Drift, state.Drift), $"{still.Drift}");
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;
}
