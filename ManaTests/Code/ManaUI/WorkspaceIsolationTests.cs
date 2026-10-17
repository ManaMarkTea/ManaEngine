using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

public static class WorkspaceIsolationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var first = new UiRoot(fonts);
        var second = new UiRoot(fonts);
        first.Content = EditorWorkspace.Attach(first, new ManaUiShowcaseViewModel(), persist: false);
        second.Content = EditorWorkspace.Attach(second, new ManaUiShowcaseViewModel(), persist: false);
        var firstWorkspace = EditorWorkspace.For(first);
        var secondWorkspace = EditorWorkspace.For(second);
        firstWorkspace.PlaceSearch.Query = "first workspace";
        firstWorkspace.Assets.Library.Filter = "first assets";
        var library = firstWorkspace.Assets.Library;
        check("asset browser filters belong to their workspace", secondWorkspace.Assets.Library.Filter.Length == 0, "");
        check("location search belongs to its workspace", secondWorkspace.PlaceSearch.Query.Length == 0, "");
        check("drag controllers belong to their roots",
            firstWorkspace.DragController != null && secondWorkspace.DragController != null
            && firstWorkspace.DragController != secondWorkspace.DragController, "");

        firstWorkspace.RequestPanel("about");
        second.UpdateLayout(new Vector2(1280, 900));
        check("another root cannot consume a panel request", !(second.Content is { } secondContent && HasDiagnostics(secondContent)), "");
        first.UpdateLayout(new Vector2(1280, 900));
        check("the intended root consumes its panel request", first.Content is { } firstContent && HasDiagnostics(firstContent), "");

        var secondController = secondWorkspace.DragController;
        first.RequestTheme(UiThemeKind.Light);
        first.UpdateLayout(new Vector2(1280, 900));
        check("theme rebuild retains workspace search state",
            EditorWorkspace.For(first) == firstWorkspace && firstWorkspace.PlaceSearch.Query == "first workspace", "");
        check("theme rebuild leaves another root's controller alone", secondWorkspace.DragController == secondController, "");
        check("theme rebuild retains the asset browser model",
            firstWorkspace.Assets.Library == library && library.Filter == "first assets", "");
    }

    private static bool HasDiagnostics(UiElement element) =>
        (element is DockHost host && host.AllItems().Any(item => item.Id == "about"))
        || element.VisualChildren.Any(HasDiagnostics);
}
