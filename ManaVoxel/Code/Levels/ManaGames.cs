using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using Runner;
using TrainYardWars;
using TrainYardWars.View;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// The games that live in projects of their own (<c>Games/</c>): the editor lists their level game modes and lends
    /// them what only the app has, such as mesh import.
    /// </summary>
    internal static class ManaGames
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            TywModel.Loader = (asset, world, into) => ManaGridMapNode.LoadAsset(ManaGridMapNode.AssetOf(asset), world, into);
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = TrainYardWarsMode.ModeType,
                DisplayName = TrainYardWarsMode.DisplayName,
                Description = TrainYardWarsMode.Description,
                ModeType = typeof(TrainYardWarsMode),
                Create = () => new TrainYardWarsMode()
            });

            // The runner's track is laid from section prefabs, which only the app can place.
            RunnerContent.Place = (context, scene, path, position) => ManaPrefabRuntime.Instantiate(context, scene, new ManaLevelNode
            {
                Kind = Mana.Code.Levels.ManaLevelSourceKind.Prefab,
                Name = Path.GetFileNameWithoutExtension(path),
                Position = [position.X, position.Y, position.Z],
                // Prefabs are found by id (the path is the hint), so the file's own id goes with it.
                Prefab = new ManaPrefabReference
                {
                    AssetPath = path,
                    AssetId = ManaPrefab.ReadId(Path.Combine(Mana.Code.Content.ManaContentAssets.Root, path))
                }
            });
            RunnerContent.Destroy = ManaPrefabRuntime.Destroy;
            RunnerContent.Sections = () =>
            {
                var folder = Path.Combine(Mana.Code.Content.ManaContentAssets.Root, RunnerContent.SectionFolder);
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*.manaprefab").Order(StringComparer.OrdinalIgnoreCase)
                        .Select(file => $"{RunnerContent.SectionFolder}/{Path.GetFileName(file)}").ToList()
                    : [];
            };
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = RunnerMode.ModeType,
                DisplayName = RunnerMode.DisplayName,
                Description = RunnerMode.Description,
                ModeType = typeof(RunnerMode),
                Create = () => new RunnerMode()
            });
        }
    }
}
