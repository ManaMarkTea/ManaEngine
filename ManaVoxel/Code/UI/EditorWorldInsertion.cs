using Mana;
using Mana.Code.Materials;
using Mana.Code.Nodes;
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
        private readonly ManaSceneNode? _placedNode;
        private readonly ManaScene? _placedScene;

        private EditorWorldInsertion(
            ManaWorld world,
            int sceneIndex,
            int meshIndex,
            int materialIndex,
            ManaScene[] scenes,
            ManaMesh[] meshes,
            ManaMaterial[] materials,
            ManaSceneNode? placedNode,
            ManaScene? placedScene)
        {
            _world = world;
            _sceneIndex = sceneIndex;
            _meshIndex = meshIndex;
            _materialIndex = materialIndex;
            _scenes = scenes;
            _meshes = meshes;
            _materials = materials;
            _placedNode = placedNode;
            _placedScene = placedScene;
        }

        public int SceneCount => _scenes.Length;
        public int MeshCount => _meshes.Length;
        public int MaterialCount => _materials.Length;
        public bool IsEmpty => SceneCount == 0 && MeshCount == 0 && MaterialCount == 0 && _placedNode == null;

        public static EditorWorldInsertion Capture(
            ManaWorld world,
            int sceneStartIndex,
            int meshStartIndex,
            int materialStartIndex,
            ManaSceneNode? placedNode = null)
        {
            ArgumentNullException.ThrowIfNull(world);

            lock (world.SceneLock)
            {
                var sceneIndex = Math.Clamp(sceneStartIndex, 0, world.Scenes.Count);
                var meshIndex = Math.Clamp(meshStartIndex, 0, world.Meshes.Count);
                var materialIndex = Math.Clamp(materialStartIndex, 0, world.Materials.Count);

                // A model placed into an existing scene adds no scene of its own, so undo has to know which
                // scene the node went into to take it back out.
                var placedScene = placedNode == null
                    ? null
                    : world.Scenes.FirstOrDefault(scene => scene.Children.Contains(placedNode));

                return new EditorWorldInsertion(
                    world,
                    sceneIndex,
                    meshIndex,
                    materialIndex,
                    world.Scenes.Skip(sceneIndex).ToArray(),
                    world.Meshes.Skip(meshIndex).ToArray(),
                    world.Materials.Skip(materialIndex).ToArray(),
                    placedNode,
                    placedScene);
            }
        }

        public void Remove()
        {
            lock (_world.SceneLock)
            {
                RemoveByIdentity(_world.Scenes, _scenes);
                RemoveByIdentity(_world.Meshes, _meshes);
                RemoveByIdentity(_world.Materials, _materials);

                if (_placedNode != null)
                {
                    _placedScene?.Children.Remove(_placedNode);
                }
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

                if (_placedNode != null && _placedScene != null && !_placedScene.Children.Contains(_placedNode))
                {
                    _placedScene.Children.Add(_placedNode);
                }
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
