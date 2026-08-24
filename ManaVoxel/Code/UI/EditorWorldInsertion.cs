using Mana;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace ManaVoxel.Code.UI
{
    /// <summary>
    /// The world data appended by one editor import. Removing it preserves the loaded objects so redo can
    /// put the exact same scene, meshes, and materials back without loading the asset a second time.
    /// </summary>
    public sealed class EditorWorldInsertion
    {
        private readonly ManaWorld _world;
        private readonly int _sceneIndex;
        private readonly int _meshIndex;
        private readonly int _materialIndex;
        private readonly ManaScene[] _scenes;
        private readonly ManaMesh[] _meshes;
        private readonly ManaMaterial[] _materials;

        private EditorWorldInsertion(
            ManaWorld world,
            int sceneIndex,
            int meshIndex,
            int materialIndex,
            ManaScene[] scenes,
            ManaMesh[] meshes,
            ManaMaterial[] materials)
        {
            _world = world;
            _sceneIndex = sceneIndex;
            _meshIndex = meshIndex;
            _materialIndex = materialIndex;
            _scenes = scenes;
            _meshes = meshes;
            _materials = materials;
        }

        public int SceneCount => _scenes.Length;
        public int MeshCount => _meshes.Length;
        public int MaterialCount => _materials.Length;
        public bool IsEmpty => SceneCount == 0 && MeshCount == 0 && MaterialCount == 0;

        public static EditorWorldInsertion Capture(
            ManaWorld world,
            int sceneStartIndex,
            int meshStartIndex,
            int materialStartIndex)
        {
            ArgumentNullException.ThrowIfNull(world);

            lock (world.SceneLock)
            {
                var sceneIndex = Math.Clamp(sceneStartIndex, 0, world.Scenes.Count);
                var meshIndex = Math.Clamp(meshStartIndex, 0, world.Meshes.Count);
                var materialIndex = Math.Clamp(materialStartIndex, 0, world.Materials.Count);
                return new EditorWorldInsertion(
                    world,
                    sceneIndex,
                    meshIndex,
                    materialIndex,
                    world.Scenes.Skip(sceneIndex).ToArray(),
                    world.Meshes.Skip(meshIndex).ToArray(),
                    world.Materials.Skip(materialIndex).ToArray());
            }
        }

        public void Remove()
        {
            lock (_world.SceneLock)
            {
                RemoveByIdentity(_world.Scenes, _scenes);
                RemoveByIdentity(_world.Meshes, _meshes);
                RemoveByIdentity(_world.Materials, _materials);
            }
        }

        public void Restore()
        {
            lock (_world.SceneLock)
            {
                // Resources first, then the scenes that reference them.
                RestoreByIdentity(_world.Materials, _materialIndex, _materials);
                RestoreByIdentity(_world.Meshes, _meshIndex, _meshes);
                RestoreByIdentity(_world.Scenes, _sceneIndex, _scenes);
            }
        }

        private static void RemoveByIdentity<T>(List<T> target, IReadOnlyList<T> inserted)
            where T : class
        {
            target.RemoveAll(candidate => inserted.Any(item => ReferenceEquals(candidate, item)));
        }

        private static void RestoreByIdentity<T>(List<T> target, int originalIndex, IReadOnlyList<T> inserted)
            where T : class
        {
            var insertionIndex = Math.Clamp(originalIndex, 0, target.Count);
            foreach (var item in inserted)
            {
                var existingIndex = target.FindIndex(candidate => ReferenceEquals(candidate, item));
                if (existingIndex >= 0)
                {
                    insertionIndex = Math.Max(insertionIndex, existingIndex + 1);
                    continue;
                }

                target.Insert(Math.Min(insertionIndex, target.Count), item);
                insertionIndex++;
            }
        }
    }
}
