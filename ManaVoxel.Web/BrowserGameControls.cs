#if MANA_BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ManaVoxel.Web
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

        [JSExport]
        internal static void Restart()
        {
            BrowserPlayGameMode.Current?.Restart();
        }
    }
}
#endif
