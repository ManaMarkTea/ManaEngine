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
        public unsafe ManaSceneNode LoadFromGltf(
            string sceneFile,
            Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? skeletalActorFactory = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            ManaSharpGltfLoader loader = new ManaSharpGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
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

            ManaSharpGltfLoader loader = new ManaSharpGltfLoader(Context) { SkeletalActorFactory = skeletalActorFactory };
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
