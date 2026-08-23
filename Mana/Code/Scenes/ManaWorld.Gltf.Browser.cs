using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using System.Diagnostics;
using System.Numerics;

namespace Mana
{
    // Pure-managed (SharpGLTF) glTF/GLB import for the browser-wasm target, where no native
    // Assimp build exists (see WebPort-Plan.md M4). ManaWorld.Gltf.Desktop.cs is the
    // Assimp-backed counterpart, which also covers OBJ/FBX/DAE/STL/PLY -- those stay
    // desktop-only for now.
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
