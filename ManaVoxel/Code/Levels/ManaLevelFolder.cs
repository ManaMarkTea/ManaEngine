namespace ManaVoxel.Code.Levels;

/// <summary>The level library's folder hierarchy, shared by the menu and scene browser.</summary>
public sealed class ManaLevelFolder
{
    public string Name { get; }
    public string RelativePath { get; }
    public List<ManaLevelFolder> Children { get; } = [];
    public List<string> Levels { get; } = [];

    private ManaLevelFolder(string name, string relativePath) { Name = name; RelativePath = relativePath; }

    public static ManaLevelFolder Build(IEnumerable<string> paths, string rootFolder)
    {
        var root = new ManaLevelFolder("All Scenes", "");
        foreach (var path in paths)
        {
            // Games' own levels come in under Games/<Name>.
            if (ManaLevelLibrary.RelativePath(path, rootFolder) is not { } relative)
            {
                continue;
            }
            var folder = root;
            foreach (var segment in (Path.GetDirectoryName(relative) ?? "").Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                var child = folder.Children.FirstOrDefault(f => f.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new ManaLevelFolder(segment, Path.Combine(folder.RelativePath, segment));
                    folder.Children.Add(child);
                }
                folder = child;
            }
            folder.Levels.Add(path);
        }
        Sort(root);
        return root;
    }

    private static void Sort(ManaLevelFolder folder)
    {
        folder.Children.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        foreach (var child in folder.Children)
        {
            Sort(child);
        }
    }

    public ManaLevelFolder? Find(string relativePath) => RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)
        ? this : Children.Select(child => child.Find(relativePath)).FirstOrDefault(folder => folder != null);

    public IEnumerable<string> DescendantLevels() => Levels.Concat(Children.SelectMany(child => child.DescendantLevels()));
}
