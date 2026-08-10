namespace ManaCore.Graphics
{
    public static class ManaImGuiSettingsStorage
    {
        // ImGui's ini stores window positions, sizes and dock-node splits in raw pixels, so a layout
        // saved at 100% is wrong at 150% and vice versa. Rather than trying to rescale a saved layout on
        // the fly (which has to guess at intent -- was that panel sized to fit its content, or to a
        // fraction of the screen?), each UI scale gets its OWN ini file and remembers its own layout
        // independently. Drag the window between a 100% and a 150% monitor and each side keeps the
        // arrangement it was last left in.
        //
        // Scale 1.0 deliberately keeps the plain, unsuffixed "imgui.ini" name so existing installs keep
        // the layout they already have instead of silently starting from defaults.
        public const float MinUiScale = 1.0f;
        public const float MaxUiScale = 4.0f;

        // Reported content scales are floats and can carry small representation noise; quantizing to the
        // quarter steps Windows and macOS actually offer (100/125/150/175/200%) stops a value wobbling in
        // its last decimal place from thrashing font-atlas rebuilds and ini files.
        private const float UiScaleQuantum = 0.25f;

        public static float CurrentUiScale { get; private set; } = 1.0f;

        public static string SettingsDirectory
        {
            get
            {
                var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }

                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    baseDirectory = Environment.CurrentDirectory;
                }

                var directory = Path.Combine(baseDirectory, "ManaRenderer");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public static string IniFilePath => Path.Combine(SettingsDirectory, IniFileName(CurrentUiScale));

        public static string LayoutVersionFilePath => Path.ChangeExtension(IniFilePath, ".layoutversion");

        /// <summary>
        /// A settings path that does NOT vary with UI scale -- for preferences that are about taste
        /// rather than layout geometry (the density preset, for one), which should follow the user across
        /// monitors instead of being remembered separately per scale.
        /// </summary>
        public static string SharedSettingsFilePath(string fileName) => Path.Combine(SettingsDirectory, fileName);

        /// <summary>Snaps a raw reported content scale to the quantized, clamped value this class actually stores.</summary>
        public static float QuantizeUiScale(float rawScale)
        {
            if (!float.IsFinite(rawScale) || rawScale <= 0f)
            {
                return 1.0f;
            }

            var snapped = MathF.Round(rawScale / UiScaleQuantum) * UiScaleQuantum;
            return Math.Clamp(snapped, MinUiScale, MaxUiScale);
        }

        public static bool WouldChangeUiScale(float rawScale) => QuantizeUiScale(rawScale) != CurrentUiScale;

        /// <summary>
        /// Switches which scale's settings are in play. A caller holding live ImGui state must save the
        /// OUTGOING layout BEFORE calling this -- every path property above flips the instant it returns.
        /// Returns true if the scale actually changed.
        /// </summary>
        public static bool SetUiScale(float rawScale)
        {
            var quantized = QuantizeUiScale(rawScale);
            if (quantized == CurrentUiScale)
            {
                return false;
            }

            CurrentUiScale = quantized;
            return true;
        }

        private static string IniFileName(float scale)
        {
            if (scale == 1.0f)
            {
                return "imgui.ini";
            }

            var percent = (int)MathF.Round(scale * 100f);
            return $"imgui.{percent}.ini";
        }

        public static void DeleteIniFile()
        {
            var path = IniFilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static int? ReadLayoutVersion()
        {
            var path = LayoutVersionFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            return int.TryParse(File.ReadAllText(path).Trim(), out var version) ? version : null;
        }

        public static void WriteLayoutVersion(int version)
        {
            File.WriteAllText(LayoutVersionFilePath, version.ToString());
        }
    }
}
