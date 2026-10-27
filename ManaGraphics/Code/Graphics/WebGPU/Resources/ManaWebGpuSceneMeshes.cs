using System.Text;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The scene meshes resident on the GPU, keyed by instance. A replaced or removed mesh is disposed a fixed number of
    /// frames later, once nothing in flight can still draw it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSceneMeshes : ManaWebGpuDeviceObject
    {
        private readonly ManaWebGpuMeshTextureCache _textures;
        private readonly ManaWebGpuEnvironment _environment;
        private readonly List<ManaWebGpuSceneMesh> _sceneMeshes = new();
        private readonly Dictionary<string, ManaWebGpuSceneMesh> _sceneMeshMap = new();
        private readonly Queue<DeferredSceneMeshDisposal> _deferredSceneMeshDisposals = new();
        private ulong _frameSerial;

        private readonly record struct DeferredSceneMeshDisposal(ulong DisposeAfterFrame, ManaWebGpuSceneMesh SceneMesh);

        public ManaWebGpuSceneMeshes(ManaWebGpuDevice gpu, ManaWebGpuMeshTextureCache textures, ManaWebGpuEnvironment environment)
            : base(gpu)
        {
            _textures = textures;
            _environment = environment;
        }

        public IReadOnlyList<ManaWebGpuSceneMesh> Meshes => _sceneMeshes;

        /// <summary>Start of a frame: advances the retirement clock and disposes what has aged out.</summary>
        public void BeginFrame()
        {
            _frameSerial++;
            DrainDeferred();
        }

        private ManaWebGpuSceneMesh Create(ManaRenderMeshInstance instance, ManaRenderScene scene, ManaWebGpuSceneBindings bindings)
        {
            return ManaWebGpuSceneMesh.Create(Gpu, _textures, bindings, _environment.EnsureMap(scene), instance);
        }

        private const ulong SceneMeshDeferredDisposeFrameDelay = 6;

        // Ceilings on mesh creation per frame; what does not fit waits a frame, a replaced mesh drawing its old geometry
        // meanwhile, and at least one always uploads. Time: a small mesh costs well under a tenth of a millisecond, so
        // a count cap left scenes of many small meshes appearing a handful a frame.
        private const double MaxSceneMeshUploadMillisecondsPerFrame = 4.0;

        // Indexes, for the size-proportional cost of a burst of large terrain patches.
        private const ulong MaxSceneMeshIndexUploadsPerFrame = 4_000_000;

        /// <summary>Retires every resident mesh: after the in-flight frames by default, now when <paramref name="immediate"/>.</summary>
        public void ReleaseAll(bool immediate = false)
        {
            foreach (var sceneMesh in _sceneMeshes)
            {
                if (immediate)
                {
                    sceneMesh.Dispose();
                }
                else
                {
                    Defer(sceneMesh);
                }
            }

            _sceneMeshes.Clear();
            _sceneMeshMap.Clear();

            if (immediate)
            {
                DrainDeferred(force: true);
            }
        }

        private void Defer(ManaWebGpuSceneMesh sceneMesh)
        {
            _deferredSceneMeshDisposals.Enqueue(new DeferredSceneMeshDisposal(
                _frameSerial + SceneMeshDeferredDisposeFrameDelay,
                sceneMesh));
        }

        /// <summary>Disposes retired meshes whose frames have finished; all of them when <paramref name="force"/>.</summary>
        public void DrainDeferred(bool force = false)
        {
            while (_deferredSceneMeshDisposals.Count > 0)
            {
                var next = _deferredSceneMeshDisposals.Peek();
                if (!force && next.DisposeAfterFrame > _frameSerial)
                {
                    break;
                }

                _deferredSceneMeshDisposals.Dequeue();
                next.SceneMesh.Dispose();
            }
        }

        /// <summary>Replaces the resident set with every renderable mesh of <paramref name="renderScene"/> at once.</summary>
        public void Upload(ManaRenderScene renderScene, ManaWebGpuSceneBindings bindings)
        {
            ReleaseAll();

            var renderableMeshes = GetRenderableMeshes(renderScene);
            for (var i = 0; i < renderableMeshes.Count; i++)
            {
                var instance = renderableMeshes[i];
                var sceneMesh = Create(instance, renderScene, bindings);
                _sceneMeshes.Add(sceneMesh);
                _sceneMeshMap[GetSceneMeshKey(instance, i)] = sceneMesh;

                ManaConsole.WriteDiagnostic($"WebGPU scene mesh uploaded: {instance.WorldTransform.Translation.ToString()}: {instance.MeshData.Positions.Length} vertices, {instance.MeshData.Indexes.Length} indexes from render packet '{instance.Name}'.");
            }

            if (renderableMeshes.Count > 4)
            {
                var instance = renderableMeshes[3];
                var meshData = instance.MeshData;
                ManaConsole.WriteDiagnostic($"WebGPU scene mesh uploaded: {instance.WorldTransform.Translation.ToString()}: {meshData.Positions.Length} vertices, {meshData.Indexes.Length} indexes from render packet '{instance.Name}'.");
            }                        

            var totalIndexes = _sceneMeshes.Sum(mesh => mesh.Mesh.IndexCount);
            ManaConsole.WriteDiagnostic($"WebGPU scene upload complete: meshes={_sceneMeshes.Count}, indexes={totalIndexes}.");
        }

        /// <summary>Matches the resident set to this frame's renderables within the upload budget.</summary>
        public void Reconcile(IReadOnlyList<ManaRenderMeshInstance> renderableMeshes, ManaRenderScene scene, ManaWebGpuSceneBindings bindings)
        {
            var nextSceneMeshes = new List<ManaWebGpuSceneMesh>(renderableMeshes.Count);
            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            var uploadedCount = 0;
            var removedCount = 0;
            var deferredCount = 0;
            ulong uploadedIndexes = 0;
            ulong removedIndexes = 0;
            List<string>? changedMeshSummaries = null;
            var uploadClock = System.Diagnostics.Stopwatch.StartNew();

            for (var i = 0; i < renderableMeshes.Count; i++)
            {
                var instance = renderableMeshes[i];
                var key = GetSceneMeshKey(instance, i);
                activeKeys.Add(key);

                if (_sceneMeshMap.TryGetValue(key, out var existing))
                {
                    if (CanReuseSceneMesh(existing.Instance, instance))
                    {
                        existing.Instance = instance;
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    // Same mesh with new vertices (skinned characters, every frame): rewrite geometry, keep the bindings.
                    if (CanUpdateSceneMeshGeometryInPlace(existing.Instance, instance) &&
                        existing.Mesh.TryUpdateGeometryInPlace(Queue, instance.MeshData))
                    {
                        existing.Instance = instance;
                        existing.InvalidateDerivedGeometry();
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    // Over budget: keep drawing the old mesh; the swap retries next frame.
                    if (IsUploadBudgetSpent(uploadedCount, uploadedIndexes, uploadClock))
                    {
                        deferredCount++;
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    removedCount++;
                    removedIndexes += existing.Mesh.IndexCount;
                    Defer(existing);
                    _sceneMeshMap.Remove(key);
                }
                else if (IsUploadBudgetSpent(uploadedCount, uploadedIndexes, uploadClock))
                {
                    // Nothing to fall back on for a first-time upload, so this mesh simply isn't drawn
                    // until a later frame has room for it.
                    deferredCount++;
                    continue;
                }

                // Measured apart from the reconcile: uploads are what spike.
                ManaWebGpuSceneMesh created;
                using (ManaFrameProfiler.Measure("GPU: Mesh Buffer Upload"))
                {
                    created = Create(instance, scene, bindings);
                }

                _sceneMeshMap[key] = created;
                nextSceneMeshes.Add(created);
                uploadedCount++;
                uploadedIndexes += created.Mesh.IndexCount;
                changedMeshSummaries ??= new List<string>();
                //changedMeshSummaries.Add(FormatSceneMeshDelta(instance, key, existing?.Mesh.IndexCount, created.Mesh.IndexCount));
            }

            var staleKeys = _sceneMeshMap.Keys
                .Where(key => !activeKeys.Contains(key))
                .ToArray();
            foreach (var staleKey in staleKeys)
            {
                if (_sceneMeshMap.Remove(staleKey, out var staleMesh))
                {
                    removedCount++;
                    removedIndexes += staleMesh.Mesh.IndexCount;
                    Defer(staleMesh);
                    changedMeshSummaries ??= new List<string>();
                    //changedMeshSummaries.Add(FormatSceneMeshDelta(staleMesh.Instance, staleKey, staleMesh.Mesh.IndexCount, null));
                }
            }

            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(nextSceneMeshes);

            if (uploadedCount > 0 || removedCount > 0 || deferredCount > 0)
            {
                var residentIndexes = _sceneMeshes.Sum(mesh => mesh.Mesh.IndexCount);
                ManaConsole.WriteDiagnostic(
                    $"WebGPU scene mesh delta: uploaded={uploadedCount} (indexes={uploadedIndexes}), " +
                    $"removed={removedCount} (indexes={removedIndexes}), deferred={deferredCount}, " +
                    $"residentMeshes={_sceneMeshes.Count}, residentIndexes={residentIndexes}, ms={uploadClock.Elapsed.TotalMilliseconds:0.00}.");

                if (changedMeshSummaries is { Count: > 0 })
                {
                    ManaConsole.WriteDiagnostic($"WebGPU changed meshes: {string.Join("; ", changedMeshSummaries)}.");
                }
            }
        }

        // Either ceiling closes the budget. Never true with nothing uploaded yet, so a mesh bigger than the
        // whole per-frame allowance still gets created rather than being deferred forever.
        private static bool IsUploadBudgetSpent(int uploadedCount, ulong uploadedIndexes, System.Diagnostics.Stopwatch uploadClock)
        {
            if (uploadedCount == 0)
            {
                return false;
            }

            return uploadClock.Elapsed.TotalMilliseconds >= MaxSceneMeshUploadMillisecondsPerFrame
                || uploadedIndexes >= MaxSceneMeshIndexUploadsPerFrame;
        }

        private static string FormatSceneMeshDelta(ManaRenderMeshInstance instance, string key, uint? oldIndexes, uint? newIndexes)
        {
            var builder = new StringBuilder();
            builder.Append('\'');
            builder.Append(string.IsNullOrWhiteSpace(instance.Name) ? "<unnamed>" : instance.Name);
            builder.Append("' key=");
            builder.Append(key);

            if (oldIndexes.HasValue)
            {
                builder.Append(" oldIndexes=");
                builder.Append(oldIndexes.Value);
            }

            if (newIndexes.HasValue)
            {
                builder.Append(" newIndexes=");
                builder.Append(newIndexes.Value);
            }

            return builder.ToString();
        }

        /// <summary>Whether two meshes wear the same maps; textures are baked into the bind group, so any change rebuilds.</summary>
        private static bool SameMaterialMaps(ManaMeshCpuData current, ManaMeshCpuData incoming)
        {
            return string.Equals(current.AlbedoTexturePath, incoming.AlbedoTexturePath, StringComparison.Ordinal)
                && string.Equals(current.NormalTexturePath, incoming.NormalTexturePath, StringComparison.Ordinal)
                && string.Equals(current.EmissionTexturePath, incoming.EmissionTexturePath, StringComparison.Ordinal)
                && string.Equals(current.MetallicTexturePath, incoming.MetallicTexturePath, StringComparison.Ordinal)
                && string.Equals(current.RoughnessTexturePath, incoming.RoughnessTexturePath, StringComparison.Ordinal)
                && string.Equals(current.OcclusionTexturePath, incoming.OcclusionTexturePath, StringComparison.Ordinal)
                && string.Equals(current.OpacityTexturePath, incoming.OpacityTexturePath, StringComparison.Ordinal)
                && current.NearestFiltering == incoming.NearestFiltering;
        }

        private static bool CanReuseSceneMesh(ManaRenderMeshInstance current, ManaRenderMeshInstance incoming)
        {
            if (!string.Equals(current.Name, incoming.Name, StringComparison.Ordinal) ||
                !string.Equals(current.MeshKey, incoming.MeshKey, StringComparison.Ordinal) ||
                current.MeshData.Indexes.Length != incoming.MeshData.Indexes.Length ||
                current.MeshData.Positions.Length != incoming.MeshData.Positions.Length ||
                !SameMaterialMaps(current.MeshData, incoming.MeshData))
            {
                return false;
            }

            if (current.MeshVersion != 0 || incoming.MeshVersion != 0)
            {
                return current.MeshVersion == incoming.MeshVersion;
            }

            return ReferenceEquals(current.MeshData, incoming.MeshData);
        }

        // Same bindings, new vertex bytes. Requires the same MeshKey: another mesh would need other textures.
        private static bool CanUpdateSceneMeshGeometryInPlace(ManaRenderMeshInstance current, ManaRenderMeshInstance incoming)
        {
            return string.Equals(current.MeshKey, incoming.MeshKey, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(current.MeshKey) &&
                string.Equals(current.Name, incoming.Name, StringComparison.Ordinal) &&
                SameMaterialMaps(current.MeshData, incoming.MeshData) &&
                current.MeshData.Positions.Length == incoming.MeshData.Positions.Length &&
                current.MeshData.Indexes.Length == incoming.MeshData.Indexes.Length;
        }

        private static string GetSceneMeshKey(ManaRenderMeshInstance instance, int index)
        {
            if (!string.IsNullOrWhiteSpace(instance.InstanceKey))
            {
                return instance.InstanceKey;
            }

            if (!string.IsNullOrWhiteSpace(instance.MeshKey))
            {
                return $"{instance.MeshKey}:{index}";
            }

            return $"{instance.Name}:{index}";
        }

        private readonly ManaStaticMeshBatcher _batcher = new();

        public List<ManaRenderMeshInstance> GetRenderableMeshes(ManaRenderScene renderScene)
        {
            return _batcher.Build(renderScene.Meshes);
        }

        public override void Dispose()
        {
            _batcher.Clear();
            ReleaseAll(immediate: true);
        }
    }
}
