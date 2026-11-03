using System.Runtime.InteropServices;
using System.Text.Json;
using ManaPixelFx;
using ManaPixelFx.Core;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The C# port of pixelfx draws exactly what the JavaScript does: every ported effect is played one run at its fps,
/// as pixelfx's sheet renderer plays it, and both buffers of every frame -- colour and light -- are hashed and
/// compared with the hashes pixelfx itself wrote (Tools/PixelFx/parity.mjs, ManaTests/Data/PixelFx/).
/// </summary>
public static class PixelFxParityChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        // ---- the core, against values pixelfx computes
        check("hash and rand match pixelfx's (rand(1, 2, 3) = 0.6755...)", Math.Abs(PixelMath.Rand(1, 2, 3) - Js.Rand123) < 1e-12, $"{PixelMath.Rand(1, 2, 3):R}");
        var rng = new PixelRng(7);
        check("the seeded generator matches mulberry32 (Rng(7) twice)", Math.Abs(rng.Next() - Js.Rng7First) < 1e-12 && Math.Abs(rng.Next() - Js.Rng7Second) < 1e-12);
        check("colours keep pixelfx's byte order (R low)", PixelColor.Hex("#123456") == 0xff563412u, $"{PixelColor.Hex("#123456"):x8}");
        check("rounding is JavaScript's, halves up", PixelMath.Round(-2.5) == -2 && PixelMath.Round(2.5) == 3);

        // ---- every ported effect: every third frame drawn and compared (state carries between them, so a divergence
        // anywhere shows), the effects side by side
        var effects = PixelEffects.All.Where(effect => !effect.Meta.Id.StartsWith("test/", StringComparison.Ordinal)).ToList();
        var results = new (string What, bool Ok, string Detail)[effects.Count];
        Parallel.For(0, effects.Count, n => results[n] = Compare(effects[n]));
        foreach (var (what, ok, detail) in results)
        {
            check(what, ok, detail);
        }
    }

    private const int Every = 3;

    private static (string What, bool Ok, string Detail) Compare(PixelEffect effect)
    {
        var id = effect.Meta.Id;
        var file = Path.Combine(["ManaTests", "Data", "PixelFx", .. id.Split('/')]) + ".json";
        if (!File.Exists(file))
        {
            return ($"{id}: has pixelfx's frames to compare with (node Tools/PixelFx/parity.mjs {id})", false, file);
        }
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var expected = json.RootElement.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString() ?? "").ToList();
        var count = PixelFxRun.FrameCount(effect.Meta, effect.Meta.Fps);
        var actual = new Dictionary<int, string>();
        PixelFxRun.Play(effect, null, 0, (i, frame) => actual[i] = $"{Fnv(frame.Color)} {Fnv(frame.Light)}", i => i % Every == 0 || i == count - 1);
        var what = $"{id}: draws what pixelfx draws, frame for frame";
        if (expected.Count != count)
        {
            return (what, false, $"{count} frames, pixelfx drew {expected.Count}");
        }
        foreach (var (i, hash) in actual.OrderBy(pair => pair.Key))
        {
            if (expected[i] != hash)
            {
                return (what, false, $"frame {i} differs (colour, light: pixelfx {expected[i]}, ours {hash})");
            }
        }
        return (what, true, $"{actual.Count} of {count} frames");
    }

    // 32-bit FNV-1a over a buffer's bytes, as the reference tool hashes them.
    private static string Fnv(uint[] words)
    {
        var h = 0x811c9dc5u;
        foreach (var b in MemoryMarshal.AsBytes(words.AsSpan()))
        {
            h ^= b;
            h = unchecked(h * 0x01000193u);
        }
        return h.ToString("x8");
    }

    // Values printed by pixelfx's own math.js (node -e), so the core is checked against the original, not itself.
    private static class Js
    {
        public const double Rand123 = 0.6754740150645375;
        public const double Rng7First = 0.011704753153026104;
        public const double Rng7Second = 0.06195825757458806;
    }
}
