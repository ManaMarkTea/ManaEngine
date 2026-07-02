using ManaCore.Graphics;

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

        private static string DensityPresetFilePath => Path.ChangeExtension(ManaImGuiSettingsStorage.IniFilePath, ".density");

        // Property table row spacing used by Inspector and Irradiance panels.
        public static float PropertyTableCellPaddingY => CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        };

        // Vector/color compound editor spacing (axis badges + value fields + color swatch).
        public static float CompactEditorFramePaddingY => CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 5.0f,
            ManaUiDensityPreset.Compact => 3.5f,
            _ => 5.0f
        };

        public static float CompactEditorItemSpacingY => CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        };

        // Scalar value editor spacing (sliders, drags, inputs, checkboxes, combos).
        public static float CompactValueFramePaddingY => CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 5.0f,
            ManaUiDensityPreset.Compact => 3.5f,
            _ => 5.0f
        };

        public static float CompactValueItemSpacingY => CurrentPreset switch
        {
            ManaUiDensityPreset.Comfortable => 4.0f,
            ManaUiDensityPreset.Compact => 2.5f,
            _ => 4.0f
        };

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
            var path = DensityPresetFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var text = File.ReadAllText(path).Trim();
            if (!Enum.TryParse<ManaUiDensityPreset>(text, ignoreCase: true, out var preset))
            {
                return;
            }

            SetPreset(preset, persist: false);
        }

        private static void PersistPreset()
        {
            File.WriteAllText(DensityPresetFilePath, CurrentPreset.ToString());
        }
    }
}
