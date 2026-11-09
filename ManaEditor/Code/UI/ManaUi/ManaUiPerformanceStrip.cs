using Mana.Code;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
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

            // Three fixed-width fields rather than one combined string padded with `{value,N:format}`.
            // That padding assumed theme.FontFamilyMono renders every character at the same width, which
            // held only as long as the font it actually resolved to was monospace -- and "RobotoMono-
            // Regular" isn't a font this app ships, so ManaFontManager silently fell back to plain
            // (proportional) Roboto. Different digits have different advance widths in a proportional
            // font, so the combined string's rendered width could shift by a pixel or two exactly when a
            // value crossed a digit boundary (memory going from 99 to 100 MB was what gave it away),
            // which read as the whole status bar jittering. A fixed-width box per stat can't do that
            // regardless of what font ends up drawing the digits.
            var fpsText = StatField(theme, 50.0f);
            var msText = StatField(theme, 50.0f);
            var mbText = StatField(theme, 60.0f);

            var lastMs = 0.0;
            var lastMb = 0.0f;

            // One binding drives the sampling as well as its own text: it is the only thing guaranteed to
            // run every frame the strip is visible, and sampling from the render loop would make the whole
            // engine pay for a readout that might not be on screen. The other two fields just format
            // whatever this one last measured.
            fpsText.BindTextPolled(() =>
            {
                var milliseconds = (float)ManaTime.GlobalTime.DeltaTime.TotalMilliseconds;
                var megabytes = GC.GetTotalMemory(false) / (1024.0f * 1024.0f);

                frames[next] = Math.Clamp(milliseconds, 0.0f, 100.0f);
                memory[next] = megabytes;
                next = (next + 1) % History;

                frameGraph.WriteCursor = next;
                memoryGraph.WriteCursor = next;

                lastMs = ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds;
                lastMb = megabytes;

                return $"{ManaTime.GlobalTime.FramesPerSecondAvg:0.0} fps";
            });

            msText.BindTextPolled(() => $"{lastMs:0.0} ms");
            mbText.BindTextPolled(() => $"{lastMb:0} MB");

            var readout = new StackPanel().Horizontal(6.0f).CenterV().Children(fpsText, msText, mbText);

            // The graphs live in a popup so they take no room until asked for. Anchored under the strip,
            // which is where the pointer already is.
            var detail = new Border()
                .Fill(theme.Frosted(theme.SurfaceRaised))
                .Blur(theme.BackdropBlur)
                .Outline(theme.BorderStrong, 1.0f)
                .Rounded(theme.CornerRadius)
                .Shadow(16.0f)
                .PaddingOf(10.0f)
                .Content(new StackPanel().Vertical(theme.Spacing).Children(
                    Titled(theme, "Frame time", frameGraph, () =>
                        $"{ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds:0.0} ms average"),
                    Titled(theme, "Managed memory", memoryGraph, () =>
                        $"{GC.GetTotalMemory(false) / (1024.0 * 1024.0):0.0} MB")));

            // Frosted rather than solid: the strip sits over the viewport, and it is small enough that an
            // opaque bar reads as a chip stuck to the scene rather than as part of the chrome.
            var strip = new Border()
                .Fill(theme.Frosted(theme.Surface))
                .Blur(theme.BackdropBlur)
                .Outline(theme.Border, 1.0f)
                .Rounded(theme.CornerRadius)
                .PaddingOf(8.0f, 2.0f)
                .Content(readout);

            return new HoverRevealer(detail) { Child = strip };
        }

        /// <summary>A fixed-width, right-aligned readout field -- see the width comment in <see cref="Build"/>.</summary>
        private static TextBlock StatField(UiTheme theme, float width)
        {
            return new TextBlock()
                .FontFamily(theme.FontFamilyMono)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Align(TextAlignment.Right)
                .Width(width);
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
