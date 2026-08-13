using Mana.Code;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Frame rate and memory, as a small always-visible strip rather than a panel.
    /// <para>
    /// These are the two numbers worth glancing at constantly and reading properly almost never, which is
    /// the opposite of what a docked panel is for -- a panel you keep open costs a column, and one you
    /// keep closed tells you nothing. The strip is a line of text; the graphs are behind a hover, where
    /// they cost nothing until wanted.
    /// </para>
    /// </summary>
    public static class ManaUiPerformanceStrip
    {
        /// <summary>
        /// Frames kept. Enough to show the shape of a stutter without a bad second averaging away.
        /// </summary>
        private const int History = 120;

        public static UiElement Build(UiTheme theme)
        {
            var frames = new float[History];
            var memory = new float[History];
            var next = 0;

            var frameGraph = Graph(theme, frames, maximum: 33.0f, threshold: 16.7f);
            var memoryGraph = Graph(theme, memory, maximum: null, threshold: null);

            var readout = new TextBlock()
                .FontFamily(theme.FontFamilyMono)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .CenterV();

            // One binding drives both the text and the sampling: it is the only thing guaranteed to run
            // every frame the strip is visible, and sampling from the render loop would make the whole
            // engine pay for a readout that might not be on screen.
            readout.BindTextPolled(() =>
            {
                var milliseconds = (float)ManaTime.GlobalTime.DeltaTime.TotalMilliseconds;
                var megabytes = GC.GetTotalMemory(false) / (1024.0f * 1024.0f);

                frames[next] = Math.Clamp(milliseconds, 0.0f, 100.0f);
                memory[next] = megabytes;
                next = (next + 1) % History;

                frameGraph.WriteCursor = next;
                memoryGraph.WriteCursor = next;

                return $"{ManaTime.GlobalTime.FramesPerSecondAvg,6:0.0} fps"
                    + $"{ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds,6:0.0} ms"
                    + $"{megabytes,7:0} MB";
            });

            // The graphs live in a popup so they take no room until asked for. Anchored under the strip,
            // which is where the pointer already is.
            var detail = new Border()
                .Fill(theme.SurfaceRaised)
                .Outline(theme.BorderStrong, 1.0f)
                .Rounded(theme.CornerRadius)
                .Shadow(16.0f)
                .PaddingOf(10.0f)
                .Content(new StackPanel().Vertical(theme.Spacing).Children(
                    Titled(theme, "Frame time", frameGraph, () =>
                        $"{ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds:0.0} ms average"),
                    Titled(theme, "Managed memory", memoryGraph, () =>
                        $"{GC.GetTotalMemory(false) / (1024.0 * 1024.0):0.0} MB")));

            var strip = new Border()
                .Fill(theme.Surface)
                .Outline(theme.Border, 1.0f)
                .Rounded(theme.CornerRadius)
                .PaddingOf(8.0f, 2.0f)
                .Content(readout);

            return new HoverRevealer(detail) { Child = strip };
        }

        private static Sparkline Graph(UiTheme theme, float[] samples, float? maximum, float? threshold)
        {
            return new Sparkline
            {
                Samples = samples,
                Minimum = 0.0f,
                Maximum = maximum,
                Threshold = threshold,
                Width = 240.0f,
                Height = 46.0f
            };
        }

        private static UiElement Titled(UiTheme theme, string title, Sparkline graph, Func<string> summary)
        {
            return new StackPanel().Vertical(2.0f).Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                    new TextBlock()
                        .Text(title)
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .GridColumn(0),
                    new TextBlock()
                        .BindTextPolled(summary)
                        .FontFamily(theme.FontFamilyMono)
                        .FontSize(theme.FontSizeSmall)
                        .GridColumn(1)),
                graph);
        }
    }

    /// <summary>
    /// Shows something in a popup while the pointer is over it, and takes it away when the pointer leaves.
    /// <para>
    /// Not a tooltip: a tooltip waits for the pointer to settle and shows text. This is for detail that
    /// should appear as soon as you look and stay while you do -- and, being a popup, costs nothing in
    /// layout until it is on screen.
    /// </para>
    /// </summary>
    public sealed class HoverRevealer : Decorator
    {
        private readonly UiElement _detail;

        private UiPopup? _popup;

        public HoverRevealer(UiElement detail)
        {
            _detail = detail;
        }

        protected override bool IsSelfHitTestVisible => true;

        protected override void OnPointerEntered(UiPointerEventArgs e)
        {
            base.OnPointerEntered(e);

            if (_popup != null || Root == null)
            {
                return;
            }

            // Detached first: the popup is rebuilt each time it is shown, and the detail is one element
            // that the last popup still holds.
            _detail.RemoveFromParent();

            _popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                StaysOpen = true,

                // Transparent to the pointer, so moving onto the graphs does not count as leaving the
                // strip -- which would close them, which would put the pointer back, forever.
                IsHitTestVisible = false,
                Child = _detail
            };

            Root.Popups.Show(_popup, this);
        }

        protected override void OnPointerExited(UiPointerEventArgs e)
        {
            base.OnPointerExited(e);
            Hide();
        }

        private void Hide()
        {
            if (_popup == null)
            {
                return;
            }

            var popup = _popup;
            _popup = null;
            Root?.Popups.Close(popup);
        }
    }
}
