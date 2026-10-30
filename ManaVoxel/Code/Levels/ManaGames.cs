using Mana;
using Mana.Code;
using Mana.Code.Nodes;
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
        // A prefab built once in a holding scene: the template every placement of it copies.
        private static ManaSceneNode PrefabTemplate(string asset, Mana.Code.ManaContext context, ManaScene? into = null)
        {
            var root = ManaContentLibrary.RootFor(context.World);
            var file = Mana.Code.Content.ManaContentMounts.FullPath(root, asset) ?? throw new FileNotFoundException($"No content folder for '{asset}'.");
            var node = ManaPrefabRuntime.Instantiate(context, into ?? new Mana.ManaScene(context) { Name = "Prefab Prototypes" }, new ManaLevelNode
            {
                Kind = Mana.Code.Levels.ManaLevelSourceKind.Prefab,
                Name = Path.GetFileNameWithoutExtension(asset),
                Prefab = new ManaPrefabReference { AssetPath = asset, AssetId = ManaPrefab.ReadId(file) }
            });
            node.UpdateTransform();
            return node;
        }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            // Every game reads models and its own prefabs (@Name/Prefabs/...) through one loader, for their meshes.
            Mana.Code.Models.ManaModel.Loader = (asset, context, into) =>
            {
                if (asset.EndsWith(ManaPrefab.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    return PrefabTemplate(asset, context, into);
                }
                return ManaGridMapNode.LoadAsset(ManaGridMapNode.AssetOf(asset), context.World, into);
            };
            // Building pictures for the Foundry hotbar: the editor's own CPU thumbnail renderer, on a throwaway world.
            Foundry.View.FoundryIcons.Renderer = asset =>
            {
                var world = new Mana.ManaWorld();
                var context = new Mana.Code.ManaContext(null!, null!, world) { DeferGpuUploads = true };
                world.Context = context;
                var path = asset.EndsWith(ManaPrefab.Extension, StringComparison.OrdinalIgnoreCase)
                    ? Mana.Code.Content.ManaContentMounts.FullPath(ManaContentLibrary.Root, asset) ?? asset
                    : asset;
                return MeshThumbnailCache.GenerateViaCpuStandalone(context, path);
            };
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
                Create = () => new YachtJoustingMode()
            });
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
                    AssetId = ManaPrefab.ReadId(Mana.Code.Content.ManaContentMounts.FullPath(Mana.Code.Content.ManaContentAssets.Root, path) ?? path)
                }
            });
            RunnerContent.Destroy = ManaPrefabRuntime.Destroy;
            RunnerContent.Sections = () =>
            {
                var folder = Mana.Code.Content.ManaContentMounts.FullPath(Mana.Code.Content.ManaContentAssets.Root, RunnerContent.SectionFolder) ?? string.Empty;
                // Biomes are the folders under it (City, Sky, ...).
                return folder.Length > 0 && Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*.manaprefab", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
                        .Select(file => $"{RunnerContent.SectionFolder}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}").ToList()
                    : [];
            };
            // Contour's ships and turrets are prefabs carrying its own scripts, placed while it plays.
            Contour.ContourContent.Place = RunnerContent.Place;
            Contour.ContourContent.Destroy = ManaPrefabRuntime.Destroy;
            Contour.ContourContent.LoadMission = (context, scene, path) =>
            {
                var file = Mana.Code.Content.ManaContentMounts.FullPath(Mana.Code.Content.ManaContentAssets.Root, path);
                if (file == null || !File.Exists(file))
                {
                    return null;
                }
                var level = ManaLevel.Load(file);
                var loaded = ManaLevelLoader.Load(level, context, scene, replaceAuthoredNodes: false);
                foreach (var warning in loaded.Warnings)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Contour: {Path.GetFileName(file)}: {warning}");
                }
                var warnings = new List<string>();
                var settings = (level.GameModes ?? [])
                    .Where(entry => entry.Type == Contour.ContourMode.ModeType)
                    .Select(entry => ManaLevelGameModes.Load(entry, warnings))
                    .OfType<Contour.ContourMode>()
                    .FirstOrDefault();
                return new Contour.ContourMissionLoad(scene.Children.ToList(), settings);
            };
            ManaLevelComponentScripts.Register(new ManaLevelScriptComponentDefinition
            {
                Type = Contour.Scripts.ContourUnitComponent.ScriptType,
                ComponentType = typeof(Contour.Scripts.ContourUnitComponent),
                Create = node => new Contour.Scripts.ContourUnitComponent(node)
            });
            ManaLevelComponentScripts.Register(new ManaLevelScriptComponentDefinition
            {
                Type = Contour.Scripts.ContourSpawnComponent.ScriptType,
                ComponentType = typeof(Contour.Scripts.ContourSpawnComponent),
                Create = node => new Contour.Scripts.ContourSpawnComponent(node)
            });
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = Contour.ContourMode.ModeType,
                DisplayName = Contour.ContourMode.DisplayName,
                Description = Contour.ContourMode.Description,
                ModeType = typeof(Contour.ContourMode),
                Create = () => new Contour.ContourMode()
            });
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
