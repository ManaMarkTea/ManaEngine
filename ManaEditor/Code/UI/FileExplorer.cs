using System.ComponentModel;
using System.Diagnostics;
using ManaGraphics.Code.Diagnostics;

namespace ManaEditor.Code.UI
{
    /// <summary>Reveals or opens a path in the OS file browser -- Explorer on Windows, Finder on macOS.</summary>
    public static class FileExplorer
    {
        /// <summary>
        /// Selects <paramref name="filePath"/> in its containing folder rather than opening it directly --
        /// lets the user see sibling files too, and works the same whether or not there is a default
        /// application associated with the file's extension.
        /// </summary>
        public static void Reveal(string filePath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", $"-R \"{filePath}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to reveal '{filePath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to select an entry inside a zip archive. Windows Explorer exposes zip files as folders, so
        /// "/select" can often target the virtual path directly; other platforms fall back to the archive.
        /// </summary>
        public static void RevealZipEntry(string zipPath, string entryPath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var virtualPath = Path.Combine(
                        zipPath,
                        entryPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
                            .TrimStart(Path.DirectorySeparatorChar));
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{virtualPath}\"") { UseShellExecute = true });
                    return;
                }

                Reveal(zipPath);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to reveal '{zipPath} :: {entryPath}': {ex.Message}");
            }
        }

        /// <summary>Opens <paramref name="path"/> itself, creating it first if it does not exist yet.</summary>
        public static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to open folder '{path}': {ex.Message}");
            }
        }
    }
}
