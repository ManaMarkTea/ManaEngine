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
        /// The command; CLAUDE.md ("Testing levels with scripts") says what each does. Levels and assets: open_level,
        /// open_prefab, open_asset, open_panel, open_tools, select_node, review_changes. View: move_camera, sky,
        /// wind, emission, screenshot. Play: play, stop_play, teleport_player, log_player, bow (hold / hold_up / release the bow's fire key). UI: import_filter (the Import panel's filter text). Checks: dirty_report, profile_reset,
        /// profile_dump. Pictures: gizmos. Editing: drop_to_floor, grid_autotile, grid_generate. Flow: wait, quit.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>open_level: level path, relative to the working directory.</summary>
        public string? Path { get; set; }

        /// <summary>select_node: the name of the scene node to select, which is what brings up the transform gizmo.</summary>
        public string? Name { get; set; }

        /// <summary>wait: number of rendered frames to hold before the next command. move_camera, sky: glide there over this many frames.</summary>
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

        /// <summary>grid_generate: big rooms' cells a side, when set; they are otherwise the preset's.</summary>
        public int BigSize { get; set; }

        /// <summary>move_camera, sky with frames: run the glide alongside the commands after it instead of holding them.</summary>
        public bool Background { get; set; }

        /// <summary>screenshot: the scene alone across the whole window, without the editor's panels or the HUD.</summary>
        public bool Clean { get; set; }

        // record: Path is a new folder of frames (omit it while recording to change clean/visible), Frames the frame
        // rate (30), Clean drops the panels, Visible false holds capture (a level loading) without ending the take.

        /// <summary>gizmos: whether editor helpers (axes, selection, markers) show -- off for a picture.</summary>
        public bool? Visible { get; set; }

        /// <summary>sky: exact simulated local date/time, in ISO 8601 form.</summary>
        public DateTimeOffset? SkyDateTime { get; set; }

        /// <summary>sky: where in the day to hold a day-night cycle, 0..1 (0 sunrise, 0.25 noon, 0.5 sunset).</summary>
        public float? TimeOfDay { get; set; }
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
