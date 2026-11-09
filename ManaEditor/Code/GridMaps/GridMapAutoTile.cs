namespace ManaEditor.Code.GridMaps
{
    /// <summary>
    /// The sides of a cell, in the order a quarter turn walks them: a tile turned once moves what faced +X to
    /// face -Z, and so on round -- the way <see cref="ManaGridMapNode.TileTransform"/> turns tiles.
    /// </summary>
    [Flags]
    public enum GridMapSides
    {
        None = 0,

        /// <summary>+X.</summary>
        East = 1,

        /// <summary>-Z.</summary>
        North = 2,

        /// <summary>-X.</summary>
        West = 4,

        /// <summary>+Z.</summary>
        South = 8,

        All = East | North | West | South
    }

    /// <summary>A piece of an auto-tile set: a palette item, and the sides it joins its neighbours on when unturned.</summary>
    public sealed record GridMapAutoTilePiece(int Item, GridMapSides Sides);

    /// <summary>
    /// A set of palette items that pick themselves -- a road kit, a fence, a wall. Painting with the set puts
    /// down whichever piece, turned whichever way, joins the cell to its neighbours from the same set; and
    /// its neighbours re-pick as it goes in, so a road dragged out comes out with its bends and junctions.
    /// </summary>
    public sealed record GridMapAutoTileSet(string Name, IReadOnlyList<GridMapAutoTilePiece> Pieces)
    {
        public bool Contains(int item) => Pieces.Any(piece => piece.Item == item);

        /// <summary>Value equality, list included -- a record compares its list by reference.</summary>
        public bool SameAs(GridMapAutoTileSet other) =>
            Name == other.Name && Pieces.SequenceEqual(other.Pieces);
    }

    public static class GridMapAutoTile
    {
        /// <summary>Each side and the cell step across it, in turn order.</summary>
        public static readonly (GridMapSides Side, int X, int Z)[] Neighbours =
        [
            (GridMapSides.East, 1, 0),
            (GridMapSides.North, 0, -1),
            (GridMapSides.West, -1, 0),
            (GridMapSides.South, 0, 1)
        ];

        /// <summary>Where the sides face once turned by <paramref name="quarterTurns"/>.</summary>
        public static GridMapSides Rotate(GridMapSides sides, int quarterTurns)
        {
            var turns = ManaGridMapNode.WrapOrientation(quarterTurns);
            var bits = (int)sides & (int)GridMapSides.All;
            return (GridMapSides)(((bits << turns) | (bits >> (4 - turns))) & (int)GridMapSides.All);
        }

        public static int Count(GridMapSides sides) => System.Numerics.BitOperations.PopCount((uint)sides);

        /// <summary>
        /// The piece and turn that best joins the sides wanted: an exact match when the set has one, and
        /// otherwise the nearest -- a missing join counts worse than a join to nothing, so a set without a
        /// T-junction uses its crossroad rather than leaving a road cut.
        /// </summary>
        public static bool TryResolve(GridMapAutoTileSet set, GridMapSides wanted, out GridMapTile tile)
        {
            tile = default;
            var best = int.MaxValue;
            foreach (var piece in set.Pieces)
            {
                for (var turn = 0; turn < ManaGridMapNode.OrientationCount; turn++)
                {
                    var faces = Rotate(piece.Sides, turn);
                    var cost = Count(wanted & ~faces) * 3 + Count(faces & ~wanted) * 2;
                    if (cost < best)
                    {
                        best = cost;
                        tile = new GridMapTile(piece.Item, turn);
                    }
                }
            }

            return best != int.MaxValue;
        }

        /// <summary>A short name for a side set, "E+W" -- for tooltips and the level file.</summary>
        public static string Describe(GridMapSides sides)
        {
            if (sides == GridMapSides.None)
            {
                return "none";
            }

            return string.Join("+", Neighbours.Where(n => sides.HasFlag(n.Side)).Select(n => n.Side.ToString()[..1]));
        }

        /// <summary>Reads <see cref="Describe"/>'s form back, and a bare number too.</summary>
        public static GridMapSides Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return GridMapSides.None;
            }

            if (int.TryParse(text, out var number))
            {
                return (GridMapSides)number & GridMapSides.All;
            }

            var sides = GridMapSides.None;
            foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sides |= char.ToUpperInvariant(part[0]) switch
                {
                    'E' => GridMapSides.East,
                    'N' => GridMapSides.North,
                    'W' => GridMapSides.West,
                    'S' => GridMapSides.South,
                    _ => GridMapSides.None
                };
            }

            return sides;
        }
    }

    /// <summary>
    /// Ready-made auto-tile sets for the packs that ship in Assets/. Defaults only: every piece's model and
    /// sides can be changed in the Grid Map window afterwards.
    /// </summary>
    public static class GridMapAutoTilePresets
    {
        public sealed record Preset(string Name, IReadOnlyList<(string Asset, GridMapSides Sides)> Pieces);

        private const string CityRoads = "zip:kenney_city-kit-roads.zip/Models/GLB format/";

        /// <summary>
        /// Kenney's city kit roads. The sides are the ones each model is built open on, read off the models:
        /// a straight runs along X, a bend joins -X to +Z, a T-junction is closed at -Z.
        /// </summary>
        public static Preset Road { get; } = new("Road",
        [
            (CityRoads + "road-square.glb", GridMapSides.None),
            (CityRoads + "road-end.glb", GridMapSides.East),
            (CityRoads + "road-straight.glb", GridMapSides.East | GridMapSides.West),
            (CityRoads + "road-bend.glb", GridMapSides.West | GridMapSides.South),
            (CityRoads + "road-intersection.glb", GridMapSides.East | GridMapSides.West | GridMapSides.South),
            (CityRoads + "road-crossroad.glb", GridMapSides.All)
        ]);

        public static IReadOnlyList<Preset> All { get; } = [Road];

        /// <summary>Adds a preset's models to the map's palette and the set that uses them; returns the set's index.</summary>
        public static int AddTo(ManaGridMapNode gridMap, Preset preset)
        {
            var pieces = preset.Pieces.Select(piece => new GridMapAutoTilePiece(gridMap.AddPaletteItem(piece.Asset), piece.Sides)).ToList();
            return gridMap.AddAutoTileSet(new GridMapAutoTileSet(preset.Name, pieces));
        }
    }
}
