namespace ManaCore.Graphics
{
    /// <summary>
    /// Where the app keeps its per-user settings files.
    /// <para>
    /// This was ManaImGuiSettingsStorage, and most of it was about ImGui's ini: which file a given UI
    /// scale's window positions and dock splits were saved to, and a layout-version stamp beside it. ImGui
    /// is gone and ManaUI persists its own layout as JSON through <c>SharedSettingsFilePath</c>, so what
    /// is left is the directory and one path helper.
    /// </para>
    /// </summary>
    public static class ManaUiSettingsStorage
    {
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

        /// <summary>
        /// A settings file in that directory. The names callers pass are unchanged from when this class was
        /// ImGui's -- including the ones that still read "imgui.*" -- so settings people already have on
        /// disk keep loading rather than silently resetting to defaults.
        /// </summary>
        public static string SharedSettingsFilePath(string fileName) => Path.Combine(SettingsDirectory, fileName);
    }
}
