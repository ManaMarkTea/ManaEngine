namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// A pointy-top hexagonal lattice over a region's square footprint, in local east/south meters (see
    /// RegionTerrainMesh for the axis convention). Rows run east-west with odd rows shifted half a hex,
    /// which is the whole difference between a row/column pair and a honeycomb.
    ///
    /// Corners are addressable as exact integers (see CornerKey), measuring east in units of Width/2 and
    /// south in units of Radius/2 -- the mesh has to find the three hexes meeting at each corner, and
    /// quantizing float positions to do it would be fragile.
    /// </summary>
    public readonly struct RegionHexGrid
    {
        public const int CornersPerHex = 6;

        // Corner k at angle (30 + 60k) degrees in the (east, south) plane, in increasing angle so a fan of
        // (center, k, k+1) winds like the quad grid this replaced. Integer in the units above, not trig.
        private static readonly int[] CornerEastUnits = { 1, 0, -1, -1, 0, 1 };
        private static readonly int[] CornerSouthUnits = { 1, 2, 1, -1, -2, -1 };

        /// <summary>Center to corner.</summary>
        public float Radius { get; }

        /// <summary>Corner to opposite corner across the east axis -- also the east spacing between adjacent hex centers.</summary>
        public float Width { get; }

        /// <summary>South spacing between adjacent rows. Less than the hex's own height, because rows interlock.</summary>
        public float RowSpacing { get; }

        public int Columns { get; }
        public int Rows { get; }

        private readonly float _eastOrigin;
        private readonly float _southOrigin;

        public RegionHexGrid(float halfExtentMeters, float radiusMeters)
        {
            Radius = MathF.Max(radiusMeters, 0.01f);
            Width = MathF.Sqrt(3f) * Radius;
            RowSpacing = 1.5f * Radius;

            // Over-covers by a row and column each side; the caller drops cells whose centers fall outside
            // the box, and without the slack the survivors would leave a bald strip round the edge.
            Columns = (int)MathF.Ceiling((2f * halfExtentMeters) / Width) + 2;
            Rows = (int)MathF.Ceiling((2f * halfExtentMeters) / RowSpacing) + 2;

            // The stagger leaves the two row parities centered a quarter width apart -- split the difference.
            _eastOrigin = -((Columns - 1) * Width * 0.5f) - (Width * 0.25f);
            _southOrigin = -((Rows - 1) * RowSpacing * 0.5f);
        }

        public float CellEast(int column, int row)
        {
            return _eastOrigin + (column * Width) + (((row & 1) == 1) ? Width * 0.5f : 0f);
        }

        public float CellSouth(int row)
        {
            return _southOrigin + (row * RowSpacing);
        }

        public float CornerEast(int column, int row, int corner)
        {
            return CellEast(column, row) + (CornerEastUnits[corner] * Width * 0.5f);
        }

        public float CornerSouth(int row, int corner)
        {
            return CellSouth(row) + (CornerSouthUnits[corner] * Radius * 0.5f);
        }

        /// <summary>Integer identity of one corner -- the same value for all three hexes meeting there.</summary>
        public static long CornerKey(int column, int row, int corner)
        {
            var east = (2 * column) + (row & 1) + CornerEastUnits[corner];
            var south = (3 * row) + CornerSouthUnits[corner];
            return ((long)east << 32) | (uint)south;
        }
    }
}
