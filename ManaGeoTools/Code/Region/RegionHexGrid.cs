namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// A pointy-top hexagonal lattice covering a region's square footprint, expressed in the region's
    /// local east/south meters (see RegionTerrainMesh for the axis convention).
    ///
    /// Rows run east-west at 1.5 * Radius apart and odd rows are shifted half a hex east -- that stagger
    /// is the whole difference between a plain row/column pair and a honeycomb. Everything else here is
    /// derived from Radius alone, so hex size stays a single knob.
    ///
    /// Corners are ALSO addressable as exact integers (see CornerKey). Three hexes meet at every interior
    /// corner and the mesh has to find those three to average their heights; doing that by quantizing a
    /// float position would be fragile, so the lattice is defined on an integer grid from the start --
    /// measuring east in units of Width/2 and south in units of Radius/2 puts every center and every
    /// corner on a whole number.
    /// </summary>
    public readonly struct RegionHexGrid
    {
        public const int CornersPerHex = 6;

        // Corner k sits at angle (30 + 60k) degrees in the (east, south) plane, listed in increasing
        // angle so that a fan of (center, corner k, corner k+1) triangles winds the same way as the quad
        // grid this replaced. cos/sin of those angles are exactly +-1 and +-1/2 in the units above, which
        // is why these are integer tables rather than trig.
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

            // Deliberately over-covers by a row and column on each side: the caller then drops the cells
            // whose centers fall outside the box, and without the slack the outermost surviving centers
            // would sit well short of the edge and leave a bald strip all the way round.
            Columns = (int)MathF.Ceiling((2f * halfExtentMeters) / Width) + 2;
            Rows = (int)MathF.Ceiling((2f * halfExtentMeters) / RowSpacing) + 2;

            // The odd-row stagger leaves the two row parities centered a quarter width apart, so the
            // lattice as a whole is centered by splitting that difference.
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

        /// <summary>
        /// The integer identity of one hex corner -- the same value for all three hexes that meet there,
        /// which is what lets the mesh average their heights into a single shared corner height.
        /// </summary>
        public static long CornerKey(int column, int row, int corner)
        {
            var east = (2 * column) + (row & 1) + CornerEastUnits[corner];
            var south = (3 * row) + CornerSouthUnits[corner];
            return ((long)east << 32) | (uint)south;
        }
    }
}
