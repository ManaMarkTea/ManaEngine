using ManaCore.Imaging;
using ManaUI;

namespace ManaTests;

/// <summary><see cref="ManaMipChain"/>: every level down to 1 x 1, averaged as light for colour pictures.</summary>
public static class MipChainChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var levels = ManaMipChain.Build(new byte[8 * 4 * 4], 8, 4, srgb: false);
        Check("halves down to one pixel", levels.Select(l => (l.Width, l.Height)).SequenceEqual([(4, 2), (2, 1), (1, 1)]),
            string.Join(" ", levels.Select(l => $"{l.Width}x{l.Height}")));
        Check("the count matches the GPU's rule", ManaMipChain.LevelCount(8, 4) == levels.Count + 1);

        // Black and white checks: stored numbers average to 128, light averages to sRGB 188.
        var checker = new byte[2 * 2 * 4];
        for (var i = 0; i < 4; i++)
        {
            var value = (byte)((i == 0 || i == 3) ? 255 : 0);
            checker[i * 4] = value;
            checker[i * 4 + 1] = value;
            checker[i * 4 + 2] = value;
            checker[i * 4 + 3] = 255;
        }
        var linear = ManaMipChain.Build(checker, 2, 2, srgb: false)[0].Pixels;
        var colour = ManaMipChain.Build(checker, 2, 2, srgb: true)[0].Pixels;
        Check("data maps average their numbers", linear[0] == 128, $"{linear[0]}");
        Check("colour pictures average as light, so they do not darken far away", Math.Abs(colour[0] - 188) <= 1, $"{colour[0]}");
        Check("alpha averages plainly", colour[3] == 255);

        var odd = ManaMipChain.Build(new byte[3 * 3 * 4], 3, 3, srgb: true);
        Check("an odd size still ends at 1 x 1", odd[^1].Width == 1 && odd[^1].Height == 1 && odd.Count == 1);
    }
}
