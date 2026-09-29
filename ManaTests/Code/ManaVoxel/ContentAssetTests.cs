using Mana;
using Mana.Code;
using Mana.Code.Levels;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// Every prefab in Content/ loads, validates and places into a level without a warning -- so a prefab written
/// by hand (or by an agent) as JSON is checked the same way as one the editor saved.
/// </summary>
public static class ContentAssetChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var prefabs = ManaContentLibrary.List().Where(entry => entry.Kind == "Prefab").ToList();
        var broken = new List<string>();
        foreach (var entry in prefabs)
        {
            try
            {
                var prefab = ManaPrefab.Load(entry.Path);
                var world = new ManaWorld();
                var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
                world.Context = context;
                world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
                world.Scenes.Add(world.DefaultScene);

                var level = new ManaLevel
                {
                    Nodes =
                    [
                        new ManaLevelNode
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = prefab.Name,
                            Kind = ManaLevelSourceKind.Prefab,
                            Prefab = new ManaPrefabReference { AssetId = prefab.Id, AssetPath = ManaContentLibrary.Reference(entry.Path) }
                        }
                    ]
                };
                var result = ManaLevelLoader.Load(level, context, world.DefaultScene, replaceAuthoredNodes: false);
                var instance = result.Nodes.FirstOrDefault(node => node.PrefabLink != null);
                if (instance == null || instance.Children.Count != 1 || result.Warnings.Count > 0)
                {
                    broken.Add($"{entry.RelativePath}: {(instance == null ? "no instance" : $"{instance.Children.Count} roots")} {string.Join("; ", result.Warnings)}");
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                broken.Add($"{entry.RelativePath}: {exception.Message}");
            }
        }

        check("every prefab in Content loads and places into a level without a warning", broken.Count == 0,
            broken.Count == 0 ? $"{prefabs.Count} prefabs" : string.Join(" | ", broken));
    }
}
