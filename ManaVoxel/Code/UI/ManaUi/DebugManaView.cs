using Mana.Code;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The ManaUI counterpart to VoxelEditorPanelsView's ImGui "Debug" tab: the frame-time and memory
    /// graphs, and a live readout of the mouse-picking ray.
    /// <para>
    /// Both graphs bind straight to their ring buffer (<see cref="ManaTime.HistoryMilliseconds"/>,
    /// <see cref="ManaMemoryStats.RawHistory"/>) via <see cref="Sparkline"/> rather than copying a
    /// chronological snapshot out every frame the way the ImGui version's PlotLines calls needed to.
    /// </para>
    /// </summary>
    public static class DebugManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var frameTime = new Sparkline
            {
                Samples = ManaTime.GlobalTime.HistoryMilliseconds,
                Minimum = 0.0f,
                Maximum = 100.0f,
                Threshold = 1000.0f / 60.0f
            };
            frameTime.BindPolled(s => s.WriteCursor,
                () => (ManaTime.GlobalTime.HistoryIndex + 1) % ManaTime.GlobalTime.HistoryMilliseconds.Length);

            var memory = new Sparkline
            {
                Samples = ManaMemoryStats.RawHistory,
                Minimum = 0.0f,
                Maximum = null
            };
            memory.BindPolled(s => s.WriteCursor, () => ManaMemoryStats.WriteCursor);

            var memoryLabel = new TextBlock().Foreground(theme.TextMuted);
            memoryLabel.BindTextPolled(() => $"Working Set: {ManaMemoryStats.Latest:0} MB");

            var rayText = new TextBlock().Foreground(theme.TextMuted);
            rayText.BindTextPolled(() =>
            {
                var origin = viewModel.DebugRayOrigin;
                return $"RAY  {origin.X:0.###}, {origin.Y:0.###}, {origin.Z:0.###}";
            });

            var directionText = new TextBlock().Foreground(theme.TextMuted);
            directionText.BindTextPolled(() =>
            {
                var direction = viewModel.DebugRayDirection;
                return $"DIR  {direction.X:0.###}, {direction.Y:0.###}, {direction.Z:0.###}";
            });

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                new Expander().Header("Timing").Section(theme.Spacing,
                    new TextBlock().Text("Frame time (ms, target 60 fps)").Foreground(theme.TextMuted),
                    frameTime),

                new Expander().Header("Memory").Section(theme.Spacing,
                    memoryLabel,
                    memory),

                new Expander().Header("Ray").Section(theme.Spacing,
                    rayText,
                    directionText));
        }
    }
}
