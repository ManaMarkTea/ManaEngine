using ImGuiNET;
using ManaCore.Loading;

namespace Mana.Code.UI
{
    public static class ManaLoadingScreenView
    {
        public static void Draw(ManaLoadingViewModel loadingModel)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);
            ImGui.Begin(
                "LoadingScreen",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoBackground);

            var windowSize = viewport.Size;
            var boxWidth = Math.Min(500.0f, windowSize.X * 0.8f);
            const float boxHeight = 160.0f;
            var boxPos = new System.Numerics.Vector2(
                (windowSize.X - boxWidth) * 0.5f,
                (windowSize.Y - boxHeight) * 0.5f);

            ImGui.SetCursorPos(boxPos);
            ImGui.BeginChild(
                "LoadingBox",
                new System.Numerics.Vector2(boxWidth, boxHeight),
                ImGuiChildFlags.Border,
                ImGuiWindowFlags.NoScrollbar);

            var time = ManaTime.GlobalTime.GameTime.TotalSeconds;
            var pulse = 0.6f + 0.4f * (float)Math.Sin(time * 3.0);
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                new System.Numerics.Vector4(0.16f, 0.52f, 0.86f, pulse));
            ImGui.Text("MANA RENDERER");
            ImGui.PopStyleColor();

            ImGui.Separator();
            ImGui.Spacing();

            var dotCount = (int)(time * 2.0) % 4;
            var dots = new string('.', dotCount);
            var loading = loadingModel.Snapshot;
            ImGui.Text($"{loading.Status}{dots}");
            ImGui.ProgressBar(
                loading.Fraction,
                new System.Numerics.Vector2(-1, 22),
                $"{loading.Fraction * 100:0}%");

            ImGui.EndChild();
            ImGui.End();
        }
    }
}
