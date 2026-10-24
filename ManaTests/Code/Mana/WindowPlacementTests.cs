using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaTests;

/// <summary>
/// ManaWindowPlacement: a torn-off panel window is left alone while its tab strip is on a monitor, and otherwise comes
/// wholly back onto the nearest one, moved as little as that allows.
/// </summary>
public static class WindowPlacementChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        // Two 1920x1080 monitors side by side, the second to the right.
        var monitors = new List<(int X, int Y, int Width, int Height)> { (0, 0, 1920, 1080), (1920, 0, 1920, 1080) };
        var size = (480, 640);

        check("a window on a monitor stays put", ManaWindowPlacement.Reachable((300, 200), size, monitors) == null, "");
        check("so does one straddling the two monitors", ManaWindowPlacement.Reachable((1700, 200), size, monitors) == null, "");
        check("and one hanging off the bottom, strip still showing", ManaWindowPlacement.Reachable((300, 900), size, monitors) == null, "");

        var above = ManaWindowPlacement.Reachable((300, -400), size, monitors);
        check("one whose strip is above the screen comes down to its top edge", above == (300, 0), $"{above}");

        var right = ManaWindowPlacement.Reachable((4200, 300), size, monitors);
        check("one past the last monitor comes all the way back onto it", right == (3840 - 480, 300), $"{right}");

        var unplugged = ManaWindowPlacement.Reachable((2500, 400), size, [(0, 0, 1920, 1080)]);
        check("one left on an unplugged monitor comes back onto the one still there", unplugged == (1920 - 480, 400), $"{unplugged}");

        var sliver = ManaWindowPlacement.Reachable((1900, 300), size, [(0, 0, 1920, 1080)]);
        check("a strip only a sliver on screen is brought fully back", sliver == (1920 - 480, 300), $"{sliver}");
        var faraway = ManaWindowPlacement.Reachable((-6000, -4000), size, monitors);
        check("and one thrown far off comes onto the nearest monitor whole", faraway == (0, 0), $"{faraway}");
    }
}
