using ManaCore.Loading;
using ManaUI;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// The startup loading screen, ported from ImGui.
    /// <para>
    /// Kept generic across game modes rather than owned by one: any mode with ManaUI enabled gets it for
    /// free, wrapped around whatever it builds by <see cref="ManaUiLoadingOverlay"/>. A mode without ManaUI
    /// has no loading screen at all -- the ImGui window that used to stand in for one went with ImGui
    /// stays rather than being deleted alongside this port.
    /// </para>
    /// </summary>
    public static class ManaLoadingScreenManaView
    {
        private const float CardWidth = 460.0f;
        private const float CardHeight = 168.0f;

        public static UiElement Build(UiTheme theme, ManaLoadingViewModel loading)
        {
            var title = new TextBlock()
                .Text("MANA RENDERER")
                .FontFamily(theme.FontFamilyMedium)
                .FontSize(theme.FontSizeHeading)
                .CenterH();

            // A slow pulse rather than a static heading, so the screen visibly hasn't hung on a step that
            // happens to take a while -- the one thing a progress bar alone cannot say once it stalls.
            title.BindPolled(t => t.Foreground, () =>
            {
                var time = ManaTime.GlobalTime.GameTime.TotalSeconds;
                var pulse = 0.6f + 0.4f * (float)Math.Sin(time * 3.0);
                return theme.Accent with { W = pulse };
            });

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterH();

            status.BindTextPolled(() =>
            {
                var time = ManaTime.GlobalTime.GameTime.TotalSeconds;
                var dots = new string('.', (int)(time * 2.0) % 4);
                return loading.Status + dots;
            });

            var bar = new ProgressBar { SweepSeconds = 1.4f };

            bar.BindPolled(b => b.Fraction, () => loading.Progress);
            bar.BindPolled(b => b.ElapsedSeconds, () => (float)ManaTime.GlobalTime.GameTime.TotalSeconds);

            var percent = new TextBlock().FontFamily(theme.FontFamilyMono).FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted).CenterH();

            percent.BindTextPolled(() => $"{loading.Progress * 100.0f:0}%");

            var card = new Border()
                .Fill(theme.Surface)
                .Outline(theme.Border, 1.0f)
                .Rounded(theme.CornerRadiusLarge)
                .Shadow(24.0f)
                .PaddingOf(28.0f, 24.0f)
                .Width(CardWidth)
                .Height(CardHeight)
                .Content(new StackPanel().Vertical(12.0f).Children(
                    title,
                    status,
                    bar,
                    percent));

            // Centred by giving the canvas slot the whole window and letting the card's own alignment place
            // it within that -- Arrange centres a fixed-size element inside a larger rect on its own, so
            // this needs no arithmetic here and stays centred through a resize for free.
            card.CenterH().CenterV();

            // A backdrop over the whole window rather than a floating card alone: the scene behind it is
            // either not loaded yet or is the previous one being torn down, and neither is worth seeing.
            return new Border()
                .Fill(theme.Background)
                .Content(new Canvas().Children(
                    card.Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f)));
        }
    }
}
