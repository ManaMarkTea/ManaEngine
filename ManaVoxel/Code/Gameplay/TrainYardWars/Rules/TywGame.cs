namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    /// <summary>Wood, Stone, Grain, Coal -- every price in the game.</summary>
    public static class TywCosts
    {
        public static readonly int[] Track = { 1, 1, 0, 0 };
        public static readonly int[] MountainTrack = { 2, 2, 0, 0 };
        public static readonly int[] Extractor = { 2, 1, 1, 0 };
        public static readonly int[] Station = { 2, 0, 1, 0 };
        public static readonly int[] Locomotive = { 1, 0, 1, 2 };
        public static readonly int[] Factory = { 0, 3, 1, 3 };
        public static readonly int[] Sabotage = { 0, 1, 0, 1 };
        public static readonly int[] Repair = { 1, 0, 0, 0 };
        public static readonly int[] Torch = { 1, 0, 0, 1 };

        public static string Format(int[] cost)
        {
            var parts = new List<string>();
            for (var i = 0; i < cost.Length; i++)
            {
                if (cost[i] > 0)
                {
                    parts.Add($"{cost[i]} {(TywResource)i}");
                }
            }

            return parts.Count == 0 ? "free" : string.Join(", ", parts);
        }
    }

    public sealed class TywPlayer
    {
        public TywPlayer(int index, string name, bool isBot, TywHex depot)
        {
            Index = index;
            Name = name;
            IsBot = isBot;
            Depot = depot;
        }

        public int Index { get; }
        public string Name { get; }
        public bool IsBot { get; set; }
        public TywHex Depot { get; }
        public int[] Bank { get; } = { 8, 7, 3, 3 };
        public int DeliveryPoints { get; internal set; }
        public int TownBonusPoints { get; internal set; }
        public float NextTrickleAt { get; internal set; }
        internal int TrickleIndex { get; set; }

        public bool CanAfford(int[] cost)
        {
            for (var i = 0; i < cost.Length; i++)
            {
                if (Bank[i] < cost[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal void Pay(int[] cost)
        {
            for (var i = 0; i < cost.Length; i++)
            {
                Bank[i] -= cost[i];
            }
        }
    }

    public enum TywTrainState
    {
        Idle,
        ToPickup,
        Loading,
        ToDrop,
        Unloading
    }

    public sealed class TywTrain
    {
        public const int Capacity = 4;

        internal TywTrain(int id, int owner, TywHex at)
        {
            Id = id;
            Owner = owner;
            At = at;
            Path = new List<TywHex> { at };
        }

        public int Id { get; }
        public int Owner { get; }
        public TywHex At { get; internal set; }

        /// <summary>The hexes it is driving through, first to last; <see cref="Progress"/> is how far along, in hexes.</summary>
        public List<TywHex> Path { get; internal set; }
        public float Progress { get; internal set; }
        public TywTrainState State { get; internal set; }
        public TywResource Cargo { get; internal set; }
        public int Amount { get; internal set; }
        public bool IsBlocked { get; internal set; }
        internal float Timer { get; set; }
        internal float Reserved { get; set; }
        internal float NextThinkAt { get; set; }
    }

    public sealed record TywEvent(float Time, int Player, string Text);

    public enum TywNoticeKind
    {
        Banked,
        Delivered,
        TownReached,
        TrackLaid,
        Built,
        TrainBought,
        Traded,
        TorchLit,
        Sabotaged,
        Repaired,
        Award,
        Won
    }

    /// <summary>Something that just happened on the board, for the view to show and sound.</summary>
    public readonly record struct TywNotice(TywNoticeKind Kind, int Player, TywHex Hex, int Amount = 0, TywResource Resource = default, int Victim = -1);

    /// <summary>What a click on a hex would do for a player.</summary>
    public enum TywActionKind
    {
        None,
        Track,
        Extractor,
        Factory,
        Station,
        Sabotage,
        Repair,
        Torch
    }

    public sealed record TywAction(TywActionKind Kind, int[] Cost, string Label, string? Blocked);

    /// <summary>A route for new track: the hexes it runs through and which links are new.</summary>
    public sealed class TywTrackPlan
    {
        public List<TywHex> Path { get; } = new();
        public List<TywLink> NewLinks { get; } = new();
        public int[] Cost { get; } = new int[4];
    }

    /// <summary>
    /// The whole of Train Yard Wars' rules, with no scene in sight: the board, who owns what, the trains and their
    /// dispatcher, scoring. The game mode only shows it and feeds it clicks, so a match can run headless (bots
    /// against bots in the tests, at any speed).
    /// </summary>
    public sealed class TywGame
    {
        public const float TrainSpeed = 1.5f;
        public const float ProductionPerYield = 0.04f;
        public const float TrickleSeconds = 10.0f;
        public const float SabotageSeconds = 20.0f;
        public const float SabotageCooldownSeconds = 30.0f;
        public const int MaxTrains = 8;
        public const int NetworkAwardMinimum = 10;
        public const int FleetAwardMinimum = 4;
        public const int TownLoadMinimum = 3;
        public const float TownRestSeconds = 30.0f;

        /// <summary>Prestige for a delivery a town asked for: the trains' own way to score, worth the most per act.</summary>
        public const int DeliveryPrestige = 2;

        /// <summary>A train waits for this much at a mine (unless the mine is full) rather than fetch single loads.</summary>
        public const float PickupMinimum = 2.0f;

        /// <summary>Banked at least this much of a load's resource, a train takes it to a port to trade instead.</summary>
        public const int PortSurplus = 8;

        /// <summary>A depot swaps <see cref="MarketRate"/> of a pile this big for one of what is short, each trickle.</summary>
        public const int MarketSurplus = 12;
        public const int MarketRate = 3;

        /// <summary>Torches a player may have burning at once (the renderer's placed lights are shared by everyone).</summary>
        public const int MaxTorches = 6;

        /// <summary>How much faster a mine works with one of its owner's torches on it or beside it.</summary>
        public const float TorchBoost = 0.5f;

        public static readonly string[] PlayerNames = { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Teal", "Pink" };

        private readonly Dictionary<TywLink, int> _links = new();
        private readonly Dictionary<TywHex, HashSet<int>> _townConnections = new();
        private readonly List<TywTrain> _trains = new();
        private readonly List<TywEvent> _log = new();
        private readonly List<TywNotice> _notices = new();
        private readonly int[] _claimed = new int[8];
        private readonly int[] _factories = new int[8];
        private readonly int[] _extractors = new int[8];
        private readonly int[] _torches = new int[8];
        private readonly Random _random;
        private int _nextTrainId = 1;

        public TywGame(int players, int seed, int humans = 1)
        {
            players = Math.Clamp(players, 2, 8);
            Seed = seed;
            _random = new Random(seed ^ 0x5eed);
            Board = TywBoard.Generate(players, seed);
            for (var p = 0; p < players; p++)
            {
                var player = new TywPlayer(p, PlayerNames[p], p >= humans, Board.Depots[p])
                {
                    NextTrickleAt = TrickleSeconds
                };
                Players.Add(player);
                SpawnTrain(p);
            }

            foreach (var port in Board.Ports)
            {
                _townConnections[port] = new HashSet<int>();
            }

            foreach (var town in Board.Towns)
            {
                _townConnections[town] = new HashSet<int>();
                Board[town]!.DemandChangesAt = 60.0f + _random.Next(20);
            }
        }

        public int Seed { get; }
        public TywBoard Board { get; }
        public List<TywPlayer> Players { get; } = new();
        public IReadOnlyList<TywTrain> Trains => _trains;
        public IReadOnlyList<TywEvent> Log => _log;
        public IReadOnlyDictionary<TywLink, int> Links => _links;
        public float Time { get; private set; }
        public float Duration { get; set; } = 12 * 60;
        public int VictoryTarget { get; set; } = 15;
        public bool IsOver { get; private set; }
        public int Winner { get; private set; } = -1;
        public int NetworkHolder { get; private set; } = -1;
        public int FleetHolder { get; private set; } = -1;

        /// <summary>Bumped when anything a scoreboard shows changes.</summary>
        public int Revision { get; private set; }

        public float TimeLeft => MathF.Max(0.0f, Duration - Time);

        /// <summary>Hands over what happened since the last call.</summary>
        public List<TywNotice> TakeNotices()
        {
            var taken = new List<TywNotice>(_notices);
            _notices.Clear();
            return taken;
        }

        // A headless match never takes them, so the oldest go.
        private void Notify(TywNotice notice)
        {
            if (_notices.Count >= 256)
            {
                _notices.RemoveRange(0, 64);
            }

            _notices.Add(notice);
        }

        // ---- Queries ---------------------------------------------------------------------------------------

        public int LinkOwner(TywHex a, TywHex b) => _links.TryGetValue(new TywLink(a, b), out var owner) ? owner : -1;

        /// <summary>Directions out of <paramref name="hex"/> along a player's own track.</summary>
        public IEnumerable<int> LinkDirections(TywHex hex, int player)
        {
            for (var d = 0; d < TywHex.DirectionCount; d++)
            {
                if (LinkOwner(hex, hex.Neighbor(d)) == player)
                {
                    yield return d;
                }
            }
        }

        /// <summary>Every direction with track out of a hex, whoever laid it.</summary>
        public IEnumerable<(int Direction, int Owner)> AllLinks(TywHex hex)
        {
            for (var d = 0; d < TywHex.DirectionCount; d++)
            {
                var owner = LinkOwner(hex, hex.Neighbor(d));
                if (owner >= 0)
                {
                    yield return (d, owner);
                }
            }
        }

        public bool IsInNetwork(int player, TywHex hex)
        {
            var tile = Board[hex];
            if (tile == null)
            {
                return false;
            }

            if (tile.Building == TywBuilding.Depot && tile.BuildingOwner == player)
            {
                return true;
            }

            return LinkDirections(hex, player).Any();
        }

        public bool IsTownConnected(TywHex town, int player) =>
            _townConnections.TryGetValue(town, out var set) && set.Contains(player);

        public IEnumerable<int> TownConnections(TywHex town) =>
            _townConnections.TryGetValue(town, out var set) ? set : Enumerable.Empty<int>();

        public int TrainCount(int player) => _trains.Count(t => t.Owner == player);

        /// <summary>Hexes a player's track has claimed (not counting the depot). Kept as a count: the awards read it every tick.</summary>
        public int ClaimedHexes(int player) => _claimed[player];

        /// <summary>Where a player's points come from, for the scoreboard and for tuning.</summary>
        public string VictoryBreakdown(int player)
        {
            var extractors = Board.Tiles.Count(t => t.BuildingOwner == player && t.Building == TywBuilding.Extractor);
            var factories = Board.Tiles.Count(t => t.BuildingOwner == player && t.Building == TywBuilding.Factory);
            var towns = _townConnections.Values.Count(set => set.Contains(player));
            var p = Players[player];
            return $"mines {extractors}, factories {factories}, towns {towns}+{p.TownBonusPoints}, deliveries {p.DeliveryPoints}" +
                   (NetworkHolder == player ? ", network" : "") + (FleetHolder == player ? ", fleet" : "");
        }

        public int VictoryPoints(int player)
        {
            // Mines and factories earn nothing themselves: prestige comes from where the trains reach and what they deliver.
            var points = 0;

            points += _townConnections.Values.Count(set => set.Contains(player));
            var p = Players[player];
            points += p.TownBonusPoints + p.DeliveryPoints;
            if (NetworkHolder == player)
            {
                points += 2;
            }

            if (FleetHolder == player)
            {
                points += 2;
            }

            return points;
        }

        // ---- Track -----------------------------------------------------------------------------------------

        private bool CanEnter(int player, TywTile tile)
        {
            if (!tile.IsPassable)
            {
                return false;
            }

            if (tile.IsShared)
            {
                return true;
            }

            return tile.Owner == -1 || tile.Owner == player;
        }

        /// <summary>
        /// The cheapest new track from anywhere on a player's network to <paramref name="target"/>, keeping to
        /// straight runs and gentle bends except where a building lets a train stop and turn. Runs along the
        /// player's own track for free, so a branch can start from the middle of a line. Null when there is no way.
        /// </summary>
        public TywTrackPlan? PlanTrack(int player, TywHex target)
        {
            var targetTile = Board[target];
            if (targetTile == null || !CanEnter(player, targetTile))
            {
                return null;
            }

            var dist = new Dictionary<(TywHex, int), float>();
            var parent = new Dictionary<(TywHex, int), (TywHex, int)>();
            var queue = new PriorityQueue<(TywHex Hex, int CameFrom), float>();

            foreach (var tile in Board.Tiles)
            {
                if (!IsInNetwork(player, tile.Hex))
                {
                    continue;
                }

                var dirs = LinkDirections(tile.Hex, player).ToList();
                if (tile.IsTerminal || dirs.Count == 0)
                {
                    StartAt((tile.Hex, -1));
                }
                else
                {
                    foreach (var d in dirs)
                    {
                        StartAt((tile.Hex, d));
                    }
                }
            }

            void StartAt((TywHex, int) state)
            {
                dist[state] = 0.0f;
                queue.Enqueue(state, 0.0f);
            }

            (TywHex, int)? found = null;
            while (queue.TryDequeue(out var state, out var cost))
            {
                if (dist.TryGetValue(state, out var known) && cost > known)
                {
                    continue;
                }

                if (state.Hex == target)
                {
                    found = state;
                    break;
                }

                var here = Board[state.Hex]!;
                for (var dout = 0; dout < TywHex.DirectionCount; dout++)
                {
                    if (state.CameFrom >= 0 && dout == state.CameFrom)
                    {
                        continue;
                    }

                    if (state.CameFrom >= 0 && !here.IsTerminal && !TywHex.IsSmoothTurn(state.CameFrom, dout))
                    {
                        continue;
                    }

                    var next = state.Hex.Neighbor(dout);
                    var nextTile = Board[next];
                    if (nextTile == null)
                    {
                        continue;
                    }

                    var owner = LinkOwner(state.Hex, next);
                    float step;
                    if (owner == player)
                    {
                        step = 0.001f;
                    }
                    else if (owner >= 0 || !CanEnter(player, nextTile))
                    {
                        continue;
                    }
                    else
                    {
                        step = nextTile.Terrain == TywTerrain.Mountain ? 2.0f : 1.0f;
                    }

                    var nextState = (next, TywHex.Opposite(dout));
                    var total = cost + step + 0.01f;
                    if (!dist.TryGetValue(nextState, out var old) || total < old)
                    {
                        dist[nextState] = total;
                        parent[nextState] = state;
                        queue.Enqueue(nextState, total);
                    }
                }
            }

            if (found == null)
            {
                return null;
            }

            var plan = new TywTrackPlan();
            var walk = found.Value;
            plan.Path.Add(walk.Item1);
            while (parent.TryGetValue(walk, out var previous))
            {
                walk = previous;
                plan.Path.Add(walk.Item1);
            }

            plan.Path.Reverse();
            for (var i = 0; i + 1 < plan.Path.Count; i++)
            {
                var a = plan.Path[i];
                var b = plan.Path[i + 1];
                if (LinkOwner(a, b) == player)
                {
                    continue;
                }

                plan.NewLinks.Add(new TywLink(a, b));
                var price = Board[b]!.Terrain == TywTerrain.Mountain ? TywCosts.MountainTrack : TywCosts.Track;
                for (var r = 0; r < 4; r++)
                {
                    plan.Cost[r] += price[r];
                }
            }

            return plan;
        }

        /// <summary>
        /// Lays a plan's new links in order for as long as the player can pay, so a route too dear for the bank
        /// still gets started. Returns how many were laid.
        /// </summary>
        public int BuildTrack(int player, TywTrackPlan plan)
        {
            var p = Players[player];
            var built = 0;
            for (var i = 0; i + 1 < plan.Path.Count; i++)
            {
                var a = plan.Path[i];
                var b = plan.Path[i + 1];
                if (LinkOwner(a, b) == player)
                {
                    continue;
                }

                var tileA = Board[a];
                var tileB = Board[b];
                if (tileA == null || tileB == null || LinkOwner(a, b) >= 0 || !CanEnter(player, tileB) || !IsInNetwork(player, a))
                {
                    break;
                }

                var price = tileB.Terrain == TywTerrain.Mountain ? TywCosts.MountainTrack : TywCosts.Track;
                if (!p.CanAfford(price))
                {
                    break;
                }

                p.Pay(price);
                _links[new TywLink(a, b)] = player;
                foreach (var tile in new[] { tileA, tileB })
                {
                    if (!tile.IsShared && tile.Owner != player)
                    {
                        tile.Owner = player;
                        _claimed[player]++;
                    }

                    tile.Revision++;
                }

                built++;
                if (tileB.IsShared)
                {
                    ConnectTown(player, b);
                }
            }

            if (built > 0)
            {
                Revision++;
                Notify(new TywNotice(TywNoticeKind.TrackLaid, player, plan.Path[^1], built));
            }

            return built;
        }

        private void ConnectTown(int player, TywHex town)
        {
            var set = _townConnections[town];
            if (!set.Add(player))
            {
                return;
            }

            var what = Board[town]!.Building == TywBuilding.Port ? "port" : "town";
            // Only a town pays the first-arrival bonus: a port is somewhere to trade, not a race.
            var first = set.Count == 1 && what == "town";
            Notify(new TywNotice(TywNoticeKind.TownReached, player, town, first ? 2 : 1));
            if (first)
            {
                Players[player].TownBonusPoints++;
                Say(player, $"{Players[player].Name} reached a {what} first (+2 prestige)");
            }
            else
            {
                Say(player, $"{Players[player].Name} connected a {what} (+1 prestige)");
            }
        }

        // ---- Buildings and actions -------------------------------------------------------------------------

        /// <summary>What clicking <paramref name="hex"/> would do for <paramref name="player"/> right now.</summary>
        public TywAction ActionAt(int player, TywHex hex)
        {
            var tile = Board[hex];
            if (tile == null)
            {
                return new TywAction(TywActionKind.None, TywCosts.Repair, "", null);
            }

            var p = Players[player];
            TywAction Offer(TywActionKind kind, int[] cost, string label) =>
                new(kind, cost, label, p.CanAfford(cost) ? null : Short(p, cost));

            if (tile.Owner == player && tile.IsDamaged(Time))
            {
                return Offer(TywActionKind.Repair, TywCosts.Repair, "Repair");
            }

            if (tile.Owner == player && tile.Building == TywBuilding.None && tile.IsResource)
            {
                return Offer(TywActionKind.Extractor, ExtractorCost(player), $"{tile.Produces} extractor");
            }

            if (tile.Building == TywBuilding.Extractor && tile.BuildingOwner == player)
            {
                return Offer(TywActionKind.Factory, TywCosts.Factory, "Upgrade to factory");
            }

            if (tile.Owner == player && tile.Building == TywBuilding.None && LinkDirections(hex, player).Any())
            {
                return Offer(TywActionKind.Station, TywCosts.Station, "Station");
            }

            if (tile.Owner >= 0 && tile.Owner != player && tile.Building != TywBuilding.Depot)
            {
                if (!Board.Tiles.Any(t => t.Owner == player && t.Hex.DistanceTo(hex) <= 1))
                {
                    return new TywAction(TywActionKind.Sabotage, TywCosts.Sabotage, "Sabotage", "only next to your own track");
                }

                if (tile.SabotageImmuneUntil > Time)
                {
                    return new TywAction(TywActionKind.Sabotage, TywCosts.Sabotage, "Sabotage", "recently hit");
                }

                return Offer(TywActionKind.Sabotage, TywCosts.Sabotage, $"Sabotage {Players[tile.Owner].Name}");
            }

            if (!IsInNetwork(player, hex) && CanEnter(player, tile))
            {
                return new TywAction(TywActionKind.Track, TywCosts.Track, "Lay track", null);
            }

            return new TywAction(TywActionKind.None, TywCosts.Track, "", null);
        }

        /// <summary>Does what <see cref="ActionAt"/> offers. Null on success, else why not.</summary>
        public string? Act(int player, TywHex hex)
        {
            var action = ActionAt(player, hex);
            if (action.Kind == TywActionKind.None)
            {
                return "nothing to do here";
            }

            if (action.Blocked != null)
            {
                return action.Blocked;
            }

            var p = Players[player];
            var tile = Board[hex]!;
            switch (action.Kind)
            {
                case TywActionKind.Track:
                    var plan = PlanTrack(player, hex);
                    if (plan == null)
                    {
                        return "no route";
                    }

                    return BuildTrack(player, plan) > 0 ? null : Short(Players[player], plan.Cost);

                case TywActionKind.Extractor:
                    p.Pay(action.Cost);
                    _extractors[player]++;
                    tile.Building = TywBuilding.Extractor;
                    tile.BuildingOwner = player;
                    break;

                case TywActionKind.Factory:
                    p.Pay(action.Cost);
                    tile.Building = TywBuilding.Factory;
                    _factories[player]++;
                    Say(player, $"{p.Name} built a factory");
                    break;

                case TywActionKind.Station:
                    p.Pay(action.Cost);
                    tile.Building = TywBuilding.Station;
                    tile.BuildingOwner = player;
                    break;

                case TywActionKind.Sabotage:
                    p.Pay(action.Cost);
                    Notify(new TywNotice(TywNoticeKind.Sabotaged, player, hex, Victim: tile.Owner));
                    // A cut line's torch goes out with it.
                    if (tile.HasTorch)
                    {
                        _torches[tile.TorchOwner]--;
                        tile.TorchOwner = -1;
                    }

                    tile.DamagedUntil = Time + SabotageSeconds;
                    tile.SabotageImmuneUntil = Time + SabotageSeconds + SabotageCooldownSeconds;
                    Say(player, $"{p.Name} sabotaged {Players[tile.Owner].Name}'s line!");
                    break;

                case TywActionKind.Repair:
                    p.Pay(action.Cost);
                    tile.DamagedUntil = Time;
                    Notify(new TywNotice(TywNoticeKind.Repaired, player, hex));
                    break;
            }

            if (action.Kind is TywActionKind.Extractor or TywActionKind.Factory or TywActionKind.Station)
            {
                Notify(new TywNotice(TywNoticeKind.Built, player, hex));
            }

            tile.Revision++;
            Revision++;
            return null;
        }

        public int TorchCount(int player) => _torches[player];

        /// <summary>A mine with one of its owner's torches on its hex or the next one.</summary>
        public bool IsLit(TywTile tile)
        {
            var owner = tile.BuildingOwner;
            if (owner < 0)
            {
                return false;
            }

            if (tile.TorchOwner == owner)
            {
                return true;
            }

            foreach (var near in Board.Around(tile.Hex))
            {
                if (near.TorchOwner == owner)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>What placing a torch on <paramref name="hex"/> would do, for the hint.</summary>
        public TywAction TorchActionAt(int player, TywHex hex)
        {
            var tile = Board[hex];
            if (tile == null || !(tile.Owner == player || tile.BuildingOwner == player))
            {
                return new TywAction(TywActionKind.None, TywCosts.Torch, "Torch", "only on your own track or buildings");
            }

            if (tile.HasTorch)
            {
                return new TywAction(TywActionKind.None, TywCosts.Torch, "Torch", "already lit");
            }

            if (_torches[player] >= MaxTorches)
            {
                return new TywAction(TywActionKind.Torch, TywCosts.Torch, "Torch", $"all {MaxTorches} torches are burning");
            }

            return new TywAction(TywActionKind.Torch, TywCosts.Torch, "Torch",
                Players[player].CanAfford(TywCosts.Torch) ? null : Short(Players[player], TywCosts.Torch));
        }

        /// <summary>Lights a torch on one of the player's hexes. Null on success, else why not.</summary>
        public string? PlaceTorch(int player, TywHex hex)
        {
            var action = TorchActionAt(player, hex);
            if (action.Kind != TywActionKind.Torch || action.Blocked != null)
            {
                return action.Blocked ?? "nowhere to put it";
            }

            var tile = Board[hex]!;
            Players[player].Pay(TywCosts.Torch);
            tile.TorchOwner = player;
            _torches[player]++;
            tile.Revision++;
            Revision++;
            Notify(new TywNotice(TywNoticeKind.TorchLit, player, hex));
            return null;
        }

        public string? BuyTrain(int player)
        {
            var p = Players[player];
            if (TrainCount(player) >= MaxTrains)
            {
                return "fleet is full";
            }

            if (!p.CanAfford(TywCosts.Locomotive))
            {
                return Short(p, TywCosts.Locomotive);
            }

            p.Pay(TywCosts.Locomotive);
            SpawnTrain(player);
            Notify(new TywNotice(TywNoticeKind.TrainBought, player, p.Depot));
            Revision++;
            return null;
        }

        private void SpawnTrain(int player)
        {
            var depot = Players[player].Depot;
            _trains.Add(new TywTrain(_nextTrainId++, player, depot) { NextThinkAt = Time + 0.3f * TrainCount(player) });
        }

        // ---- Simulation ------------------------------------------------------------------------------------

        public void Tick(float deltaSeconds)
        {
            if (IsOver)
            {
                return;
            }

            Time += deltaSeconds;
            var startRevision = Revision;

            foreach (var tile in Board.Tiles)
            {
                if (tile.Building is TywBuilding.Extractor or TywBuilding.Factory)
                {
                    var rate = tile.Richness * ProductionPerYield * (tile.Building == TywBuilding.Factory ? 2.0f : 1.0f)
                               * (IsLit(tile) ? 1.0f + TorchBoost : 1.0f);
                    tile.Stock = MathF.Min(StockCap(tile), tile.Stock + rate * deltaSeconds);
                }

                if (tile.Building == TywBuilding.Town && Time >= tile.DemandChangesAt)
                {
                    ChangeDemand(tile);
                }

                // A repair by the clock: the view polls revisions, so the hex has to say it changed.
                if (tile.DamagedUntil > 0.0f && tile.DamagedUntil <= Time)
                {
                    tile.DamagedUntil = 0.0f;
                    tile.Revision++;
                }
            }

            foreach (var player in Players)
            {
                if (Time >= player.NextTrickleAt)
                {
                    player.NextTrickleAt += TrickleSeconds;
                    // Whatever is scarcest (taking turns on a tie): a bank run dry of stone is a stall, a pile of grain is not.
                    var gift = Enumerable.Range(0, 4).Select(i => (player.TrickleIndex + i) % 4).MinBy(r => player.Bank[r]);
                    player.Bank[gift]++;
                    player.TrickleIndex = gift + 1;

                    // The depot's market: a pile swaps for what is short, dearer than at a port, so a player with no
                    // port in reach is never left sitting on a hundred grain with nothing to build.
                    var glut = Enumerable.Range(0, 4).MaxBy(r => player.Bank[r]);
                    if (player.Bank[glut] >= MarketSurplus)
                    {
                        var wanted = Scarcest(player, (TywResource)glut);
                        if (player.Bank[(int)wanted] < 3)
                        {
                            player.Bank[glut] -= MarketRate;
                            player.Bank[(int)wanted]++;
                            Notify(new TywNotice(TywNoticeKind.Traded, player.Index, player.Depot, 1, wanted));
                        }
                    }

                    Revision++;
                }
            }

            foreach (var train in _trains)
            {
                TickTrain(train, deltaSeconds);
            }

            UpdateAwards();
            CheckForWinner();
            if (Revision == startRevision)
            {
                // Stock and trains move every frame; the scoreboard only needs a nudge now and then.
                if ((int)(Time * 4) != (int)((Time - deltaSeconds) * 4))
                {
                    Revision++;
                }
            }
        }

        /// <summary>How many loads a mine holds before it stops producing.</summary>
        public static float StockCap(TywTile tile) => tile.Building == TywBuilding.Factory ? 10.0f : 6.0f;

        private void ChangeDemand(TywTile town)
        {
            var next = (TywResource)_random.Next(3);
            if (next >= town.Demand)
            {
                next++;
            }

            town.Demand = next;
            town.DemandChangesAt = Time + 60.0f;
            town.Revision++;
        }

        private void UpdateAwards()
        {
            NetworkHolder = Award(NetworkHolder, ClaimedHexes, NetworkAwardMinimum, "Biggest Network");
            FleetHolder = Award(FleetHolder, TrainCount, FleetAwardMinimum, "Biggest Fleet");
        }

        // A holder keeps an award until someone strictly passes them.
        private int Award(int holder, Func<int, int> measure, int minimum, string name)
        {
            var best = holder >= 0 && measure(holder) >= minimum ? holder : -1;
            foreach (var player in Players)
            {
                var value = measure(player.Index);
                if (value >= minimum && (best < 0 || value > measure(best)))
                {
                    best = player.Index;
                }
            }

            if (best != holder && best >= 0)
            {
                Say(best, $"{name} goes to {Players[best].Name} (+2 prestige)");
                Notify(new TywNotice(TywNoticeKind.Award, best, Players[best].Depot, 2));
                Revision++;
            }

            return best;
        }

        private void CheckForWinner()
        {
            foreach (var player in Players)
            {
                if (VictoryPoints(player.Index) >= VictoryTarget)
                {
                    End(player.Index);
                    return;
                }
            }

            if (Time >= Duration)
            {
                var winner = Players
                    .OrderByDescending(p => VictoryPoints(p.Index))
                    .ThenByDescending(p => ClaimedHexes(p.Index))
                    .First();
                End(winner.Index);
            }
        }

        private void End(int winner)
        {
            IsOver = true;
            Winner = winner;
            Say(winner, $"{Players[winner].Name} wins with {VictoryPoints(winner)} prestige!");
            Notify(new TywNotice(TywNoticeKind.Won, winner, Players[winner].Depot));
            Revision++;
        }

        private void Say(int player, string text)
        {
            _log.Add(new TywEvent(Time, player, text));
        }

        // ---- Trains ----------------------------------------------------------------------------------------

        private void TickTrain(TywTrain train, float dt)
        {
            switch (train.State)
            {
                case TywTrainState.Idle:
                    if (Time >= train.NextThinkAt)
                    {
                        train.NextThinkAt = Time + 0.5f;
                        Dispatch(train);
                    }

                    break;

                case TywTrainState.ToPickup:
                case TywTrainState.ToDrop:
                    if (Drive(train, dt))
                    {
                        train.At = train.Path[^1];
                        train.Timer = 1.0f;
                        train.State = train.State == TywTrainState.ToPickup ? TywTrainState.Loading : TywTrainState.Unloading;
                    }

                    break;

                case TywTrainState.Loading:
                    train.Timer -= dt;
                    if (train.Timer <= 0.0f)
                    {
                        Load(train);
                    }

                    break;

                case TywTrainState.Unloading:
                    train.Timer -= dt;
                    if (train.Timer <= 0.0f)
                    {
                        Unload(train);
                    }

                    break;
            }
        }

        /// <summary>Moves a train along its path, holding at the edge of a sabotaged hex. True on arrival.</summary>
        private bool Drive(TywTrain train, float dt)
        {
            var last = train.Path.Count - 1;
            var next = train.Progress + TrainSpeed * dt;

            // The first hex boundary the train would cross into a damaged hex.
            var nextIndex = (int)MathF.Floor(train.Progress + 0.5f) + 1;
            train.IsBlocked = false;
            if (nextIndex <= last && next >= nextIndex - 0.5f && Board[train.Path[nextIndex]]!.IsDamaged(Time))
            {
                next = MathF.Max(train.Progress, nextIndex - 0.5f - 0.05f);
                train.IsBlocked = true;
            }

            train.Progress = MathF.Min(next, last);
            return train.Progress >= last;
        }

        private void Dispatch(TywTrain train)
        {
            // Still loaded (the town it was bound for stopped wanting it): find somewhere to leave it first.
            if (train.Amount > 0)
            {
                SendToDrop(train);
                return;
            }

            var fromHere = SearchTrack(train.Owner, train.At);
            TywTile? best = null;
            var bestScore = 0.0f;
            foreach (var tile in Board.Tiles)
            {
                if (tile.BuildingOwner != train.Owner || tile.Building is not (TywBuilding.Extractor or TywBuilding.Factory))
                {
                    continue;
                }

                var available = tile.Stock - tile.Reserved;
                var full = tile.Stock >= StockCap(tile) - 0.01f;
                if ((available < PickupMinimum && !(full && available >= 1.0f)) || !fromHere.TryGetValue(tile.Hex, out var there))
                {
                    continue;
                }

                var back = NearestDrop(train.Owner, tile.Hex, cargo: null).Distance;
                if (float.IsPositiveInfinity(back))
                {
                    continue;
                }

                var score = MathF.Min(available, TywTrain.Capacity) / (there.Distance + back + 2.0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }

            if (best != null)
            {
                var path = PathTo(fromHere, best.Hex);
                if (path == null)
                {
                    return;
                }

                train.Reserved = MathF.Min(TywTrain.Capacity, MathF.Floor(best.Stock - best.Reserved));
                best.Reserved += train.Reserved;
                StartDriving(train, path, TywTrainState.ToPickup);
                return;
            }

            // Nothing to fetch: go home rather than sit at a mine.
            var at = Board[train.At]!;
            if (at.Building != TywBuilding.Depot && at.Building != TywBuilding.Station)
            {
                var (dropPath, _) = NearestDrop(train.Owner, train.At, cargo: null);
                if (dropPath != null && dropPath.Count > 1)
                {
                    StartDriving(train, dropPath, TywTrainState.ToDrop);
                }
            }
        }

        private void StartDriving(TywTrain train, List<TywHex> path, TywTrainState state)
        {
            train.Path = path;
            train.Progress = 0.0f;
            train.State = state;
        }

        private void Load(TywTrain train)
        {
            var tile = Board[train.At]!;
            tile.Reserved = MathF.Max(0.0f, tile.Reserved - train.Reserved);
            train.Reserved = 0.0f;
            var amount = (int)MathF.Min(TywTrain.Capacity, MathF.Floor(tile.Stock));
            if (amount <= 0 || tile.Building is not (TywBuilding.Extractor or TywBuilding.Factory))
            {
                train.State = TywTrainState.Idle;
                return;
            }

            tile.Stock -= amount;
            train.Cargo = tile.Produces;
            train.Amount = amount;
            SendToDrop(train);
        }

        private void SendToDrop(TywTrain train)
        {
            var (path, _) = NearestDrop(train.Owner, train.At, train.Cargo, townLoad: train.Amount >= TownLoadMinimum);
            if (path == null)
            {
                train.State = TywTrainState.Idle;
                return;
            }

            StartDriving(train, path, TywTrainState.ToDrop);
        }

        private void Unload(TywTrain train)
        {
            var tile = Board[train.At]!;
            var player = Players[train.Owner];
            if (train.Amount > 0)
            {
                if (tile.Building == TywBuilding.Town)
                {
                    if (!tile.Wants(train.Cargo, Time) || train.Amount < TownLoadMinimum)
                    {
                        // The town changed its mind on the way: keep the load, and the dispatcher takes it on.
                        train.State = TywTrainState.Idle;
                        train.NextThinkAt = Time;
                        return;
                    }

                    player.DeliveryPoints += DeliveryPrestige;
                    Say(train.Owner, $"{player.Name} delivered {train.Cargo} to a town (+{DeliveryPrestige} prestige)");
                    Notify(new TywNotice(TywNoticeKind.Delivered, train.Owner, tile.Hex, DeliveryPrestige, train.Cargo));
                    ChangeDemand(tile);
                    tile.SatisfiedUntil = Time + TownRestSeconds;
                    tile.DemandChangesAt = tile.SatisfiedUntil + 45.0f;
                    Revision++;
                }
                else if (tile.Building == TywBuilding.Port)
                {
                    var wanted = Scarcest(player, train.Cargo);
                    var amount = Math.Max(1, (train.Amount + 1) / 2);
                    player.Bank[(int)wanted] += amount;
                    Notify(new TywNotice(TywNoticeKind.Traded, train.Owner, tile.Hex, amount, wanted));
                    Revision++;
                }
                else
                {
                    player.Bank[(int)train.Cargo] += train.Amount;
                    Notify(new TywNotice(TywNoticeKind.Banked, train.Owner, tile.Hex, train.Amount, train.Cargo));
                    Revision++;
                }
            }

            train.Amount = 0;
            train.State = TywTrainState.Idle;
            train.NextThinkAt = Time;
        }

        /// <summary>
        /// Each mine costs a little more than the last (one more wood and stone for every three owned), so a big
        /// world's spare hexes do not turn into an endless pile of resources.
        /// </summary>
        public int[] ExtractorCost(int player)
        {
            var extra = _extractors[player] / 3;
            var cost = (int[])TywCosts.Extractor.Clone();
            cost[(int)TywResource.Wood] += extra;
            cost[(int)TywResource.Stone] += extra;
            return cost;
        }

        // What is missing, not the whole price: the label already shows that.
        private static string Short(TywPlayer player, int[] cost) =>
            "short " + TywCosts.Format(cost.Select((c, i) => Math.Max(0, c - player.Bank[i])).ToArray());

        /// <summary>What a player has least of, other than <paramref name="except"/>: what a port trades a load for.</summary>
        private static TywResource Scarcest(TywPlayer player, TywResource except)
        {
            var best = except == TywResource.Wood ? TywResource.Stone : TywResource.Wood;
            for (var r = 0; r < 4; r++)
            {
                if ((TywResource)r != except && player.Bank[r] < player.Bank[(int)best])
                {
                    best = (TywResource)r;
                }
            }

            return best;
        }

        /// <summary>A load worth trading at a port: plenty of it banked already, and something else running short.</summary>
        private bool IsSurplus(int player, TywResource cargo)
        {
            var bank = Players[player].Bank;
            return bank[(int)cargo] >= PortSurplus && bank.Where((_, r) => r != (int)cargo).Min() < 3;
        }

        /// <summary>
        /// The closest place a player's train can leave a load: their depot or a station, or -- with cargo a
        /// connected town is asking for and not much further -- that town.
        /// </summary>
        private (List<TywHex>? Path, float Distance) NearestDrop(int player, TywHex from, TywResource? cargo, bool townLoad = false)
        {
            var search = SearchTrack(player, from);
            var bestDistance = float.PositiveInfinity;
            TywHex? nearestOwn = null;
            var townDistance = float.PositiveInfinity;
            foreach (var (hex, entry) in search)
            {
                var tile = Board[hex]!;
                if ((tile.Building is TywBuilding.Depot or TywBuilding.Station) && tile.BuildingOwner == player)
                {
                    if (entry.Distance < bestDistance)
                    {
                        bestDistance = entry.Distance;
                        nearestOwn = hex;
                    }
                }
            }

            var home = nearestOwn;
            if (cargo is { } resource && townLoad)
            {
                foreach (var (hex, entry) in search)
                {
                    var tile = Board[hex]!;
                    if (tile.Wants(resource, Time) && IsTownConnected(hex, player) &&
                        entry.Distance <= bestDistance * 2.0f + 3.0f && entry.Distance < townDistance)
                    {
                        townDistance = entry.Distance;
                        home = hex;
                    }
                }
            }

            // A load of something already piled up goes to a port, when one is connected and not far out of the way.
            if (cargo is { } surplus && townDistance == float.PositiveInfinity && IsSurplus(player, surplus))
            {
                foreach (var (hex, entry) in search)
                {
                    if (Board[hex]!.Building == TywBuilding.Port && IsTownConnected(hex, player) &&
                        entry.Distance <= bestDistance * 2.0f + 4.0f && entry.Distance < townDistance)
                    {
                        townDistance = entry.Distance;
                        home = hex;
                    }
                }
            }

            if (home == null)
            {
                return (null, float.PositiveInfinity);
            }

            return (PathTo(search, home.Value), MathF.Min(bestDistance, townDistance));
        }

        public readonly record struct SearchEntry(float Distance, (TywHex Hex, int CameFrom) State);

        private sealed class TrackSearch : Dictionary<TywHex, SearchEntry>
        {
            public Dictionary<(TywHex, int), (TywHex, int)> Parents { get; } = new();
        }

        /// <summary>
        /// Every hex a player's train can reach from a terminal along their own track, with the shortest way to
        /// each. A train only turns sharply or reverses where a building stands.
        /// </summary>
        private TrackSearch SearchTrack(int player, TywHex from)
        {
            var result = new TrackSearch();
            var dist = new Dictionary<(TywHex, int), float>();
            var queue = new PriorityQueue<(TywHex Hex, int CameFrom), float>();
            dist[(from, -1)] = 0.0f;
            queue.Enqueue((from, -1), 0.0f);
            while (queue.TryDequeue(out var state, out var cost))
            {
                if (dist[state] < cost)
                {
                    continue;
                }

                if (!result.ContainsKey(state.Hex))
                {
                    result[state.Hex] = new SearchEntry(cost, state);
                }

                var tile = Board[state.Hex]!;
                for (var dout = 0; dout < TywHex.DirectionCount; dout++)
                {
                    var next = state.Hex.Neighbor(dout);
                    if (LinkOwner(state.Hex, next) != player)
                    {
                        continue;
                    }

                    if (state.CameFrom >= 0 && !tile.IsTerminal && !TywHex.IsSmoothTurn(state.CameFrom, dout))
                    {
                        continue;
                    }

                    var nextState = (next, TywHex.Opposite(dout));
                    var total = cost + 1.0f;
                    if (!dist.TryGetValue(nextState, out var old) || total < old)
                    {
                        dist[nextState] = total;
                        result.Parents[nextState] = state;
                        queue.Enqueue(nextState, total);
                    }
                }
            }

            return result;
        }

        private static List<TywHex>? PathTo(TrackSearch search, TywHex target)
        {
            if (!search.TryGetValue(target, out var entry))
            {
                return null;
            }

            var path = new List<TywHex>();
            var state = entry.State;
            path.Add(state.Hex);
            while (search.Parents.TryGetValue(state, out var previous))
            {
                state = previous;
                path.Add(state.Hex);
            }

            path.Reverse();
            return path;
        }
    }
}
