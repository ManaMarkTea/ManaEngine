using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

public static class GpuResourceReportChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");

        var before = new Dictionary<string, long> { ["buffers"] = 10, ["textures"] = 4, ["samplers"] = 2 };
        var same = new Dictionary<string, long>(before);
        Check("a report that did not move has no changes", ManaGpuResourceReport.Changes(before, same).Count == 0);

        var after = new Dictionary<string, long> { ["buffers"] = 12, ["textures"] = 3, ["samplers"] = 2, ["bindGroups"] = 5 };
        var changes = ManaGpuResourceReport.Changes(before, after).ToDictionary(change => change.Key, change => change.Value);
        Check("growth is positive", changes["buffers"] == 2);
        Check("shrinking is negative", changes["textures"] == -1);
        Check("a kind that is new counts from zero", changes["bindGroups"] == 5);
        Check("an unchanged kind is left out", !changes.ContainsKey("samplers"));

        var gone = ManaGpuResourceReport.Changes(before, new Dictionary<string, long>());
        Check("a kind that vanished reads as its whole count lost", gone.Count == 3 && gone.All(change => change.Value < 0));

        Check("format leaves empty kinds out and sorts the rest",
            ManaGpuResourceReport.Format(new Dictionary<string, long> { ["textures"] = 4, ["buffers"] = 10, ["samplers"] = 0 }) == "buffers=10, textures=4");
        Check("format of nothing says so", ManaGpuResourceReport.Format(new Dictionary<string, long>()) == "nothing live");
    }
}
