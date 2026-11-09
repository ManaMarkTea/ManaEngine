#if !MANA_BROWSER
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ManaEditor.Code.Imports
{
    /// <summary>
    /// FBX to glTF through Godot's FBX2glTF (BSD; the Autodesk FBX SDK inside it is free to redistribute), fetched once
    /// into <c>Library/Tools/FBX2glTF</c> for this platform. The Mac build is x86-64 and runs under Rosetta.
    /// </summary>
    public static class ManaFbxConverter
    {
        private const string Release = "https://github.com/godotengine/FBX2glTF/releases/download/v0.13.1/";

        private static string Platform
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "windows-x86_64";
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "macos-x86_64";
                }
                return "linux-x86_64";
            }
        }

        /// <summary>The converter's executable, downloaded on first use.</summary>
        public static async Task<string> EnsureAsync(string libraryFolder, IProgress<string>? progress, CancellationToken token)
        {
            var name = $"FBX2glTF-{Platform}";
            var folder = Path.Combine(libraryFolder, "Tools", "FBX2glTF");
            var executable = Path.Combine(folder, name, name);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                executable += ".exe";
            }
            if (File.Exists(executable))
            {
                return executable;
            }

            progress?.Report("Downloading FBX2glTF (once)");
            Directory.CreateDirectory(folder);
            var archive = Path.Combine(folder, name + ".zip");
            using (var http = new HttpClient())
            {
                var bytes = await http.GetByteArrayAsync(Release + name + ".zip", token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(archive, bytes, token).ConfigureAwait(false);
            }
            ZipFile.ExtractToDirectory(archive, folder, overwriteFiles: true);
            File.Delete(archive);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException($"FBX2glTF's archive held no {Path.GetFileName(executable)}.");
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
            }
            return executable;
        }

        /// <summary>Converts <paramref name="fbx"/>; returns the .gltf it wrote (beside its buffer, in <c>&lt;stem&gt;_out/</c>).</summary>
        public static async Task<string> ConvertAsync(string executable, string fbx, string outputFolder, CancellationToken token)
        {
            var stem = Path.GetFileNameWithoutExtension(fbx);
            Directory.CreateDirectory(outputFolder);
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "--input", fbx, "--output", Path.Combine(outputFolder, stem), "--pbr-metallic-roughness",
                "--long-indices", "auto", "-k", "position", "-k", "normal", "-k", "uv0"
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("FBX2glTF did not start.");
            // Drained, or a full pipe stalls it: it prints a line for every material of every mesh.
            var errors = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, line) =>
            {
                if (line.Data != null)
                {
                    lock (errors)
                    {
                        errors.AppendLine(line.Data);
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            var gltf = Path.Combine(outputFolder, stem + "_out", stem + ".gltf");
            if (process.ExitCode != 0 || !File.Exists(gltf))
            {
                throw new InvalidDataException($"FBX2glTF could not convert {Path.GetFileName(fbx)} (exit {process.ExitCode}): {errors.ToString().Trim()}");
            }
            return gltf;
        }
    }
}
#endif
