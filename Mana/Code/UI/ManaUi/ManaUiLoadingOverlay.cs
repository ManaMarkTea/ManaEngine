using ManaUI;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Shows the loading screen over whatever a game mode built, for as long as loading is under way.
    /// <para>
    /// Wrapped around the tree once, in <see cref="Mana.Code.BaseGameMode"/>, rather than left to each game
    /// mode to remember.
    /// </para>
    /// <para>
    /// The content underneath is collapsed while the screen is up rather than merely drawn over, which
    /// stops it costing anything -- <see cref="UiRoot"/> skips a collapsed subtree entirely, both for
    /// layout and for polled bindings. That is not just an optimisation here: this is precisely the window
    /// where a background thread is still mutating the scene graph, and a hidden outliner or inspector
    /// polling it every frame at the same time is a race, not merely wasted work. Collapsing it means the
    /// tree underneath is measured and arranged fresh the instant loading finishes, rather than continuously
    /// through loading for a picture nobody could see.
    /// </para>
    /// </summary>
    public sealed class ManaUiLoadingOverlay : Panel
    {
        private readonly UiElement _content;
        private readonly UiElement _loading;

        public ManaUiLoadingOverlay(UiElement content, UiElement loading, Func<bool> isLoading)
        {
            _content = content;
            _loading = loading;

            Children.Add(content);
            Children.Add(loading);

            // Polled rather than decided once in Arrange: a binding runs every frame regardless of whether
            // layout is dirty, which visibility on its own would not. The two are opposites of each other
            // by construction, so there is exactly one moment where the switch happens rather than a frame
            // where both or neither are visible.
            content.BindPolled(el => el.Visibility, () => isLoading() ? Visibility.Collapsed : Visibility.Visible);
            loading.BindPolled(el => el.Visibility, () => isLoading() ? Visibility.Visible : Visibility.Collapsed);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _content.Measure(availableSize);
            _loading.Measure(availableSize);
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var bounds = new UiRect(Vector2.Zero, finalSize);
            _content.Arrange(bounds);
            _loading.Arrange(bounds);
            return finalSize;
        }
    }
}
