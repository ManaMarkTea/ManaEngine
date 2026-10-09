using System.Numerics;
using ManaUI;
using ManaVoxel.Code.Gameplay.YachtJousting;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class YachtJoustingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        YachtMatch Ready()
        {
            var match = new YachtMatch(1);
            for (var i = 0; i < 121; i++)
            {
                match.Step(1f / 60, default, default);
            }
            return match;
        }
        void Attack(YachtMatch match, bool block = false, bool back = false)
        {
            var a = match.Boats[0];
            var b = match.Boats[1];
            a.Position = Vector2.Zero;
            b.Position = new Vector2(6, 0);
            b.Facing = back ? 1 : -1;
            a.Charge = 1;
            match.Step(1f / 60, default, new YachtInput(Vector2.Zero, false, block));
        }
        var hit = Ready();
        Attack(hit);
        check("a fully charged lance deals 50 damage", hit.Boats[1].Health == 50, $"{hit.Boats[1].Health}");
        for (var i = 0; i < 10; i++)
        {
            hit.Step(1f / 60, default, default);
        }
        check("a lance hits only once per thrust", hit.Boats[1].Health == 50, "");
        var parry = Ready();
        Attack(parry, true);
        check("a facing shield blocks and spends guard", parry.Boats[1].Health == 100 && parry.Boats[1].Guard < 71, "");
        var behind = Ready();
        Attack(behind, true, true);
        check("a shield cannot block a hit from behind", behind.Boats[1].Health == 50, "");
        var miss = Ready();
        miss.Boats[0].Position = Vector2.Zero;
        miss.Boats[1].Position = new Vector2(6, 4);
        miss.Boats[0].Charge = 1;
        miss.Step(1f / 60, default, default);
        check("steering out of the lane avoids the lance", miss.Boats[1].Health == 100, "");
        var win = Ready();
        win.Boats[1].Health = 40;
        Attack(win);
        check("a knockout awards a point and ends at the target", win.Winner == 0 && win.Boats[0].Score == 1, "");
        var draw = Ready();
        draw.Boats[0].Position = Vector2.Zero;
        draw.Boats[1].Position = new Vector2(6, 0);
        foreach (var boat in draw.Boats)
        {
            boat.Health = 40;
            boat.Charge = 1;
        }
        draw.Step(1f / 60, default, default);
        check("a double knockout replays the round without awarding a point", draw.Winner < 0 && draw.Boats.All(b => b.Score == 0) && draw.RoundWait > 0, "");
        var bot = new YachtMatch(5);
        for (var i = 0; i < 60 * 600 && bot.Winner < 0; i++)
        {
            bot.Step(1f / 60, bot.Bot(0), bot.Bot(1));
        }
        check("a complete first-to-five bot match reaches a winner", bot.Winner >= 0 && bot.ImpactSerial > 0, $"{bot.Boats[0].Score}:{bot.Boats[1].Score}, impacts {bot.ImpactSerial}, {bot.Time:F1}s");
        var warnings = new List<string>();
        var loaded = ManaLevelGameModes.Load(new ManaLevelGameModeEntry { Type = "YachtJousting" }, warnings);
        check("the level can load Yacht Jousting without warnings", loaded is YachtJoustingMode && warnings.Count == 0, string.Join("; ", warnings));
    }
}
