using ManaUI;
using ManaEditor.Code.Levels;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Save Level As: the box offers a copy name nobody has taken, in the open level's own folder, says where it will land,
/// saves there on Enter (as a new file, through the host) and goes away; Escape lets go without saving.
/// </summary>
public static class SaveLevelAsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var host = new Host();
        var levels = new LevelViewModel { Host = host };
        var opened = Path.Combine(ManaLevelLibrary.ResolveFolder(), "Tests", "Terrain" + ".manalevel");
        levels.MarkOpened(opened);

        var root = new UiRoot(fonts);
        var anchor = new Button().Label("Save As...");
        root.Content = anchor;
        var size = new Vector2(1200, 800);
        root.UpdateLayout(size);

        var popup = VoxelEditorToolbarManaView.ShowSaveAs(UiTheme.Dark(), levels, anchor);
        root.UpdateLayout(size);
        var name = Walk(popup!).OfType<TextBox>().First();
        check("the box offers a copy of the open level's name", name.Text == "Terrain copy", name.Text);
        var folders = LevelViewModel.SaveFolders(opened);
        check("in the open level's own folder first", folders[0].Label.EndsWith("Content/Levels/Tests", StringComparison.Ordinal), folders[0].Label);
        check("then the main folder", folders.Any(f => f.Label.EndsWith("Content/Levels", StringComparison.Ordinal)), string.Join(", ", folders.Select(f => f.Label)));
        check("and the name is ready to type over", root.FocusManager.Focused == name, "");

        root.Input.InjectKeyDown(UiKey.Enter);
        root.UpdateLayout(size);
        check("Enter saves the copy there, as a new file", host.SavedAs == Path.Combine(folders[0].Folder, "Terrain copy.manalevel"), host.SavedAs ?? "nothing");
        check("and the box goes away", !root.Popups.HasOpenPopups, "");

        host.SavedAs = null;
        VoxelEditorToolbarManaView.ShowSaveAs(UiTheme.Dark(), levels, anchor);
        root.UpdateLayout(size);
        root.Input.InjectKeyDown(UiKey.Escape);
        root.UpdateLayout(size);
        check("Escape lets go without saving", host.SavedAs == null && !root.Popups.HasOpenPopups, "");

        check("a name with nothing usable in it saves nothing", LevelViewModel.SaveAsPath("  /:* ", folders[0].Folder) == null, "");
        levels.ShowSaveAs = null;
        levels.RequestSaveAs();
        check("asked for with no toolbar, it says so rather than doing nothing", levels.Status.Contains("toolbar"), levels.Status);
    }

    private static IEnumerable<UiElement> Walk(UiElement element)
    {
        yield return element;
        for (var i = 0; i < element.VisualChildCount; i++)
        {
            foreach (var below in Walk(element.GetVisualChild(i)))
            {
                yield return below;
            }
        }
    }

    private sealed class Host : IManaLevelHost
    {
        public string? SavedAs;
        public bool IsPlaying => false;
        public bool CanPlay => true;
        public bool HasPlayerStart => false;
        public void OpenLevel(string path) { }
        public void SaveLevel(string path) { }
        public void SaveLevelAs(string path) => SavedAs = path;
        public void NewLevel() { }
        public void SaveScene(Mana.ManaScene scene) { }
        public bool SceneHasChanges(Mana.ManaScene scene) => false;
        public void PlacePlayerStart() { }
        public void TogglePlay() { }
        public void PlayFromCamera() { }
        public void PlayInNewWindow() { }
    }
}
