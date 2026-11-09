using ManaUI;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.UI.ManaUi;

public static class LevelMenu
{
    public static IReadOnlyList<MenuItem> Build(ManaLevelFolder folder, LevelViewModel levels)
    {
        var items = new List<MenuItem>();
        foreach (var child in folder.Children)
        {
            items.Add(new MenuItem(child.Name).WithItems(Build(child, levels).ToArray()));
        }
        foreach (var path in folder.Levels)
        {
            items.Add(new MenuItem(ManaLevelLibrary.DisplayName(path), () => levels.Open(path))
                .WithTooltip(path).Checkable(() => string.Equals(levels.CurrentPath, path, StringComparison.OrdinalIgnoreCase)));
        }
        return items;
    }
}
