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

            foreach (var tile in mine.Where(t => t.IsResource && t.Building == TywBuilding.None).OrderByDescending(t => t.Richness))
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
            var reach = mine.Select(t => t.Hex).Append(me.Depot).ToList();
            var haveResource = game.Board.Tiles
                .Where(t => t.BuildingOwner == _player && t.Building is TywBuilding.Extractor or TywBuilding.Factory)
                .Select(t => t.Produces)
                .ToHashSet();

            var candidates = game.Board.Tiles
                .Where(t => (t.IsResource && t.Owner == -1 && t.Building == TywBuilding.None) ||
                            (t.IsShared && !game.IsTownConnected(t.Hex, _player)))
                .Select(t => (Tile: t, Near: reach.Min(h => h.DistanceTo(t.Hex))))
                .OrderBy(c => c.Near)
                .Take(8)
                .ToList();

            TywTrackPlan? bestPlan = null;
            var bestScore = 0.0f;
            foreach (var (tile, _) in candidates)
            {
                var plan = game.PlanTrack(_player, tile.Hex);
                if (plan == null || plan.NewLinks.Count == 0)
                {
                    continue;
                }

                float value;
                if (tile.IsShared)
                {
                    value = game.TownConnections(tile.Hex).Any() ? 2.0f : 3.0f;
                }
                else
                {
                    value = tile.Richness * (haveResource.Contains(tile.Produces) ? 1.0f : 2.0f);
                    if (tile.Produces == TywResource.Coal)
                    {
                        value *= 1.3f;
                    }
                }

                var score = value / (plan.Cost.Sum() + 1.0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlan = plan;
                }
            }

            if (bestPlan == null || !me.CanAfford(bestPlan.Cost))
            {
                return false;
            }

            return game.BuildTrack(_player, bestPlan) > 0;
        }
    }
}
