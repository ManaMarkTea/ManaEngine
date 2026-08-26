using ManaCore.Graphics;
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

    public static class ManaUiDensity
    {
        public static ManaUiDensityPreset CurrentPreset { get; private set; } = ManaUiDensityPreset.Comfortable;

        // Deliberately NOT derived from ManaImGuiSettingsStorage.IniFilePath (which now varies per UI
        // scale): density is a taste preference, not layout geometry, so it should follow the user across
        // monitors rather than being remembered separately at each scale. The filename is unchanged from
        // when it was derived, so existing saved preferences still load.
        private static string DensityPresetFilePath => ManaImGuiSettingsStorage.SharedSettingsFilePath("imgui.density");

        // Every value below is a raw pixel measurement pushed as an ImGui style var, so each one is
        // DPI-scaled on the way out. ImGuiStyle.ScaleAllSizes (applied in BaseGameMode.ConfigImStyle)
        // only reaches the style struct's own fields -- it can't see these.
        private static float Scaled(float pixels) => pixels * ManaImGuiSettingsStorage.CurrentUiScale;

        // Property table row spacing used by Inspector and Irradiance panels.
        public static float PropertyTableCellPaddingY => Scaled(CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        });

        // Vector/color compound editor spacing (axis badges + value fields + color swatch).
        public static float CompactEditorFramePaddingY => Scaled(CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 5.0f,
            ManaUiDensityPreset.Compact => 3.5f,
            _ => 5.0f
        });

        public static float CompactEditorItemSpacingY => Scaled(CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        });

        // Scalar value editor spacing (sliders, drags, inputs, checkboxes, combos).
        public static float CompactValueFramePaddingY => Scaled(CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 5.0f,
            ManaUiDensityPreset.Compact => 3.5f,
            _ => 5.0f
        });

        public static float CompactValueItemSpacingY => Scaled(CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        });

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
