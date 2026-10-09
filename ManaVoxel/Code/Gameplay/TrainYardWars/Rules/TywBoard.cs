namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    public enum TywTerrain
    {
        Plains,
        Forest,
        Hills,
        Fields,
        Mountain,
        Water,
        Sand,
        Desert,
        Town,
        Depot,
        Port
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
        Town,
        Port
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

        /// <summary>Yield of a resource hex, 1 (poor) to 3 (rich): how fast its extractor fills.</summary>
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

        /// <summary>Who has a torch burning here, or -1. A torch lights the mines around it, and they work faster.</summary>
        public int TorchOwner { get; internal set; } = -1;

        public bool HasTorch => TorchOwner >= 0;

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

        /// <summary>Track can run here: anywhere on land, and out onto the water at a port.</summary>
        public bool IsPassable => Terrain != TywTerrain.Water;

        /// <summary>A place anyone may run track into: a town or a port.</summary>
        public bool IsShared => Building is TywBuilding.Town or TywBuilding.Port;

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
    /// The world: a continent from <see cref="TywMapGen"/>, ringed by sea; depots spread as far apart as the land
    /// allows, neutral towns across it, ports out on the water. Seeded, so a match can be replayed and a test can
    /// count on its board.
    /// </summary>
    public sealed class TywBoard
    {
        private readonly Dictionary<TywHex, TywTile> _tiles = new();

        public TywBoard(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public int Columns { get; }
        public int Rows { get; }

        public IEnumerable<TywTile> Tiles => _tiles.Values;

        public TywTile? this[TywHex hex] => _tiles.TryGetValue(hex, out var tile) ? tile : null;

        public bool Contains(TywHex hex) => _tiles.ContainsKey(hex);

        public IReadOnlyList<TywHex> Depots { get; private set; } = Array.Empty<TywHex>();
        public IReadOnlyList<TywHex> Towns { get; private set; } = Array.Empty<TywHex>();
        public IReadOnlyList<TywHex> Ports { get; private set; } = Array.Empty<TywHex>();

        public static (int Columns, int Rows) SizeFor(int players) => players switch
        {
            <= 2 => (24, 18),
            <= 4 => (32, 22),
            <= 6 => (38, 26),
            _ => (44, 30)
        };

        public static TywBoard Generate(int players, int seed)
        {
            var random = new Random(seed);
            var (columns, rows) = SizeFor(players);
            var board = new TywBoard(columns, rows);
            foreach (var (hex, kind) in TywMapGen.Collapse(columns, rows, seed))
            {
                board._tiles[hex] = new TywTile(hex, kind, 0);
            }

            board.KeepMainland();
            foreach (var tile in board._tiles.Values.Where(t => t.IsResource))
            {
                var roll = random.Next(10);
                tile.Richness = roll < 4 ? 1 : roll < 8 ? 2 : 3;
            }

            board.PlaceDepots(players, random);
            board.PlaceTowns(players, random);
            board.PlacePorts(players, random);
            return board;
        }

        public IEnumerable<TywTile> Around(TywHex hex) =>
            Enumerable.Range(0, TywHex.DirectionCount).Select(d => this[hex.Neighbor(d)]).OfType<TywTile>();

        private static bool IsLand(TywTile tile) => tile.Terrain is not (TywTerrain.Water or TywTerrain.Port);

        // Islands nobody could lay track to are sunk, so every bit of land is reachable.
        private void KeepMainland()
        {
            var seen = new HashSet<TywHex>();
            var largest = new List<TywTile>();
            foreach (var start in _tiles.Values.Where(IsLand))
            {
                if (!seen.Add(start.Hex))
                {
                    continue;
                }

                var island = new List<TywTile>();
                var queue = new Queue<TywTile>();
                queue.Enqueue(start);
                while (queue.TryDequeue(out var tile))
                {
                    island.Add(tile);
                    foreach (var next in Around(tile.Hex).Where(n => IsLand(n) && seen.Add(n.Hex)))
                    {
                        queue.Enqueue(next);
                    }
                }

                if (island.Count > largest.Count)
                {
                    largest = island;
                }
            }

            var keep = largest.Select(t => t.Hex).ToHashSet();
            foreach (var tile in _tiles.Values.Where(t => IsLand(t) && !keep.Contains(t.Hex)))
            {
                tile.Terrain = TywTerrain.Water;
            }
        }

        // Each depot as far from the others as the land allows, on open ground away from the coast.
        private void PlaceDepots(int players, Random random)
        {
            var sites = _tiles.Values
                .Where(t => t.Terrain is TywTerrain.Plains or TywTerrain.Fields or TywTerrain.Desert && Around(t.Hex).All(IsLand))
                .ToList();
            if (sites.Count < players)
            {
                sites = _tiles.Values.Where(IsLand).ToList();
            }

            var depots = new List<TywHex> { sites[random.Next(sites.Count)].Hex };
            while (depots.Count < players)
            {
                depots.Add(sites.Where(s => !depots.Contains(s.Hex)).MaxBy(s => depots.Min(d => d.DistanceTo(s.Hex)))!.Hex);
            }

            // The first was a random pick: move it to wherever is farthest from the rest.
            var others = depots.Skip(1).ToList();
            if (others.Count > 0)
            {
                depots[0] = sites.Where(s => !others.Contains(s.Hex)).MaxBy(s => others.Min(d => d.DistanceTo(s.Hex)))!.Hex;
            }

            for (var p = 0; p < players; p++)
            {
                var tile = _tiles[depots[p]];
                tile.Terrain = TywTerrain.Depot;
                tile.Richness = 0;
                tile.Building = TywBuilding.Depot;
                tile.BuildingOwner = p;
                tile.Owner = p;
                EnsureNearby(depots[p], random);
            }

            Depots = depots;
        }

        // A fair start: wood, stone, grain and coal all within reach of a depot, turning a nearby hex if need be
        // (the one whose neighbours suit the new terrain best, so the bands still read).
        private void EnsureNearby(TywHex depot, Random random)
        {
            foreach (var need in new[] { TywTerrain.Forest, TywTerrain.Hills, TywTerrain.Fields, TywTerrain.Mountain })
            {
                var near = _tiles.Values.Where(t => t.Hex.DistanceTo(depot) is >= 2 and <= 5).ToList();
                if (near.Any(t => t.Terrain == need))
                {
                    continue;
                }

                var swap = near
                    .Where(t => t.Building == TywBuilding.None && t.Terrain is TywTerrain.Plains or TywTerrain.Desert or TywTerrain.Fields or TywTerrain.Hills or TywTerrain.Forest)
                    .OrderByDescending(t => Around(t.Hex).Count(n => TywMapGen.MayTouch(need, n.Terrain)))
                    .ThenBy(_ => random.Next())
                    .FirstOrDefault();
                if (swap != null)
                {
                    swap.Terrain = need;
                    swap.Richness = 2;
                }
            }
        }

        private void PlaceTowns(int players, Random random)
        {
            var count = players * 2 + 2;
            var towns = new List<TywHex>();
            foreach (var tile in _tiles.Values
                         .Where(t => t.Building == TywBuilding.None && t.Terrain is TywTerrain.Plains or TywTerrain.Fields or TywTerrain.Sand or TywTerrain.Desert)
                         .OrderBy(_ => random.Next()))
            {
                if (towns.Count >= count)
                {
                    break;
                }

                if (towns.Any(t => t.DistanceTo(tile.Hex) < 5) || Depots.Any(d => d.DistanceTo(tile.Hex) < 4))
                {
                    continue;
                }

                tile.Terrain = TywTerrain.Town;
                tile.Richness = 0;
                tile.Building = TywBuilding.Town;
                tile.Demand = (TywResource)random.Next(4);
                towns.Add(tile.Hex);
            }

            Towns = towns;
        }

        // Ports sit out on the water in a bay: sea hexes with open land on two or more sides, spread round the coast.
        private void PlacePorts(int players, Random random)
        {
            var count = players + 2;
            var ports = new List<TywHex>();
            foreach (var tile in _tiles.Values
                         .Where(t => t.Terrain == TywTerrain.Water && Around(t.Hex).Count(n => IsLand(n) && n.Building == TywBuilding.None) >= 2)
                         .OrderBy(_ => random.Next()))
            {
                if (ports.Count >= count)
                {
                    break;
                }

                if (ports.Any(p => p.DistanceTo(tile.Hex) < 7) || Depots.Any(d => d.DistanceTo(tile.Hex) < 3))
                {
                    continue;
                }

                tile.Terrain = TywTerrain.Port;
                tile.Building = TywBuilding.Port;
                ports.Add(tile.Hex);
            }

            Ports = ports;
        }

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
    }
}
