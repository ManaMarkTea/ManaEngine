namespace ManaEditor.Code.GridMaps
{
    /// <summary>
    /// The editor's grid map brush: which map is being painted, with what, facing which way, on which floor.
    /// <para>
    /// Static, like MeshImportRequests, because the two halves live far apart: the tool window turns painting on
    /// and picks the brush, and the editor game mode turns the pointer into cells. Only one map can be painted
    /// at a time, so one brush is all there is.
    /// </para>
    /// </summary>
    public static class GridMapEditing
    {
        /// <summary>The Blank brush: painting with it clears cells.</summary>
        public const int Blank = -1;

        /// <summary>The map being painted, or null when painting is off.</summary>
        public static ManaGridMapNode? Target { get; private set; }

        private static int _brush;
        private static int _autoTile = -1;

        /// <summary>The palette item painted with, or <see cref="Blank"/> to clear. Choosing one puts the auto-tile set down.</summary>
        public static int Brush
        {
            get => _brush;
            set
            {
                _brush = value;
                _autoTile = -1;
            }
        }

        /// <summary>The auto-tile set painted with (a road, say), or -1 when the brush is a palette item.</summary>
        public static int AutoTile
        {
            get => _autoTile;
            set => _autoTile = Math.Max(-1, value);
        }

        public static bool IsAutoTile => _autoTile >= 0;

        /// <summary>Quarter turns about Y the brush is rotated by.</summary>
        public static int Orientation { get; set; }

        /// <summary>The floor painted on -- the cell Y the pointer's ray is cut against.</summary>
        public static int Floor { get; set; }

        /// <summary>Whether the brush clears cells. Holding Shift clears with any brush.</summary>
        public static bool Erasing => !IsAutoTile && Brush == Blank;

        public static bool IsPainting(ManaGridMapNode gridMap) => ReferenceEquals(Target, gridMap);

        public static void Begin(ManaGridMapNode gridMap)
        {
            if (!ReferenceEquals(Target, gridMap))
            {
                Target = gridMap;
                Brush = Brush == Blank ? Blank : Math.Clamp(Brush, 0, Math.Max(0, gridMap.Palette.Count - 1));
                _autoTile = -1;
            }
        }

        public static void Stop() => Target = null;

        public static void Rotate(int quarterTurns) =>
            Orientation = ManaGridMapNode.WrapOrientation(Orientation + quarterTurns);
    }
}
