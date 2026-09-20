using System.Text.Json;

namespace ManaVoxel.Code.Automation
{
    /// <summary>
    /// One step of a <c>--command-file</c> script (see <see cref="ManaCommandRunner"/>). A single class
    /// covers every command kind rather than a discriminated hierarchy -- the set is small and flat, and
    /// each field is simply left null/default for commands that don't use it.
    /// </summary>
    public sealed class ManaCommand
    {
        /// <summary>
        /// One of: open_level, wait, move_camera, select_node, screenshot, quit, play, stop_play, profile_reset,
        /// profile_dump (writes the frame profiler's per-scope report and average frame rate to <see cref="Path"/>).
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>open_level: level path, relative to the working directory.</summary>
        public string? Path { get; set; }

        /// <summary>select_node: the name of the scene node to select, which is what brings up the transform gizmo.</summary>
        public string? Name { get; set; }

        /// <summary>wait: number of rendered frames to hold before the next command.</summary>
        public int Frames { get; set; }

        /// <summary>move_camera: world-space camera position.</summary>
        public float[]? Position { get; set; }

        /// <summary>move_camera: optional world-space point to face; omit to leave rotation untouched.</summary>
        public float[]? Target { get; set; }
    }

    public static class ManaCommandFile
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static List<ManaCommand> Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ManaCommand>>(json, SerializerOptions)
                ?? throw new InvalidDataException($"Command file '{path}' did not contain a JSON array.");
        }
    }
}
