using ManaUI;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Shows the loading screen over whatever a game mode built, for as long as loading is under way.
    /// <para>
    /// Wrapped around the tree once, in <see cref="Mana.Code.BaseGameMode"/>, rather than left to
    /// each game mode to remember. The content underneath keeps being laid out and rendered while hidden --
    /// cheaper to accept than to special-case, and every panel already has to cope with a world that has no
    /// scenes in it yet, since that is also true for the first frame after loading finishes.
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
            // layout is dirty, which visibility on its own would not.
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
