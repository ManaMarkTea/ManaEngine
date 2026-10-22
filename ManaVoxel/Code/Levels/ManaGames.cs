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
        private static ManaSceneNode PrefabTemplate(string asset, Mana.Code.ManaContext context)
        {
            var root = ManaContentLibrary.RootFor(context.World);
            var file = Mana.Code.Content.ManaContentMounts.FullPath(root, asset) ?? throw new FileNotFoundException($"No content folder for '{asset}'.");
            var node = ManaPrefabRuntime.Instantiate(context, new Mana.ManaScene(context) { Name = "Prefab Prototypes" }, new ManaLevelNode
            {
                Kind = Mana.Code.Levels.ManaLevelSourceKind.Prefab,
                Name = Path.GetFileNameWithoutExtension(asset),
                Prefab = new ManaPrefabReference { AssetPath = asset, AssetId = ManaPrefab.ReadId(file) }
            });
            node.UpdateTransform();
            return node;
        }

        // A prefab for a game to place again and again: each copy keeps the prefab's tree, so a game can move a part
        // (a turret's head) by its name, and shares the template's meshes and materials, so a copy costs no GPU buffers.
        private static Foundry.View.FoundryModel PrefabModel(string asset, Mana.Code.ManaContext context)
        {
            var template = PrefabTemplate(asset, context);
            var prototype = GridMapPrototype.FromNode(Path.GetFileNameWithoutExtension(asset), template);
            if (!prototype.HasGeometry)
            {
                throw new InvalidOperationException($"Foundry asset '{asset}': {prototype.Error}");
            }
            // The placed instance holds the prefab's one root node; a copy is of that root, placed where it is asked for.
            var source = template;
            if (template.Children.Count == 1)
            {
                source = template.Children[0];
            }
            return new Foundry.View.FoundryModel(prototype.BoundsMin, prototype.BoundsMax, (into, name, model) =>
            {
                var copy = Copy(into, source);
                copy.Name = name;
                SetTransform(copy, source.LocalTransform * model);
                return copy;
            });
        }

        private static ManaSceneNode Copy(Mana.Code.ManaContext context, ManaSceneNode node)
        {
            ManaSceneNode copy;
            if (node.Components.OfType<MeshRendererComponent>().FirstOrDefault(r => r.Mesh != null) is { } renderer)
            {
                var mesh = new ManaMeshSceneNode(context, renderer.Mesh);
                mesh.MeshRenderer.Material = renderer.Material;
                copy = mesh;
            }
            else
            {
                copy = new ManaSceneNode(context, Matrix4x4.Identity);
            }
            copy.Name = node.Name;
            copy.IsEditorTransient = true;
            SetTransform(copy, node.LocalTransform);
            foreach (var light in node.Components.OfType<ManaLightSourceComponent>())
            {
                copy.Components.Add(new ManaLightSourceComponent(copy)
                {
                    Color = light.Color, Intensity = light.Intensity, Range = light.Range, LightType = light.LightType, Direction = light.Direction,
                    Enabled = light.Enabled, CastsShadows = light.CastsShadows, FlickerAmount = light.FlickerAmount, FlickerSpeed = light.FlickerSpeed
                });
            }
            foreach (var child in node.Children)
            {
                copy.AddChild(Copy(context, child));
            }
            return copy;
        }

        // Position, rotation and scale rather than a fixed matrix, so the game can still move the part from there.
        private static void SetTransform(ManaSceneNode node, Matrix4x4 transform)
        {
            Matrix4x4.Decompose(transform, out var scale, out var rotation, out var position);
            node.Position = position;
            node.Rotation = rotation.ToEuler();
            node.Scale = scale;
            node.UpdateTransform();
        }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            Foundry.View.ModelLibrary.Loader = (asset, context) =>
            {
                if (asset.EndsWith(ManaPrefab.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    return PrefabModel(asset, context);
                }
                var prototype = GridMapPrototype.Load(asset, context);
                if (!prototype.HasGeometry)
                {
                    throw new InvalidOperationException($"Foundry asset '{asset}': {prototype.Error}");
                }
                return new Foundry.View.FoundryModel(prototype.BoundsMin, prototype.BoundsMax, prototype.Instantiate);
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
