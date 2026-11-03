using System.Text.Json;
using ManaCore.Imaging;
using ManaPixelFx;
using ManaPixelFx.Core;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The C# port of pixelfx draws what the JavaScript does: each ported effect is rendered frame by frame as pixelfx's
/// sheet renderer renders it (stepped at its fps, light eased off the edges) and both buffers, colour and light, are
/// compared with the sheets imported from the original (Content/Sprites/PixelFx/). Floating-point maths libraries
/// differ in the last bit now and then, which can flip a pixel on a threshold, so a sliver of difference is allowed.
/// </summary>
public static class PixelFxParityChecks
{
    // Share of an effect's drawn pixels allowed to differ by more than a step in a channel.
    private const double Tolerance = 0.002;

    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        // ---- the core, against values pixelfx computes
        check("hash and rand match pixelfx's (rand(1, 2, 3) = 0.6755...)", Math.Abs(PixelMath.Rand(1, 2, 3) - Js.Rand123) < 1e-12, $"{PixelMath.Rand(1, 2, 3):R}");
        var rng = new PixelRng(7);
        check("the seeded generator matches mulberry32 (Rng(7) twice)", Math.Abs(rng.Next() - Js.Rng7First) < 1e-12 && Math.Abs(rng.Next() - Js.Rng7Second) < 1e-12);
        check("colours keep pixelfx's byte order (R low)", PixelColor.Hex("#123456") == 0xff563412u, $"{PixelColor.Hex("#123456"):x8}");
        check("rounding is JavaScript's, halves up", PixelMath.Round(-2.5) == -2 && PixelMath.Round(2.5) == 3);

        // ---- every ported effect against its sheet
        foreach (var effect in PixelEffects.All)
        {
            var meta = effect.Meta;
            var file = Path.Combine("Content", "Sprites", "PixelFx", meta.Id + ".manasheet");
            if (!File.Exists(file))
            {
                check($"{meta.Id}: has a sheet to compare with", false, file);
                continue;
            }
            var (diff, drawn, frames) = Compare(effect, file);
            var share = diff / Math.Max(1.0, drawn);
            check($"{meta.Id}: draws what pixelfx draws ({frames} frames)", share <= Tolerance && drawn > 0,
                $"{diff} of {drawn} drawn pixels differ ({share:P2})");
        }
    }

    /// <summary>Pixels differing by more than one step in a channel, pixels drawn, and frames compared.</summary>
    private static (long Diff, long Drawn, int Frames) Compare(PixelEffect effect, string sheetFile)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(sheetFile));
        var root = json.RootElement;
        var columns = root.GetProperty("columns").GetInt32();
        var pivot = root.GetProperty("pivot");
        var animation = root.GetProperty("animations")[0];
        var fps = animation.GetProperty("framesPerSecond").GetDouble();
        var count = animation.GetProperty("frames").GetArrayLength();
        var sheet = ManaImageCodec.Load(root.GetProperty("texture").GetString() ?? "");
        var lightSheet = ManaImageCodec.Load(root.GetProperty("light").GetString() ?? "");
        var rows = root.GetProperty("rows").GetInt32();

        var meta = effect.Meta;
        var (w, h) = meta.Size;
        var (ox, oy) = meta.Origin;
        var bw = sheet.Width / columns;
        var bh = sheet.Height / rows;
        var bx = (int)Math.Round(ox - pivot[0].GetDouble() * bw);
        var by = (int)Math.Round(pivot[1].GetDouble() * bh + oy - bh);
        var step = (int)Math.Round(meta.Fps / fps);

        var frame = new PixelFrame(w, h);
        var fx = new PixelFxInstance(effect, new Dictionary<string, object> { ["x"] = (double)ox, ["y"] = (double)oy });
        var screen = w >= 320 && Math.Abs(w / (double)h - 16.0 / 9.0) < 0.05;
        var n = Math.Max(1, (int)Math.Round(meta.Duration * meta.Fps) + (meta.Loop ? 0 : 1));
        long diff = 0;
        long drawn = 0;
        var compared = 0;
        for (var i = 0; i < n && i / step < count; i++)
        {
            if (i > 0)
            {
                fx.Update(1.0 / meta.Fps);
            }
            if (i % step != 0)
            {
                continue;
            }
            frame.Clear();
            fx.Draw(frame);
            if (!screen)
            {
                frame.EdgeFade();
            }
            var color = System.Runtime.InteropServices.MemoryMarshal.AsBytes(frame.Color.AsSpan());
            var light = System.Runtime.InteropServices.MemoryMarshal.AsBytes(frame.Light.AsSpan());
            var k = i / step;
            var cellX = (k % columns) * bw;
            var cellY = (k / columns) * bh;
            for (var y = 0; y < bh; y++)
            {
                for (var x = 0; x < bw; x++)
                {
                    var s = ((cellY + y) * sheet.Width + cellX + x) * 4;
                    var mine = ((by + y) * w + bx + x) * 4;
                    var theirs = sheet.Pixels.AsSpan(s, 4);
                    var ours = color.Slice(mine, 4);
                    var theirLight = lightSheet.Pixels.AsSpan(s, 4);
                    var ourLight = light.Slice(mine, 4);
                    if (theirs[3] == 0 && ours[3] == 0 && theirLight[3] == 0 && ourLight[3] == 0)
                    {
                        continue;
                    }
                    drawn++;
                    if (Differs(theirs, ours) || Differs(theirLight, ourLight))
                    {
                        diff++;
                    }
                }
            }
            compared++;
        }
        return (diff, drawn, compared);
    }

    private static bool Differs(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        for (var c = 0; c < 4; c++)
        {
            if (Math.Abs(a[c] - b[c]) > 1)
            {
                return true;
            }
        }
        return false;
    }

    // Values printed by pixelfx's own math.js (node -e), so the core is checked against the original, not itself.
    private static class Js
    {
        public const double Rand123 = 0.6754740150645375;
        public const double Rng7First = 0.011704753153026104;
        public const double Rng7Second = 0.06195825757458806;
    }
}
