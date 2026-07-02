namespace ManaCore.Graphics
{
    public static class ManaImGuiSettingsStorage
    {
        public static string IniFilePath
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
                return Path.Combine(directory, "imgui.ini");
            }
        }

        public static string LayoutVersionFilePath => Path.ChangeExtension(IniFilePath, ".layoutversion");

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
