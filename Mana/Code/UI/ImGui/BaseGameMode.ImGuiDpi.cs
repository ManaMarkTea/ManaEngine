using ImGuiNET;
using ManaCore.Diagnostics;
using ManaCore.Graphics;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        private bool _uiScaleInitialized;

        public float CurrentUiScale => ManaImGuiSettingsStorage.CurrentUiScale;

        /// <summary>
        /// Main thread, once per frame before anything draws. On a DPI change (startup, or the window
        /// moving to a differently-scaled monitor) switches the UI over: that scale's saved layout,
        /// fonts rebuilt at the new pixel size, style metrics rescaled.
        /// </summary>
        protected void SyncImGuiUiScale()
        {
            var rawScale = ManaDpiScale.QueryUiScale(Context?.Window);
            if (!ManaImGuiSettingsStorage.WouldChangeUiScale(rawScale))
            {
                _uiScaleInitialized = true;
                return;
            }

            // Must save while IniFilePath still points at the old scale -- SetUiScale flips it.
            var previousIniPath = ManaImGuiSettingsStorage.IniFilePath;
            if (_uiScaleInitialized)
            {
                ImGui.SaveIniSettingsToDisk(previousIniPath);
            }

            var window = Context?.Window;
            ManaLog.WriteDiagnostic(
                $"[UI scale] {ManaImGuiSettingsStorage.CurrentUiScale:0.##} -> {ManaImGuiSettingsStorage.QuantizeUiScale(rawScale):0.##} " +
                $"(raw={rawScale:0.####}, monitorContentScale={(window != null ? ManaDpiScale.QueryContentScale(window) : 1f):0.####}, " +
                $"windowSize={window?.Size.X}x{window?.Size.Y}, framebuffer={window?.FramebufferSize.X}x{window?.FramebufferSize.Y})");

            ManaImGuiSettingsStorage.SetUiScale(rawScale);
            AdoptUiScaleLayout(previousIniPath);
            _uiScaleInitialized = true;

            // Rasterize at the real pixel size; io.FontGlobalScale would just stretch the atlas.
            RequestImGuiFontRebuild();

            // Rebuilds base metrics then rescales them, so padding tracks the font.
            ReapplyCurrentImGuiTheme();
        }

        // Runs before ImGui.NewFrame, the only safe place to swap dock settings -- loading an ini between
        // frames makes ImGui rebuild the dock tree cleanly next frame. Seeds rather than rebuilding
        // defaults, because DrawDockSpace's DockBuilderRemoveNode path destroys the central (viewport)
        // node when the tree already exists.
        private void AdoptUiScaleLayout(string previousIniPath)
        {
            if (IsSavedDockLayoutStale())
            {
                ManaImGuiSettingsStorage.DeleteIniFile();
            }

            var iniPath = ManaImGuiSettingsStorage.IniFilePath;
            if (!IsUsableDockLayout(iniPath))
            {
                TrySeedDockLayout(from: previousIniPath, to: iniPath);
            }

            if (IsUsableDockLayout(iniPath))
            {
                ImGui.LoadIniSettingsFromDisk(iniPath);
                // A seeded copy has no .layoutversion of its own and would read as stale next run.
                ManaImGuiSettingsStorage.WriteLayoutVersion(DockLayoutVersion);
                return;
            }

            // Nothing usable anywhere -- fall through to the "brand new install" path, which builds
            // defaults without any DockBuilder teardown.
            _dockLayoutInitialized = false;
            UtilityDockId = 0;
            OutlinerDockId = 0;
        }

        // Intact, not merely present. Every layout this app builds has exactly one central node (the
        // empty passthru node that IS the 3D viewport), so an ini without one is corrupt. Without this
        // check a single bad save is permanent: it loads, then saves back, forever.
        private static bool IsUsableDockLayout(string iniPath)
        {
            if (!File.Exists(iniPath))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(iniPath).Contains("CentralNode=1", StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void TrySeedDockLayout(string from, string to)
        {
            if (!IsUsableDockLayout(from))
            {
                return;
            }

            try
            {
                File.Copy(from, to, overwrite: true);
            }
            catch (IOException)
            {
                // Seeding is a convenience; the default-layout fallback is a perfectly good outcome.
            }
        }
    }
}
