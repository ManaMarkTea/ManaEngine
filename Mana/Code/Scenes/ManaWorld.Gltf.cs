using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using System.Diagnostics;
using System.Numerics;

namespace Mana
{
    // Pure-managed (SharpGLTF) glTF/GLB import, used on every platform. Originally the
    // browser-wasm-only counterpart to an Assimp-backed desktop path (see WebPort-Plan.md M4);
    // that path also read OBJ/FBX/DAE/STL/PLY, but nothing in this codebase's actual content
    // uses those formats, and carrying a native Assimp dependency for that on desktop only,
    // with browser using a completely different loader for the same asset type, wasn't worth
    // it once confirmed. Assimp is no longer referenced anywhere.
    public partial class ManaWorld
    {
        /// <param name="sceneFile">Path to the glTF or GLB model to load.</param>
        /// <param name="skeletalActorFactory">Optional factory for skeletal actor nodes; null uses the default actor type.</param>
        /// <param name="into">
        /// The scene the model should land in. Left null, the loader's own one-model wrapper scene is added to
        /// the world instead -- which is right for a character or a thumbnail, and wrong for anything the user
        /// places, since every import then shows up as a scene of its own.
        /// </param>
        public unsafe ManaSceneNode LoadFromGltf(
            string sceneFile,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null,
            ManaScene? into = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaSharpGltfLoader loader = new ManaSharpGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFile(sceneFile);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog() { Name = $"Loading {sceneFile}", Duration = stopwatch.Elapsed });

            Adopt(loader, into);

            return rootSceneNode;
        }

        public unsafe ManaSceneNode LoadFromGltfMemory(
            ReadOnlyMemory<byte> sceneBytes,
            string virtualFileName,
            Func<string, byte[]?>? externalAssetResolver = null,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null,
            ManaScene? into = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaSharpGltfLoader loader = new ManaSharpGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFileFromMemory(sceneBytes.Span, virtualFileName, externalAssetResolver);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog()
            {
                Name = $"Loading {virtualFileName} (memory)",
                Duration = stopwatch.Elapsed
            });

            Adopt(loader, into);

            return rootSceneNode;
        }

        private void Adopt(ManaSharpGltfLoader loader, ManaScene? into)
        {
            if (into == null)
            {
                // Under the scene lock: levels load on a background thread while the render thread walks
                // Scenes, and an unguarded AddRange there is "collection was modified" on a big enough level.
                lock (SceneLock)
                {
                    Scenes.AddRange(loader.Scenes);
                }
            }
            else
            {
                // The loader wraps what it read in a scene of its own. The caller wanted it in this one, so the
                // nodes move across and the wrapper is simply never added to the world.
                lock (SceneLock)
                {
                    foreach (var wrapper in loader.Scenes)
                    {
                        into.Children.AddRange(wrapper.Children);
                        wrapper.Children.Clear();
                    }
                }
            }

            Meshes.AddRange(loader.Meshes);
            Materials.AddRange(loader.Materials);
        }
    }
}
