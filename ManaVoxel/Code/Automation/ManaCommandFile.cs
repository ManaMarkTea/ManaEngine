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
        /// profile_dump (writes the frame profiler's per-scope report and average frame rate to <see cref="Path"/>),
        /// grid_autotile (adds a preset set to a grid map and drags it through <see cref="Cells"/>),
        /// grid_generate (opens the Generator on the grid map named by <see cref="Name"/> and generates with the dungeon preset).
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

        /// <summary>grid_autotile: the auto-tile preset to add to the map named by <see cref="Name"/> ("Road").</summary>
        public string? Preset { get; set; }

        /// <summary>grid_autotile: [x, z] cells to drag the set through, in order, on floor 0.</summary>
        public int[][]? Cells { get; set; }

        /// <summary>grid_generate: the seed, and [width, depth] of the area from cell (0, 0).</summary>
        public int Seed { get; set; } = 1;

        public int[]? Size { get; set; }
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
