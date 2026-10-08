using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>The Find Panel dialog's matching.</summary>
public static class PanelSearchChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var panels = new[] { ("worldperf", "World Perf"), ("project", "Project"), ("inspector", "Inspector"), ("import", "Import") };

        check("empty query lists everything", PanelSearchManaView.Filter(panels, "").Count == 4, "");
        check("matches anywhere, ignoring case", PanelSearchManaView.Filter(panels, "PERF").Single().Id == "worldperf", "");
        check("every word must match", PanelSearchManaView.Filter(panels, "world pro").Count == 0, "");
        check("prefix matches come first", PanelSearchManaView.Filter(panels, "in")[0].Title == "Import"
            || PanelSearchManaView.Filter(panels, "in")[0].Title == "Inspector", "");
    }
}
