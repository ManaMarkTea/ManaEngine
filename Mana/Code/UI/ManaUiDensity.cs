using ManaCore.Graphics;
using ManaUI;
#if MANA_BROWSER
using ManaCore.Browser;
#endif

namespace Mana
{
    public enum ManaUiDensityPreset
    {
        Comfortable,
        Compact
    }

    /// <summary>
    /// How tightly the UI packs its rows -- a taste preference, remembered across runs.
    /// <para>
    /// This used to be a table of raw pixel measurements pushed as ImGui style vars, each one DPI-scaled
    /// on the way out because ImGui's own ScaleAllSizes could not reach them. ManaUI has the same four
    /// numbers as theme tokens already, so the preset now nudges those instead and the table is gone.
    /// Multiplicative rather than absolute, so a theme with its own chunkier metrics (LCARS) stays
    /// recognisably itself when compacted rather than collapsing to the base theme's sizes.
    /// </para>
    /// </summary>
    public static class ManaUiDensity
    {
        public static ManaUiDensityPreset CurrentPreset { get; private set; } = ManaUiDensityPreset.Comfortable;

        /// <summary>
        /// Raised when the preset changes, so a host can re-apply it. Needed because the tokens are read
        /// as controls are constructed, not at render time -- the same reason switching theme rebuilds.
        /// </summary>
        public static event EventHandler? PresetChanged;

        // Deliberately NOT a per-UI-scale path: density is a taste preference, not layout geometry, so it
        // follows the user across monitors. The filename is unchanged from the ImGui era so preferences
        // already on disk still load.
        private static string DensityPresetFilePath => ManaUiSettingsStorage.SharedSettingsFilePath("imgui.density");

        /// <summary>
        /// Tightens a theme's metrics for the Compact preset, or leaves it alone for Comfortable. Call it
        /// on a freshly created theme, before anything reads it.
        /// </summary>
        public static void Apply(UiTheme theme)
        {
            ArgumentNullException.ThrowIfNull(theme);

            if (CurrentPreset != ManaUiDensityPreset.Compact)
            {
                return;
            }

            // Vertical only. Compacting horizontally costs legibility -- a label needs the width it needs --
            // while vertical space is the one a docked panel actually runs out of.
            theme.ControlHeight = MathF.Max(18.0f, theme.ControlHeight * 0.85f);
            theme.Spacing = MathF.Max(2.0f, theme.Spacing * 0.7f);
            theme.Padding = new Thickness(
                theme.Padding.Left,
                MathF.Max(2.0f, theme.Padding.Top * 0.6f),
                theme.Padding.Right,
                MathF.Max(2.0f, theme.Padding.Bottom * 0.6f));
            theme.ControlPadding = new Thickness(
                theme.ControlPadding.Left,
                MathF.Max(1.0f, theme.ControlPadding.Top * 0.6f),
                theme.ControlPadding.Right,
                MathF.Max(1.0f, theme.ControlPadding.Bottom * 0.6f));
            theme.LineSpacing = MathF.Max(1.0f, theme.LineSpacing - 0.05f);
        }

        public static void SetPreset(ManaUiDensityPreset preset, bool persist = true)
        {
            if (CurrentPreset == preset)
            {
                return;
            }

            CurrentPreset = preset;

            if (persist)
            {
                PersistPreset();
            }

            PresetChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void RestorePreset()
        {
#if MANA_BROWSER
            var text = ManaBrowserSettingsBridge.TryGet(DensitySettingsKey)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
#else
            var path = DensityPresetFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var text = File.ReadAllText(path).Trim();
#endif
            if (!Enum.TryParse<ManaUiDensityPreset>(text, ignoreCase: true, out var preset))
            {
                return;
            }

            SetPreset(preset, persist: false);
        }

        // Filename-derived, not a separate literal: keeps this in step with DensityPresetFilePath without
        // the two ever being able to drift apart.
        private static string DensitySettingsKey => Path.GetFileName(DensityPresetFilePath);

        private static void PersistPreset()
        {
#if MANA_BROWSER
            ManaBrowserSettingsBridge.Set(DensitySettingsKey, CurrentPreset.ToString());
#else
            File.WriteAllText(DensityPresetFilePath, CurrentPreset.ToString());
#endif
        }
    }
}
