using ManaUI;
using ManaVoxel.Code.Gameplay.TrainYardWars;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// Train Yard Wars' rules, headless: the hex maths, track routing that keeps to gentle bends, trains hauling
/// to the bank, sabotage stopping them, and whole bot matches that have to end with a winner.
/// </summary>
public static class TrainYardWarsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // A level names its game modes; each loads with its settings and saves back the same.
        var entry = new ManaLevelGameModeEntry
        {
            Id = "abc",
            Type = "TrainYardWars",
            Properties = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["players"] = System.Text.Json.JsonSerializer.SerializeToElement(2),
                ["seed"] = System.Text.Json.JsonSerializer.SerializeToElement(42),
                ["matchMinutes"] = System.Text.Json.JsonSerializer.SerializeToElement(8.0f)
            }
        };
        var warnings = new List<string>();
        var loaded = ManaLevelGameModes.Load(entry, warnings) as TrainYardWarsMode;
        Check("a level's game mode loads with its settings", loaded is { Players: 2, Seed: 42, MatchMinutes: 8.0f, PrestigeTarget: 12 } && warnings.Count == 0,
            string.Join("; ", warnings));
        Check("Train Yard Wars runs its own world, without the character", loaded is { WantsPlayerCharacter: false, KeepsEditorFloor: false, HandlesExitKeys: true });
        if (loaded != null)
        {
            var saved = ManaLevelGameModes.Capture(loaded);
            var again = ManaLevelGameModes.Load(saved, warnings) as TrainYardWarsMode;
            Check("and saves back what it loaded", saved.Id == "abc" && saved.Type == "TrainYardWars" && again is { Players: 2, Seed: 42, MatchMinutes: 8.0f },
                string.Join(", ", saved.Properties.Keys));
            Check("its settings are only its own (not the base class's flags)",
                saved.Properties.Keys.OrderBy(k => k).SequenceEqual(new[] { "botThinkSeconds", "daylight", "dayMinutes", "hour", "matchMinutes", "players", "prestigeTarget", "seed" }),
                string.Join(", ", saved.Properties.Keys));
        }

        var character = ManaLevelGameModes.Create("Character") as Mana.Code.GameModes.ManaCharacterGameMode;
        Check("the Character mode is built in, and is what asks for the character", character is { WantsPlayerCharacter: true, ShowsPlayHud: true, MaxJumps: 2 });
        Check("with no mode asking, play is a debug camera",
            !Mana.Code.GameModes.ManaGameModeRunner.AnyWantsPlayerCharacter(Array.Empty<Mana.Code.GameModes.ManaLevelGameMode>())
            && !Mana.Code.GameModes.ManaGameModeRunner.AnyWantsPlayerCharacter(new[] { loaded! }));
        if (character != null)
        {
            character.MaxJumps = 3;
            Check("the character jumps as its mode says", Mana.Code.GameModes.PlayModeController.JumpsFor(new Mana.Code.GameModes.ManaLevelGameMode[] { character }) == 3
                && Mana.Code.GameModes.PlayModeController.JumpsFor(Array.Empty<Mana.Code.GameModes.ManaLevelGameMode>()) == 2);
        }

        Check("an unknown game mode is skipped with a warning",
            ManaLevelGameModes.Load(new ManaLevelGameModeEntry { Type = "NoSuchMode" }, warnings) == null && warnings.Any(w => w.Contains("NoSuchMode")));
        foreach (var file in new[] { "Train Yard Wars", "Train Yard Wars Duel" })
        {
            var path = ManaLevelLibrary.ResolvePath(file);
            var level = File.Exists(path) ? ManaLevel.Load(path) : null;
            Check($"'{file}' is a level that plays Train Yard Wars", level?.GameModes?.SingleOrDefault()?.Type == "TrainYardWars", path);
        }

        var origin = new TywHex(0, 0);
        Check("opposite directions are three apart", TywHex.Opposite(1) == 4 && origin.Neighbor(0).Neighbor(3) == origin);
        Check("a ring of radius 3 has 18 hexes", TywBoard.Ring(3).Count() == 18 && TywBoard.Ring(3).All(h => h.DistanceTo(origin) == 3));
        var far = new TywHex(3, -1);
        Check("hex to world and back", TywHex.FromWorld(far.ToWorld(2.0f), 2.0f) == far);
        Check("straight and gentle bends are smooth, sharp ones not",
            TywHex.IsSmoothTurn(0, 3) && TywHex.IsSmoothTurn(0, 2) && TywHex.IsSmoothTurn(0, 4) && !TywHex.IsSmoothTurn(0, 1) && !TywHex.IsSmoothTurn(0, 5));

        var game = new TywGame(4, seed: 7, humans: 0);
        Check("one depot and one train per player", game.Board.Depots.Count == 4 && game.Trains.Count == 4);
        var depots = game.Board.Depots;
        var closest = depots.SelectMany((a, i) => depots.Skip(i + 1).Select(b => a.DistanceTo(b))).Min();
        Check("depots start well apart", closest >= 8, $"closest pair {closest} hexes");
        Check("the world has towns and ports", game.Board.Towns.Count >= 6 && game.Board.Ports.Count >= 3,
            $"{game.Board.Towns.Count} towns, {game.Board.Ports.Count} ports");

        // The world: sea all round the edge, one connected continent, terrain in sensible bands.
        var cells = TywMapGen.Cells(game.Board.Columns, game.Board.Rows).ToList();
        Check("the outer rings are all sea", cells.Where(c => c.Edge == 0).All(c => game.Board[c.Hex]!.Terrain == TywTerrain.Water) &&
                                             cells.Where(c => c.Edge < TywMapGen.BorderRings).All(c => game.Board[c.Hex]!.Terrain is TywTerrain.Water or TywTerrain.Port));
        bool Land(TywTile t) => t.Terrain is not (TywTerrain.Water or TywTerrain.Port);
        var land = game.Board.Tiles.Where(Land).ToList();
        var reached = new HashSet<TywHex> { land[0].Hex };
        var frontier = new Queue<TywHex>(reached);
        while (frontier.TryDequeue(out var at))
        {
            foreach (var next in game.Board.Around(at).Where(n => Land(n) && reached.Add(n.Hex)))
            {
                frontier.Enqueue(next.Hex);
            }
        }

        Check("every bit of land joins up", reached.Count == land.Count, $"{reached.Count} of {land.Count}");
        Check("land fills a good share of the map", land.Count > cells.Count * 0.35, $"{land.Count} of {cells.Count}");
        var pairs = game.Board.Tiles.SelectMany(t => game.Board.Around(t.Hex).Select(n => (t.Terrain, n.Terrain)))
            .Where(pair => pair.Item1 is not (TywTerrain.Town or TywTerrain.Depot or TywTerrain.Port) &&
                           pair.Item2 is not (TywTerrain.Town or TywTerrain.Depot or TywTerrain.Port))
            .ToList();
        var broken = pairs.Count(pair => !TywMapGen.MayTouch(pair.Item1, pair.Item2));
        Check("neighbouring terrain follows the bands (sea, beach, lowland, forest and hills, mountain)", broken <= pairs.Count / 50,
            $"{broken} of {pairs.Count} pairs break them");
        Console.WriteLine("    Train Yard Wars terrain: " + string.Join(", ", game.Board.Tiles.GroupBy(t => t.Terrain).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        foreach (var (players, seed) in new[] { (4, 1), (4, 42), (8, 1), (8, 42), (8, 3), (2, 5) })
        {
            var sample = TywBoard.Generate(players, seed);
            var ground = sample.Tiles.Count(t => t.Terrain is not (TywTerrain.Water or TywTerrain.Port));
            var high = sample.Tiles.Count(t => t.Terrain is TywTerrain.Mountain or TywTerrain.Hills);
            Check($"{players}-player map {seed}: hills and mountains are under a quarter of the land", high * 4 < ground,
                $"{high} of {ground}, mountains {sample.Tiles.Count(t => t.Terrain == TywTerrain.Mountain)}");
        }

        Check("mountains are ranges, not the whole land", game.Board.Tiles.Count(t => t.Terrain == TywTerrain.Mountain) < land.Count / 5);
        Check("no mountain meets the sea", !pairs.Any(pair => pair is (TywTerrain.Mountain, TywTerrain.Water)));
        Check("ports are on the water beside land", game.Board.Ports.All(p => game.Board.Around(p).Count(Land) >= 2));

        foreach (var depot in game.Board.Depots)
        {
            var near = game.Board.Tiles.Where(t => t.Hex.DistanceTo(depot) <= 5).Select(t => t.Terrain).ToHashSet();
            Check($"depot {depot} has wood, stone, grain and coal in reach",
                near.Contains(TywTerrain.Forest) && near.Contains(TywTerrain.Hills) && near.Contains(TywTerrain.Fields) && near.Contains(TywTerrain.Mountain));
        }

        // A route to the nearest forest, laid and mined, then hauled home.
        var player = game.Players[0];
        player.Bank[0] = player.Bank[1] = 30;
        var forest = game.Board.Tiles.Where(t => t.Terrain == TywTerrain.Forest)
            .OrderBy(t => t.Hex.DistanceTo(player.Depot)).First();
        var plan = game.PlanTrack(0, forest.Hex);
        Check("a track plan reaches the nearest forest", plan != null && plan.Path[^1] == forest.Hex, plan == null ? "none" : string.Join(" ", plan.Path));
        if (plan == null)
        {
            return;
        }

        Check("the plan starts at the depot", plan.Path[0] == player.Depot);
        Check("the plan never turns sharply between buildings", SmoothThrough(game, plan.Path));
        var built = game.BuildTrack(0, plan);
        Check("the track is laid and the hexes claimed", built == plan.NewLinks.Count && plan.Path.All(h => game.Board[h]!.Owner == 0));
        Check("the forest now offers an extractor", game.ActionAt(0, forest.Hex).Kind == TywActionKind.Extractor);
        Check("and building it works", game.Act(0, forest.Hex) == null && forest.Building == TywBuilding.Extractor);
        Check("a rival cannot lay track onto claimed hexes", game.PlanTrack(1, forest.Hex) == null);
        player.Bank[(int)TywResource.Coal] += 5;
        Check("a mine starts dark", !game.IsLit(forest));
        Check("a torch goes on your own hex", game.PlaceTorch(0, forest.Hex) == null && forest.HasTorch && game.TorchCount(0) == 1);
        Check("and lights the mine beside it", game.IsLit(forest));
        Check("not on a rival's", game.TorchActionAt(1, forest.Hex).Kind == TywActionKind.None);

        var woodBefore = player.Bank[(int)TywResource.Wood];
        for (var i = 0; i < 60 * 30; i++)
        {
            game.Tick(1.0f / 30.0f);
        }

        Check("a train hauled wood home within a minute", player.Bank[(int)TywResource.Wood] > woodBefore + 2,
            $"{woodBefore} -> {player.Bank[(int)TywResource.Wood]}");
        var notices = game.TakeNotices();
        Check("banking a load is announced, with what and how much",
            notices.Any(n => n is { Kind: TywNoticeKind.Banked, Player: 0, Resource: TywResource.Wood, Amount: > 0 } && n.Hex == player.Depot));
        Check("and announcements are handed over once", game.TakeNotices().Count == 0);

        // Sabotage: a rival that reaches next to the line can cut it, and the train waits at the cut.
        var cut = plan.Path[plan.Path.Count / 2];
        var rival = game.Players[1];
        rival.Bank[0] = rival.Bank[1] = rival.Bank[3] = 40;
        var beside = Enumerable.Range(0, 6).Select(cut.Neighbor)
            .Where(h => game.Board[h] is { Owner: -1, IsPassable: true, Building: TywBuilding.None })
            .Select(h => game.PlanTrack(1, h))
            .Where(p => p != null)
            .OrderBy(p => p!.NewLinks.Count)
            .FirstOrDefault();
        Check("a rival can reach the line", beside != null && game.BuildTrack(1, beside) > 0);
        Check("and sabotage it", game.ActionAt(1, cut).Kind == TywActionKind.Sabotage && game.Act(1, cut) == null && game.Board[cut]!.IsDamaged(game.Time),
            game.ActionAt(1, cut).Blocked ?? "");
        Check("a hex just hit cannot be hit again", game.ActionAt(1, cut).Blocked != null);
        Check("sabotage puts out a torch on the hex", !game.Board[cut]!.HasTorch || cut == forest.Hex);
        var train = game.Trains.First(t => t.Owner == 0);
        var blockedSeen = false;
        for (var i = 0; i < 20 * 30; i++)
        {
            game.Tick(1.0f / 30.0f);
            blockedSeen |= train.IsBlocked;
            if (train.IsBlocked)
            {
                Check("a blocked train is short of the damaged hex", MathF.Round(train.Progress) < train.Path.IndexOf(cut),
                    $"progress {train.Progress} path {string.Join(" ", train.Path)}");
                break;
            }
        }

        Check("a train stops at a sabotaged hex", blockedSeen);

        // Whole matches between bots, at 10 frames a second: they must end, and nothing is made from nothing.
        var lengths = new List<string>();
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            foreach (var players in new[] { 2, 4, 8 })
            {
                var match = new TywGame(players, seed, humans: 0);
                var bots = match.Players.Select(p => new TywBot(p.Index, seed)).ToList();
                var negative = false;
                while (!match.IsOver)
                {
                    foreach (var bot in bots)
                    {
                        bot.Tick(match);
                    }

                    match.Tick(0.1f);
                    negative |= match.Players.Any(p => p.Bank.Any(r => r < 0));
                }

                lengths.Add($"{players}p#{seed}: {match.Time / 60:0.0} min, {match.Players[match.Winner].Name} {match.VictoryPoints(match.Winner)} prestige ({match.VictoryBreakdown(match.Winner)})");
                Check($"{players}-player bot match {seed} ends with a winner", match.Winner >= 0);
                Check($"{players}-player bot match {seed} never goes into debt", !negative);
                Check($"{players}-player bot match {seed} gets going (track, mines, trains)",
                    match.Players.Count(p => match.ClaimedHexes(p.Index) > 3) * 2 >= players && match.Trains.Count > players,
                    string.Join(", ", match.Players.Select(p => $"{p.Name} {match.ClaimedHexes(p.Index)} hexes")));
                Check($"{players}-player bot match {seed} trains only drive smooth routes",
                    match.Trains.All(t => SmoothThrough(match, t.Path)));
            }
        }

        Console.WriteLine("    Train Yard Wars bot matches: " + string.Join("; ", lengths));
    }

    // Every hex a path passes through either is a building (a train may turn there) or is crossed smoothly.
    private static bool SmoothThrough(TywGame game, List<TywHex> path)
    {
        for (var i = 1; i + 1 < path.Count; i++)
        {
            var tile = game.Board[path[i]]!;
            if (tile.IsTerminal)
            {
                continue;
            }

            if (!TywHex.IsSmoothTurn(path[i].DirectionTo(path[i - 1]), path[i].DirectionTo(path[i + 1])))
            {
                return false;
            }
        }

        return true;
    }
}
