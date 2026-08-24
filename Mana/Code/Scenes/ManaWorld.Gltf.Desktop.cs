using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using System.Diagnostics;
using System.Numerics;

namespace Mana
{
    // Assimp-backed glTF/OBJ/FBX/etc. import -- desktop only, no browser-wasm build of Assimp
    // exists (see WebPort-Plan.md M4). ManaWorld.Gltf.Browser.cs is the pure-managed
    // (SharpGLTF, glTF/GLB only) counterpart used on that target instead.
    public partial class ManaWorld
    {
        public unsafe ManaSceneNode LoadFromGltf(
            string sceneFile,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaGltfLoader loader = new ManaGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFile(sceneFile);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog() { Name = $"Loading {sceneFile}", Duration = stopwatch.Elapsed });

            Scenes.AddRange(loader.Scenes);
            Meshes.AddRange(loader.Meshes);
            Materials.AddRange(loader.Materials);

            return rootSceneNode;
        }

        public unsafe ManaSceneNode LoadFromGltfMemory(
            ReadOnlyMemory<byte> sceneBytes,
            string virtualFileName,
            Func<string, byte[]?>? externalAssetResolver = null,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaGltfLoader loader = new ManaGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
            var rootSceneNode = loader.LoadFileFromMemory(sceneBytes.Span, virtualFileName, externalAssetResolver);

            stopwatch.Stop();
            manaPerformanceLogs.Add(new ManaPerformanceLog()
            {
                Name = $"Loading {virtualFileName} (memory)",
                Duration = stopwatch.Elapsed
            });

            Scenes.AddRange(loader.Scenes);
            Meshes.AddRange(loader.Meshes);
            Materials.AddRange(loader.Materials);

            return rootSceneNode;
        }
    }
}
