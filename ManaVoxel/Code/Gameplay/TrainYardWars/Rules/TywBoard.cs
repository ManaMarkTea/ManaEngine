namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    public enum TywTerrain
    {
        Plains,
        Forest,
        Hills,
        Fields,
        Mountain,
        Lake,
        Town,
        Depot
    }

    public enum TywResource
    {
        Wood,
        Stone,
        Grain,
        Coal
    }

    public enum TywBuilding
    {
        None,
        Depot,
        Extractor,
        Factory,
        Station,
        Town
    }

    /// <summary>One hex of the board and everything standing on it.</summary>
    public sealed class TywTile
    {
        public TywTile(TywHex hex, TywTerrain terrain, int richness)
        {
            Hex = hex;
            Terrain = terrain;
            Richness = richness;
        }

        public TywHex Hex { get; }
        public TywTerrain Terrain { get; internal set; }

        /// <summary>1 to 3 pips on a resource hex; how fast its extractor fills.</summary>
        public int Richness { get; internal set; }

        /// <summary>The player whose track claims this hex, or -1. Towns are never claimed: anyone may run into one.</summary>
        public int Owner { get; internal set; } = -1;

        public TywBuilding Building { get; internal set; }

        /// <summary>Who owns the building (depot, extractor, station), or -1.</summary>
        public int BuildingOwner { get; internal set; } = -1;

        /// <summary>Loads waiting at an extractor.</summary>
        public float Stock { get; internal set; }

        /// <summary>Loads a train is already on its way to collect.</summary>
        public float Reserved { get; internal set; }

        public float DamagedUntil { get; internal set; }
        public float SabotageImmuneUntil { get; internal set; }

        /// <summary>A town's current want, and when it changes.</summary>
        public TywResource Demand { get; internal set; }
        public float DemandChangesAt { get; internal set; }

        /// <summary>A town just served takes nothing more until then.</summary>
        public float SatisfiedUntil { get; internal set; }

        public bool Wants(TywResource resource, float now) =>
            Building == TywBuilding.Town && SatisfiedUntil <= now && Demand == resource;

        /// <summary>Bumped whenever the track, building or damage on this hex changes, for the view to poll.</summary>
        public int Revision { get; internal set; }

        public bool IsPassable => Terrain != TywTerrain.Lake;

        public bool IsResource => Terrain is TywTerrain.Forest or TywTerrain.Hills or TywTerrain.Fields or TywTerrain.Mountain;

        /// <summary>A train may stop and turn round here (and track may meet at any angle).</summary>
        public bool IsTerminal => Building is not TywBuilding.None;

        public TywResource Produces => Terrain switch
        {
            TywTerrain.Forest => TywResource.Wood,
            TywTerrain.Hills => TywResource.Stone,
            TywTerrain.Fields => TywResource.Grain,
            _ => TywResource.Coal
        };

        public bool IsDamaged(float now) => DamagedUntil > now;
    }

    /// <summary>
    /// The hex map: a big hexagon of land, a few lakes, neutral towns spread through the middle and one depot per
    /// player on the rim. Seeded, so a match can be replayed and a test can count on its board.
    /// </summary>
    public sealed class TywBoard
    {
        private readonly Dictionary<TywHex, TywTile> _tiles = new();

        public TywBoard(int radius)
        {
            Radius = radius;
        }

        public int Radius { get; }

        public IEnumerable<TywTile> Tiles => _tiles.Values;

        public TywTile? this[TywHex hex] => _tiles.TryGetValue(hex, out var tile) ? tile : null;

        public bool Contains(TywHex hex) => _tiles.ContainsKey(hex);

        public static int RadiusFor(int players) => players <= 2 ? 4 : players <= 4 ? 5 : 6;

        public static TywBoard Generate(int players, int seed)
        {
            var random = new Random(seed);
            var board = new TywBoard(RadiusFor(players));
            foreach (var hex in TywHex.Spiral(board.Radius))
            {
                board._tiles[hex] = new TywTile(hex, PickTerrain(random), PickRichness(random));
            }

            // Depots evenly round the rim, starting at a random corner so seat 1 is not always east.
            var rim = Ring(board.Radius).ToList();
            var startIndex = random.Next(rim.Count);
            var depots = new List<TywHex>();
            for (var p = 0; p < players; p++)
            {
                var hex = rim[(startIndex + p * rim.Count / players) % rim.Count];
                var tile = board._tiles[hex];
                tile.Terrain = TywTerrain.Depot;
                tile.Richness = 0;
                tile.Building = TywBuilding.Depot;
                tile.BuildingOwner = p;
                tile.Owner = p;
                depots.Add(hex);
            }

            board.Depots = depots;

            // Towns in the interior, kept apart from each other and from the depots.
            var townCount = Math.Max(3, players);
            var candidates = TywHex.Spiral(board.Radius - 1).OrderBy(_ => random.Next()).ToList();
            var towns = new List<TywHex>();
            foreach (var hex in candidates)
            {
                if (towns.Count >= townCount)
                {
                    break;
                }

                if (towns.Any(t => t.DistanceTo(hex) < 3) || depots.Any(d => d.DistanceTo(hex) < 3))
                {
                    continue;
                }

                var tile = board._tiles[hex];
                tile.Terrain = TywTerrain.Town;
                tile.Richness = 0;
                tile.Building = TywBuilding.Town;
                tile.Demand = (TywResource)random.Next(4);
                towns.Add(hex);
            }

            board.Towns = towns;

            // Lakes stay clear of depots and towns, so nobody starts walled in.
            foreach (var tile in board._tiles.Values.Where(t => t.Terrain == TywTerrain.Lake))
            {
                if (depots.Concat(towns).Any(h => h.DistanceTo(tile.Hex) < 2))
                {
                    tile.Terrain = TywTerrain.Plains;
                    tile.Richness = 0;
                }
            }

            // Every depot gets each common resource, and coal, within reach: the Catan fairness pass.
            foreach (var depot in depots)
            {
                foreach (var need in new[] { TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Fields, TywTerrain.Mountain })
                {
                    var reach = need == TywTerrain.Mountain ? 4 : 3;
                    var near = board._tiles.Values.Where(t => t.Hex.DistanceTo(depot) <= reach && t.Hex.DistanceTo(depot) >= 2).ToList();
                    if (near.Any(t => t.Terrain == need))
                    {
                        continue;
                    }

                    var swap = near.Where(t => t.Terrain is TywTerrain.Plains or TywTerrain.Lake).OrderBy(_ => random.Next()).FirstOrDefault();
                    if (swap != null)
                    {
                        swap.Terrain = need;
                        swap.Richness = 2;
                    }
                }
            }

            foreach (var tile in board._tiles.Values.Where(t => !t.IsResource))
            {
                tile.Richness = 0;
            }

            return board;
        }

        public IReadOnlyList<TywHex> Depots { get; private set; } = Array.Empty<TywHex>();

        public IReadOnlyList<TywHex> Towns { get; private set; } = Array.Empty<TywHex>();

        public static IEnumerable<TywHex> Ring(int radius)
        {
            var hex = new TywHex(0, 0);
            for (var i = 0; i < radius; i++)
            {
                hex = hex.Neighbor(4);
            }

            for (var side = 0; side < TywHex.DirectionCount; side++)
            {
                for (var step = 0; step < radius; step++)
                {
                    yield return hex;
                    hex = hex.Neighbor(side);
                }
            }
        }

        private static TywTerrain PickTerrain(Random random)
        {
            var roll = random.Next(100);
            return roll switch
            {
                < 22 => TywTerrain.Forest,
                < 40 => TywTerrain.Hills,
                < 58 => TywTerrain.Fields,
                < 68 => TywTerrain.Mountain,
                < 74 => TywTerrain.Lake,
                _ => TywTerrain.Plains
            };
        }

        private static int PickRichness(Random random)
        {
            var roll = random.Next(10);
            return roll < 4 ? 1 : roll < 8 ? 2 : 3;
        }
    }
}
