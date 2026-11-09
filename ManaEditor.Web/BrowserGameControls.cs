#if MANA_BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ManaEditor.Web
{
    /// <summary>
    /// What the page's in-game menu (main.js) asks of the game: pause while it is open, and restart. Choosing another
    /// level is the page's own business -- it goes back to the selector, which starts a fresh game.
    /// </summary>
    [SupportedOSPlatform("browser")]
    public static partial class BrowserGameControls
    {
        [JSExport]
        internal static void SetPaused(bool paused)
        {
            BrowserPlayGameMode.Paused = paused;
        }

        /// <summary>True when the level's game mode handles Escape itself, so the page should not open its menu.</summary>
        [JSExport]
        internal static bool ModeHandlesEscape() => BrowserPlayGameMode.Current?.ModeHandlesEscape == true;

        /// <summary>The on-screen controls the level's modes want now: stick, swipe, pointer or twin.</summary>
        [JSExport]
        internal static string TouchControls() => BrowserPlayGameMode.Current?.TouchControlsName ?? "stick";

        /// <summary>The layout only fits on its side, so a phone held upright should be asked to turn.</summary>
        [JSExport]
        internal static bool PrefersLandscape() => BrowserPlayGameMode.Current?.PrefersLandscape == true;

        /// <summary>The aiming stick: x and y up to 1, screen directions; zero when let go.</summary>
        [JSExport]
        internal static void SetTouchAim(double x, double y)
        {
            BrowserPlayGameMode.Current?.SetTouchAim(new Vector2((float)x, (float)y));
        }

        [JSExport]
        internal static void Restart()
        {
            BrowserPlayGameMode.Current?.Restart();
        }
    }
}
#endif
