using Mana.Code.Nodes;

namespace ManaEditor.Code.Terrain
{
    /// <summary>
    /// The editor's terrain brush: which terrain is being sculpted and with what. Static, like GridMapEditing: the
    /// Terrain window sets it and the editor's input controller turns the pointer into dabs.
    /// </summary>
    public static class TerrainEditing
    {
        /// <summary>The terrain being sculpted, or null when sculpting is off (the pointer selects and moves).</summary>
        public static ManaTerrainNode? Target { get; private set; }

        public static ManaTerrainBrush Brush { get; set; } = ManaTerrainBrush.Raise;

        /// <summary>Brush radius in metres.</summary>
        public static float Size { get; set; } = 4.0f;

        /// <summary>0 to 1: how quickly the brush works.</summary>
        public static float Strength { get; set; } = 0.5f;

        /// <summary>0 is a hard-edged disc, 1 fades all the way from the centre.</summary>
        public static float Falloff { get; set; } = 0.6f;

        /// <summary>The layer Paint lays down: 0 grass, 1 dirt, 2 rock, 3 sand.</summary>
        public static int Layer { get; set; } = 1;

        /// <summary>Where the brush is over the terrain, in world space, for the ring; null when off it.</summary>
        public static Vector3? Hover { get; set; }

        public static readonly string[] LayerNames = ["Grass", "Dirt", "Rock", "Sand"];

        public static bool IsSculpting(ManaTerrainNode terrain) => ReferenceEquals(Target, terrain);

        public static void Begin(ManaTerrainNode terrain) => Target = terrain;

        public static void Stop()
        {
            Target = null;
            Hover = null;
        }
    }
}
