using ManaUI;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;

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

        var origin = new TywHex(0, 0);
        Check("opposite directions are three apart", TywHex.Opposite(1) == 4 && origin.Neighbor(0).Neighbor(3) == origin);
        Check("a ring of radius 3 has 18 hexes", TywBoard.Ring(3).Count() == 18 && TywBoard.Ring(3).All(h => h.DistanceTo(origin) == 3));
        var far = new TywHex(3, -1);
        Check("hex to world and back", TywHex.FromWorld(far.ToWorld(2.0f), 2.0f) == far);
        Check("straight and gentle bends are smooth, sharp ones not",
            TywHex.IsSmoothTurn(0, 3) && TywHex.IsSmoothTurn(0, 2) && TywHex.IsSmoothTurn(0, 4) && !TywHex.IsSmoothTurn(0, 1) && !TywHex.IsSmoothTurn(0, 5));

        var game = new TywGame(4, seed: 7, humans: 0);
        Check("one depot and one train per player", game.Board.Depots.Count == 4 && game.Trains.Count == 4);
        Check("depots sit on the rim", game.Board.Depots.All(d => d.DistanceTo(origin) == game.Board.Radius));
        Check("the board has towns", game.Board.Towns.Count >= 3);
        foreach (var depot in game.Board.Depots)
        {
            var near = game.Board.Tiles.Where(t => t.Hex.DistanceTo(depot) <= 4).Select(t => t.Terrain).ToHashSet();
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

        var woodBefore = player.Bank[(int)TywResource.Wood];
        for (var i = 0; i < 60 * 30; i++)
        {
            game.Tick(1.0f / 30.0f);
        }

        Check("a train hauled wood home within a minute", player.Bank[(int)TywResource.Wood] > woodBefore + 2,
            $"{woodBefore} -> {player.Bank[(int)TywResource.Wood]}");

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

                lengths.Add($"{players}p#{seed}: {match.Time / 60:0.0} min, {match.Players[match.Winner].Name} {match.VictoryPoints(match.Winner)} VP ({match.VictoryBreakdown(match.Winner)})");
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
