#if MANA_BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ManaCore.Browser
{
    /// <summary>
    /// A small string key/value store backed by the browser's own <c>localStorage</c>, for the handful of
    /// desktop settings files (docking layout, UI density preset) that would otherwise write to the wasm
    /// virtual filesystem and vanish on the next page load.
    /// <para>
    /// Lives here, in ManaCore, rather than beside <c>ManaBrowserInterop</c> in ManaGraphics: the callers
    /// (<c>ManaUiDensity</c> in Mana, <c>ManaUiWorkspaceLayout</c> in ManaEditor) are settings-file code with
    /// no graphics dependency, and neither of their projects references ManaGraphics -- ManaCore is the one
    /// layer both already sit on top of. Targets the same already-loaded <c>mana_interop.js</c> module
    /// ManaBrowserInterop registers (JSHost.ImportAsync registers a module name once; any assembly can
    /// declare its own <c>[JSImport]</c> against it afterward), so nothing extra needs loading here -- but
    /// that also means every caller depends on <c>ManaBrowserInterop.InitializeAsync()</c> having already
    /// run, same as the loading-screen bridge does.
    /// </para>
    /// <para>
    /// Every call is swallowed on failure, same reasoning as the loading-screen bridge: a settings write is
    /// never worth taking the editor down over, and a private-browsing tab or a full origin quota are both
    /// real ways for <c>localStorage</c> itself to throw.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("browser")]
    public static partial class ManaBrowserSettingsBridge
    {
        private const string ModuleName = "mana_interop";

        [JSImport("manaSettingsGet", ModuleName)]
        private static partial string? GetCore(string key);

        [JSImport("manaSettingsSet", ModuleName)]
        private static partial void SetCore(string key, string value);

        [JSImport("manaSettingsRemove", ModuleName)]
        private static partial void RemoveCore(string key);

        /// <summary>The stored value for <paramref name="key"/>, or null if there isn't one or the read failed.</summary>
        public static string? TryGet(string key)
        {
            try
            {
                return GetCore(key);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ManaBrowserSettingsBridge] Read of '{key}' failed: {exception.Message}");
                return null;
            }
        }

        public static void Set(string key, string value)
        {
            try
            {
                SetCore(key, value);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ManaBrowserSettingsBridge] Write of '{key}' failed: {exception.Message}");
            }
        }

        public static void Remove(string key)
        {
            try
            {
                RemoveCore(key);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ManaBrowserSettingsBridge] Remove of '{key}' failed: {exception.Message}");
            }
        }
    }
}
#endif
