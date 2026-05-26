using ImGuiNET;

namespace Mana.Code.UI
{
    public class WorldPerfPanelView
    {
        public void Draw(IReadOnlyList<ManaPerformanceLog> logs)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("World Perf");
            foreach (var perf in logs)
            {
                ImGui.Text($"{perf.Duration.Milliseconds}ms - {perf.Name}");
            }
            ImGui.End();
        }
    }
}
