using ManaUI;
using ManaVoxel.Code.UI.ManaUi;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

public static class ScenesChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        string[] paths = ["Levels/Little Pond.manalevel", "Levels/Little Train Yard.manalevel", "Levels/2D Sample.manalevel"];
        check("blank search preserves library order", ScenesManaView.Filter(paths, "  ").SequenceEqual(paths), "");
        check("search matches all words regardless of case or order",
            ScenesManaView.Filter(paths, " YARD little ").SequenceEqual(new[] { paths[1] }), "");
        check("search excludes folder and extension names", ScenesManaView.Filter(paths, "Levels").Count == 0
            && ScenesManaView.Filter(paths, "manalevel").Count == 0, "");
        check("unmatched search is empty", ScenesManaView.Filter(paths, "missing").Count == 0, "");

        var folder = Path.Combine(Path.GetTempPath(), "ManaScenes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pictures = Path.Combine(folder, "Pictures");
            Directory.CreateDirectory(pictures);
            var level = Path.Combine(folder, "Little Pond.manalevel");
            File.WriteAllText(level, "{}");
            var nestedFolder = Path.Combine(folder, "Tests", "Water");
            Directory.CreateDirectory(nestedFolder);
            var nested = Path.Combine(nestedFolder, "Little Pond.manalevel");
            File.WriteAllText(nested, "{}");
            var siblingFolder = Path.Combine(folder, "Tests Extra");
            Directory.CreateDirectory(siblingFolder);
            var sibling = Path.Combine(siblingFolder, "Other.manalevel");
            File.WriteAllText(sibling, "{}");
            var discovered = ManaLevelLibrary.List(folder);
            check("level discovery includes arbitrarily nested folders", discovered.Count == 3 && discovered.Contains(nested), "");
            var hierarchy = ManaLevelFolder.Build(discovered, folder);
            var testsFolder = hierarchy.Find("Tests")!;
            check("folder selection includes descendants and excludes similarly named siblings",
                testsFolder.DescendantLevels().SequenceEqual(new[] { nested }), "");
            check("root scenes and duplicate scene names keep their distinct paths", hierarchy.Levels.Single() == level
                && hierarchy.DescendantLevels().Count() == 3, "");
            var host = new LevelHost();
            var menuLevels = new LevelViewModel { Host = host };
            menuLevels.MarkOpened(nested);
            var menu = LevelMenu.Build(hierarchy, menuLevels);
            var nestedMenu = menu.Single(item => item.Text == "Tests").Items.Single(item => item.Text == "Water").Items.Single();
            check("nested folders become submenus with checked scene leaves", nestedMenu.Text == "Little Pond"
                && nestedMenu.Tooltip == nested && nestedMenu.IsCheckedProvider?.Invoke() == true && nestedMenu.Invoked != null, "");
            check("root scene remains a direct menu item", menu.Single(item => item.Text == "Little Pond").HasSubmenu == false, "");
            nestedMenu.Invoke();
            check("a nested menu leaf opens the full scene path", host.Opened == nested, "");
            menuLevels.Save();
            check("saving a nested scene keeps its folder", host.Saved == nested, "");
            check("missing preview uses placeholder", ScenesManaView.PreviewPath(level) == null, "");
            var jpg = Path.Combine(pictures, "Little Pond.jpg");
            File.WriteAllBytes(jpg, []);
            check("authored JPEG is found beside the level", ScenesManaView.PreviewPath(level) == jpg, "");
            var png = Path.Combine(pictures, "Little Pond.png");
            File.WriteAllBytes(png, []);
            check("PNG takes precedence over JPEG", ScenesManaView.PreviewPath(level) == png, "");
            check("another scene never borrows this preview",
                ScenesManaView.PreviewPath(Path.Combine(folder, "Other.manalevel")) == null, "");
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        var levels = new LevelViewModel();
        var ui = new UiRoot(fonts);
        ui.Content = ScenesManaView.Build(ui.Theme, levels);
        void Layout() { ui.UpdateLayout(new Vector2(760, 520)); ui.UpdateLayout(new Vector2(760, 520)); }
        Layout();
        var tree = Find<TreeView>(ui.Content).Single();
        var rootFolder = (ManaLevelFolder)tree.SelectedItem!;
        var selectedTests = rootFolder.Find("Tests")!;
        tree.SelectedItem = selectedTests;
        Layout();
        var tiles = Find<VirtualWrapPanel>(ui.Content).Single();
        check("folder outliner sits to the left of scene tiles", tree.Bounds.Right <= tiles.Bounds.Left, "");
        check("selecting a folder filters the scene panel", tiles.Items.Count == selectedTests.DescendantLevels().Count(), "");
        levels.Refresh(); Layout();
        tree = Find<TreeView>(ui.Content).Single();
        check("refresh preserves the selected folder", (tree.SelectedItem as ManaLevelFolder)?.RelativePath == "Tests", "");
        var search = Find<TextBox>(ui.Content).Single();
        search.Text = "Particles"; Layout();
        tiles = Find<VirtualWrapPanel>(ui.Content).Single();
        check("scene search filters within the selected folder", tiles.Items.Count == ScenesManaView.Filter(selectedTests.DescendantLevels(), "Particles").Count, "");
        check("the folder scene panel renders", ui.Render().InstanceCount > 0, "");
    }

    private static IEnumerable<T> Find<T>(UiElement element) where T : UiElement
    {
        if (element is T found)
        {
            yield return found;
        }
        foreach (var child in element.VisualChildren)
        {
            foreach (var descendant in Find<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class LevelHost : IManaLevelHost
    {
        public string? Opened, Saved;
        public bool IsPlaying => false;
        public bool CanPlay => true;
        public bool HasPlayerStart => false;
        public void OpenLevel(string path) => Opened = path;
        public void SaveLevel(string path) => Saved = path;
        public void NewLevel() { }
        public void SaveScene(Mana.ManaScene scene) { }
        public bool SceneHasChanges(Mana.ManaScene scene) => false;
        public void PlacePlayerStart() { }
        public void TogglePlay() { }
        public void PlayFromCamera() { }
        public void PlayInNewWindow() { }
    }
}
