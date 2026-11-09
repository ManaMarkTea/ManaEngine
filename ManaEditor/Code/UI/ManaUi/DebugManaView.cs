using Mana.Code;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
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
            return new TabView()
                .Add("Diagnostics", BuildDiagnostics(theme, viewModel).Scrollable())
                .Add("Physics", VoxelEditorPanelsManaView.BuildPhysics(theme, viewModel));
        }

        private static UiElement BuildDiagnostics(UiTheme theme, VoxelEditorViewModel viewModel)
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

            // Working set is everything the process holds, native allocations included; this is just what
            // .NET's own heap is carrying, the same number the status bar's performance strip already shows
            // -- the debug panel used to show only working set, with no way to tell how much of it was
            // actually managed memory versus native.
            var managedMemory = new Sparkline
            {
                Samples = ManaMemoryStats.RawManagedHistory,
                Minimum = 0.0f,
                Maximum = null
            };
            managedMemory.BindPolled(s => s.WriteCursor, () => ManaMemoryStats.WriteCursor);

            var managedMemoryLabel = new TextBlock().Foreground(theme.TextMuted);
            managedMemoryLabel.BindTextPolled(() => $"Managed: {ManaMemoryStats.LatestManaged:0} MB");

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
                    memory,
                    managedMemoryLabel,
                    managedMemory),

                new Expander().Header("Ray").Section(theme.Spacing,
                    rayText,
                    directionText));
        }
    }
}
