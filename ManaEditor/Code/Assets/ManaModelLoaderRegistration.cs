using System.Runtime.CompilerServices;
using Mana;
using Mana.Code.Content;
using Mana.Code.Levels;
using Mana.Code.Models;
using Mana.Code.Nodes;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Assets
{
    /// <summary>
    /// Gives game code the editor's loaders: the mesh importer for the model library (<see cref="ManaModel.Loader"/>, which
    /// takes prefabs too) and prefab placing with scripts and colliders (<see cref="ManaPrefabs.Spawner"/>).
    /// </summary>
    internal static class ManaModelLoaderRegistration
    {
        [ModuleInitializer]
        internal static void Register()
        {
            // A game's own app may set its own afterwards.
            ManaModel.Loader ??= (asset, context, scene) =>
            {
                if (asset.EndsWith(ManaPrefab.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    return SpawnPrefab(context, scene, asset);
                }
                return ManaMeshAssetLoader.For(context.World).Load(asset, scene);
            };
            ManaPrefabs.Spawner ??= SpawnPrefab;
        }

        // Built as a level would place it: the same loader, so the instance has everything a placed one has.
        private static ManaSceneNode SpawnPrefab(ManaContext context, ManaScene scene, string assetPath)
        {
            var root = ManaContentLibrary.RootFor(context.World);
            var file = ManaContentMounts.FullPath(root, assetPath) ?? throw new FileNotFoundException($"No content folder for '{assetPath}'.");
            var entry = new ManaLevelNode
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Path.GetFileNameWithoutExtension(assetPath),
                Kind = ManaLevelSourceKind.Prefab,
                Prefab = new ManaPrefabReference { AssetId = ManaPrefab.ReadId(file), AssetPath = assetPath.Replace('\\', '/') }
            };
            return ManaPrefabRuntime.Instantiate(context, scene, entry);
        }
    }
}
