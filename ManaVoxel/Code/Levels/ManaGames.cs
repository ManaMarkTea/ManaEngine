using ManaVoxel.Code.Assets;
using CodeScreensaver;
using Foundry;
using YachtJousting;
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
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = FoundryMode.ModeType,
                DisplayName = FoundryMode.DisplayName,
                Description = FoundryMode.Description,
                ModeType = typeof(FoundryMode),
                Create = () => new FoundryMode()
            });
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = CodeScreensaverMode.ModeType,
                DisplayName = CodeScreensaverMode.DisplayName,
                Description = CodeScreensaverMode.Description,
                ModeType = typeof(CodeScreensaverMode),
                Create = () => new CodeScreensaverMode()
            });
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = YachtJoustingMode.ModeType,
                DisplayName = YachtJoustingMode.DisplayName,
                Description = YachtJoustingMode.Description,
                ModeType = typeof(YachtJoustingMode),
                Create = () => new YachtJoustingMode((asset, context) =>
                {
                    var prototype = GridMapPrototype.Load(asset, context);
                    if (!prototype.HasGeometry)
                    {
                        throw new InvalidOperationException($"Yacht Jousting asset '{asset}': {prototype.Error}");
                    }
                    return new YachtModel(prototype.BoundsMin, prototype.BoundsMax, prototype.Instantiate);
                })
            });
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
                // Biomes are the folders under it (City, Sky, ...).
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*.manaprefab", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
                        .Select(file => $"{RunnerContent.SectionFolder}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}").ToList()
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
