namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    /// <summary>
    /// A greedy player: grabs the nearest rich hexes, buys a train per mine, now and then cuts a rival's line.
    /// Good enough to fill empty seats and to play whole matches in the tests.
    /// </summary>
    public sealed class TywBot
    {
        private readonly int _player;
        private readonly Random _random;
        private float _nextThinkAt;

        // A town or port it has set out for: it keeps laying toward it, a few hexes at a time, until it arrives.
        private TywHex? _goal;

        public TywBot(int player, int seed)
        {
            _player = player;
            _random = new Random(seed * 31 + player);
            _nextThinkAt = 0.5f + player * 0.25f;
        }

        /// <summary>How often it acts, in seconds; lower is a harder bot.</summary>
        public float ThinkSeconds { get; set; } = 1.4f;

        public void Tick(TywGame game)
        {
            if (game.IsOver || game.Time < _nextThinkAt)
            {
                return;
            }

            _nextThinkAt = game.Time + ThinkSeconds * (0.7f + 0.6f * (float)_random.NextDouble());
            Think(game);
        }

        private void Think(TywGame game)
        {
            var me = game.Players[_player];
            var mine = game.Board.Tiles.Where(t => t.Owner == _player).ToList();

            foreach (var tile in mine.Where(t => t.IsDamaged(game.Time) && t.DamagedUntil - game.Time > 6.0f))
            {
                if (game.Act(_player, tile.Hex) == null)
                {
                    return;
                }
            }

            // Mines on the hexes it owns -- though once it has a few, not on poor ground the track merely crosses:
            // each mine costs more than the last.
            var mineCount = game.Board.Tiles.Count(t => t.BuildingOwner == _player && t.Building is TywBuilding.Extractor or TywBuilding.Factory);
            foreach (var tile in mine.Where(t => t.IsResource && t.Building == TywBuilding.None && (t.Richness >= 2 || mineCount < 3))
                         .OrderByDescending(t => t.Richness))
            {
                if (me.CanAfford(game.ExtractorCost(_player)))
                {
                    game.Act(_player, tile.Hex);
                    return;
                }
            }

            var extractors = game.Board.Tiles.Count(t => t.BuildingOwner == _player && t.Building is TywBuilding.Extractor or TywBuilding.Factory);
            if (game.TrainCount(_player) < Math.Max(1, (extractors + 1) * 2 / 3 + 1) && game.BuyTrain(_player) == null)
            {
                return;
            }

            // Light the richest dark mine, once coal is no longer scarce.
            if (game.TorchCount(_player) < TywGame.MaxTorches && me.Bank[(int)TywResource.Coal] >= 3 && me.CanAfford(TywCosts.Torch))
            {
                var dark = game.Board.Tiles
                    .Where(t => t.BuildingOwner == _player && t.Building is TywBuilding.Extractor or TywBuilding.Factory && !game.IsLit(t) && !t.HasTorch)
                    .OrderByDescending(t => t.Richness)
                    .FirstOrDefault();
                if (dark != null && game.PlaceTorch(_player, dark.Hex) == null)
                {
                    return;
                }
            }

            if (Expand(game, me, mine))
            {
                return;
            }

            var factory = game.Board.Tiles.FirstOrDefault(t => t.BuildingOwner == _player && t.Building == TywBuilding.Extractor && t.Richness >= 2);
            if (factory != null && me.CanAfford(TywCosts.Factory) && me.Bank.Sum() > 10)
            {
                game.Act(_player, factory.Hex);
                return;
            }

            if (_random.NextDouble() < 0.2 && me.CanAfford(TywCosts.Sabotage) && me.Bank.Sum() > 8)
            {
                var target = game.Board.Tiles
                    .Where(t => t.Owner >= 0 && t.Owner != _player && t.Building != TywBuilding.Depot && t.SabotageImmuneUntil <= game.Time)
                    .Where(t => mine.Any(m => m.Hex.DistanceTo(t.Hex) <= 1))
                    .OrderBy(_ => _random.Next())
                    .FirstOrDefault();
                if (target != null)
                {
                    game.Act(_player, target.Hex);
                }
            }
        }

        private bool Expand(TywGame game, TywPlayer me, List<TywTile> mine)
        {
            // Already on the way somewhere worth it: carry on while there is anything to lay with.
            if (_goal is { } goal)
            {
                if (game.IsTownConnected(goal, _player) || game.PlanTrack(_player, goal) is not { NewLinks.Count: > 0 } onward)
                {
                    _goal = null;
                }
                else if (me.CanAfford(TywCosts.Track) && me.Bank[(int)TywResource.Wood] + me.Bank[(int)TywResource.Stone] >= 4)
                {
                    return game.BuildTrack(_player, onward) > 0;
                }
                else
                {
                    return false;
                }
            }

            var reach = mine.Select(t => t.Hex).Append(me.Depot).ToList();
            var haveResource = game.Board.Tiles
                .Where(t => t.BuildingOwner == _player && t.Building is TywBuilding.Extractor or TywBuilding.Factory)
                .Select(t => t.Produces)
                .ToHashSet();

            // What the towns it reaches are asking for is worth more: it turns into deliveries.
            var wanted = game.Board.Towns
                .Where(t => game.IsTownConnected(t, _player))
                .Select(t => game.Board[t]!.Demand)
                .ToHashSet();

            int Near(TywTile t) => reach.Min(h => h.DistanceTo(t.Hex));
            var hubs = game.Board.Tiles
                .Where(t => t.IsShared && !game.IsTownConnected(t.Hex, _player))
                .Select(t => (Tile: t, Near: Near(t)))
                .Where(c => c.Near <= 14)
                .OrderBy(c => c.Near)
                .Take(5);
            var sites = game.Board.Tiles
                .Where(t => t.IsResource && t.Owner == -1 && t.Building == TywBuilding.None)
                .Select(t => (Tile: t, Near: Near(t)))
                .OrderBy(c => c.Near)
                .Take(10);

            TywTrackPlan? bestPlan = null;
            TywTile? bestTile = null;
            var bestScore = 0.0f;
            foreach (var (tile, _) in hubs.Concat(sites))
            {
                var plan = game.PlanTrack(_player, tile.Hex);
                if (plan == null || plan.NewLinks.Count == 0)
                {
                    continue;
                }

                float value;
                if (tile.IsShared)
                {
                    // Prestige: more for being first; a port a little less than a town.
                    var first = !game.TownConnections(tile.Hex).Any();
                    value = (first ? 6.0f : 3.5f) * (tile.Building == TywBuilding.Port ? 0.8f : 1.0f);
                }
                else
                {
                    value = tile.Richness * (haveResource.Contains(tile.Produces) ? 1.0f : 2.0f);
                    if (tile.Produces == TywResource.Coal)
                    {
                        value *= 1.3f;
                    }

                    if (wanted.Contains(tile.Produces))
                    {
                        value *= 1.4f;
                    }
                }

                var score = value / (plan.Cost.Sum() + 1.0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlan = plan;
                    bestTile = tile;
                }
            }

            if (bestPlan == null || bestTile == null)
            {
                return false;
            }

            if (me.CanAfford(bestPlan.Cost))
            {
                return game.BuildTrack(_player, bestPlan) > 0;
            }

            // Too dear in one go: a town or port is worth setting out for and finishing over the next turns.
            if (bestTile.IsShared && me.Bank[(int)TywResource.Wood] >= 2 && me.Bank[(int)TywResource.Stone] >= 2)
            {
                _goal = bestTile.Hex;
                return game.BuildTrack(_player, bestPlan) > 0;
            }

            return false;
        }
    }
}
