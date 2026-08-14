using Mana.Code.UI.ManaUi;
using ManaCore.Loading;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The progress bar primitive, the loading screen built on it, and the overlay that puts it over whatever a
/// game mode built.
/// <para>
/// The overlay is the part that matters most: the loading screen used to live on a render path that never
/// touched ManaUI at all, so a mode with panels docked showed nothing of them while loading, and a mode that
/// finished loading had never once laid its own tree out.
/// </para>
/// </summary>
public static class LoadingScreenChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- the progress bar
        var root = new UiRoot(fonts);
        var bar = new ProgressBar { Fraction = 0.4f };

        root.Content = new Border().PaddingOf(10.0f).Content(bar);
        root.UpdateLayout(new Vector2(300, 60));

        var drawn = root.Render().Instances.ToArray();
        Check("a progress bar draws", drawn.Length >= 2, $"{drawn.Length} instances");
        Check("in one draw call", root.Render().Batches.Count == 1, "one batch");

        var barBounds = bar.Bounds;
        var fill = drawn.FirstOrDefault(i => i.Rect.X <= barBounds.X + 0.5f && i.Rect.Z < barBounds.Right - 0.5f);

        Check("the fill is narrower than the track",
            fill.Rect.Z > fill.Rect.X, $"{fill.Rect.Z - fill.Rect.X:0}px of {barBounds.Width:0}px");

        Check("roughly proportional to the fraction",
            MathF.Abs((fill.Rect.Z - fill.Rect.X) / barBounds.Width - 0.4f) < 0.05f,
            $"{(fill.Rect.Z - fill.Rect.X) / barBounds.Width:0.00}");

        bar.Fraction = 1.4f;
        Check("a fraction past 1 is clamped", bar.Fraction <= 1.0f, $"{bar.Fraction}");

        bar.Fraction = -0.4f;
        Check("and past 0 the other way", bar.Fraction >= 0.0f, $"{bar.Fraction}");

        bar.Fraction = 0.0f;
        root.UpdateLayout(new Vector2(300, 60));
        var empty = root.Render().Instances.ToArray();

        Check("nothing fills at zero",
            empty.Count(i => i.Rect.Z - i.Rect.X < barBounds.Width - 0.5f && i.Rect.Z - i.Rect.X > 0.5f) == 0,
            $"{empty.Length} instances");

        var sweep = new ProgressBar { IsIndeterminate = true, ElapsedSeconds = 0.3f, SweepSeconds = 1.4f };
        root.Content = new Border().PaddingOf(10.0f).Content(sweep);
        root.UpdateLayout(new Vector2(300, 60));

        Check("an indeterminate bar still draws something",
            root.Render().Instances.Length >= 2, "band drawn");

        // ---------------------------------------------------------------- the loading screen
        var loading = new ManaLoadingViewModel();
        loading.Start("Loading assets", 0.25f);

        root.Content = ManaLoadingScreenManaView.Build(root.Theme, loading);
        root.UpdateLayout(new Vector2(1200, 800));

        var screenDrawn = root.Render();
        Check("the loading screen renders", screenDrawn.Instances.Length > 0, $"{screenDrawn.Instances.Length}");
        Check("as one draw call", screenDrawn.Batches.Count == 1, $"{screenDrawn.Batches.Count}");

        var texts = FindAll<TextBlock>(root.Content!);
        Check("the status text is on it",
            texts.Any(t => t.Text.StartsWith("Loading assets", StringComparison.Ordinal)),
            string.Join(" | ", texts.Select(t => t.Text)));

        Check("with a percentage",
            texts.Any(t => t.Text == "25%"), string.Join(" | ", texts.Select(t => t.Text)));

        loading.Report(new ManaLoadingProgress("Building scene", 0.8f));
        root.UpdateLayout(new Vector2(1200, 800));
        root.Render();

        var updatedTexts = FindAll<TextBlock>(root.Content!);
        Check("the screen tracks the model rather than a snapshot",
            updatedTexts.Any(t => t.Text.StartsWith("Building scene", StringComparison.Ordinal))
                && updatedTexts.Any(t => t.Text == "80%"),
            string.Join(" | ", updatedTexts.Select(t => t.Text)));

        var card = Find<Border>(root.Content!);
        Check("the card stays centred as the window resizes",
            card != null
                && MathF.Abs(card.Bounds.Center.X - 600.0f) < 1.0f
                && MathF.Abs(card.Bounds.Center.Y - 400.0f) < 1.0f,
            $"{card?.Bounds.Center}");

        root.UpdateLayout(new Vector2(800, 500));
        root.Render();

        var cardAfterResize = Find<Border>(root.Content!);
        Check("and after a resize",
            cardAfterResize != null
                && MathF.Abs(cardAfterResize.Bounds.Center.X - 400.0f) < 1.0f
                && MathF.Abs(cardAfterResize.Bounds.Center.Y - 250.0f) < 1.0f,
            $"{cardAfterResize?.Bounds.Center}");

        // ---------------------------------------------------------------- the overlay
        var isLoading = true;
        var underlying = new Border().Fill(UiColor.FromHex("#204060")).Size(200.0f, 120.0f);
        var screen = new Border().Fill(UiColor.FromHex("#101010")).Size(300.0f, 200.0f);

        var overlay = new ManaUiLoadingOverlay(underlying, screen, () => isLoading);
        root.Content = overlay;
        root.UpdateLayout(new Vector2(1200, 800));

        var loadingInstances = root.Render().Instances.ToArray();
        Check("while loading, the screen is drawn",
            loadingInstances.Any(i => i.FillColor == UiColor.Pack(UiColor.FromHex("#101010"))),
            "screen present");

        isLoading = false;
        root.UpdateLayout(new Vector2(1200, 800));
        var afterInstances = root.Render().Instances.ToArray();

        Check("and hidden once loading finishes",
            !afterInstances.Any(i => i.FillColor == UiColor.Pack(UiColor.FromHex("#101010"))),
            "screen gone");

        Check("with the real content still there",
            afterInstances.Any(i => i.FillColor == UiColor.Pack(UiColor.FromHex("#204060"))),
            "content present");

        // The content underneath is not merely hidden alongside the screen -- it has to be laid out and
        // drawn the whole time, or the first frame after loading shows nothing until a second pass catches
        // up. This is what a docked workspace depends on: it must already be arranged by the time it
        // becomes visible.
        isLoading = true;
        root.UpdateLayout(new Vector2(1200, 800));
        root.Render();

        Check("content underneath is laid out even while hidden",
            underlying.Bounds.Width > 0.0f, $"{underlying.Bounds}");

        // ---------------------------------------------------------------- wrapping real root content
        // UiRoot.Content is a shorthand for its content layer's child, not a detached handle -- so it is
        // still parented to that layer, and reusing it as a child of the overlay without freeing it first
        // is reparenting it onto its own descendant. Every real game mode goes through exactly this path:
        // BuildManaUi assigns Root.Content, then the loading wrap reads it back and rewraps it.
        var wrapRoot = new UiRoot(fonts);
        var workspace = new Border().Fill(UiColor.FromHex("#305070")).Size(400.0f, 300.0f);
        wrapRoot.Content = workspace;

        Check("the workspace is parented to the root before wrapping",
            workspace.Parent != null, workspace.Parent?.GetType().Name ?? "none");

        Exception? thrown = null;
        try
        {
            var existing = wrapRoot.Content ?? new Border();
            existing.RemoveFromParent();

            wrapRoot.Content = new ManaUiLoadingOverlay(
                existing, ManaLoadingScreenManaView.Build(wrapRoot.Theme, new ManaLoadingViewModel()), () => false);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Check("wrapping already-parented content does not throw",
            thrown == null, thrown?.Message ?? "clean");

        wrapRoot.UpdateLayout(new Vector2(1200, 800));
        var wrapped = wrapRoot.Render().Instances.ToArray();

        Check("and the original content still renders",
            wrapped.Any(i => i.FillColor == UiColor.Pack(UiColor.FromHex("#305070"))),
            "workspace present");
    }

    private static T? Find<T>(UiElement element) where T : UiElement => FindAll<T>(element).FirstOrDefault();

    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;

        static void Walk(UiElement element, List<T> found)
        {
            if (element is T match)
            {
                found.Add(match);
            }

            foreach (var child in element.VisualChildren)
            {
                Walk(child, found);
            }
        }
    }
}
