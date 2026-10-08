using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void UpdateDemoMeshBounds(ManaMeshCpuData cpuData)
        {
            if (cpuData.Positions.Length == 0)
            {
                _demoMeshCenter = Vector3.Zero;
                _demoMeshScale = 1.0f;
                return;
            }

            var min = cpuData.Positions[0];
            var max = cpuData.Positions[0];
            foreach (var position in cpuData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            _demoMeshCenter = (min + max) * 0.5f;
            var extents = max - min;
            var maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            _demoMeshScale = maxExtent <= 0.0001f ? 1.0f : 2.0f / maxExtent;
        }

        private void UploadSceneMeshes(ManaRenderScene renderScene)
        {
            ReleaseSceneMeshes();

            var renderableMeshes = GetRenderableMeshes(renderScene);
            for (var i = 0; i < renderableMeshes.Count; i++)
            {
                var instance = renderableMeshes[i];
                var sceneMesh = CreateSceneMesh(instance);
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

        private ManaWebGpuSceneMesh CreateSceneMesh(ManaRenderMeshInstance instance)
        {
            var meshData = instance.MeshData;
            var mesh = ManaWebGpuMesh.CreateFromMeshData(_api, _device, _queue, meshData, instance.Name);

            var bufferDescriptor = new BufferDescriptor
            {
                // CopySrc costs nothing functionally and lets mana_debug_readback_buffer (see
                // mana_webgpu.c) inspect this buffer's real GPU-side bytes during future debugging.
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc,
                Size = (ulong)sizeof(ManaWebGpuObjectUniform)
            };
            var uniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
            if (uniformBuffer == null)
            {
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU uniform buffer for mesh '{instance.Name}'.");
            }

            var textureResource = _meshTextures.AcquireMeshTexture(meshData);
            var environmentReflectionMap = GetOrCreateEnvironmentReflectionMap();

            var bindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = uniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuObjectUniform)
            };

            var samplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 1,
                Sampler = meshData.NearestFiltering ? _nearestSampler : _demoSampler
            };

            var textureBindGroupEntry = new BindGroupEntry
            {
                Binding = 2,
                TextureView = textureResource.TextureView
            };

            var environmentMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 3,
                TextureView = environmentReflectionMap.EnvironmentMapResource.TextureView
            };

            var irradianceMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 4,
                TextureView = environmentReflectionMap.IrradianceMapResource.TextureView
            };

            var prefilteredReflectionMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 5,
                TextureView = environmentReflectionMap.PrefilteredReflectionMapResource.TextureView
            };

            var brdfLutBindGroupEntry = new BindGroupEntry
            {
                Binding = 6,
                TextureView = environmentReflectionMap.BrdfLutResource.TextureView
            };

            var clampSamplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 7,
                Sampler = _clampSampler
            };

            // The rest of the maps, in binding order. Each is a real texture or the neutral 1x1 for its
            // kind, because a bind group has to fill every binding its layout declares.
            var materialMaps = _meshTextures.AcquireMaterialMaps(meshData);

            var bindGroupEntries = new BindGroupEntry[ObjectBindingCount];
            bindGroupEntries[0] = bindGroupEntry;
            bindGroupEntries[1] = samplerBindGroupEntry;
            bindGroupEntries[2] = textureBindGroupEntry;
            bindGroupEntries[3] = environmentMapBindGroupEntry;
            bindGroupEntries[4] = irradianceMapBindGroupEntry;
            bindGroupEntries[5] = prefilteredReflectionMapBindGroupEntry;
            bindGroupEntries[6] = brdfLutBindGroupEntry;
            bindGroupEntries[7] = clampSamplerBindGroupEntry;

            for (var map = 0; map < MaterialMapCount; map++)
            {
                bindGroupEntries[FirstMaterialMapBinding + map] = new BindGroupEntry
                {
                    Binding = (uint)(FirstMaterialMapBinding + map),
                    TextureView = materialMaps[map].TextureView
                };
            }

            BindGroup* bindGroup = null;
            fixed (BindGroupEntry* bindGroupEntriesPointer = bindGroupEntries)
            {
                var bindGroupDescriptor = new BindGroupDescriptor
                {
                    Layout = _objectBindGroupLayout,
                    EntryCount = ObjectBindingCount,
                    Entries = bindGroupEntriesPointer
                };
                bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
            }

            if (bindGroup == null)
            {
                _meshTextures.ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU bind group for mesh '{instance.Name}'.");
            }

            // Scene capture keeps its own, larger, unsplit uniform layout (see
            // ManaWebGpuSceneCaptureUniform in ManaWebGpuContext.Uniforms.cs), so it needs its own
            // buffer size distinct from the (now much smaller) per-object bufferDescriptor above.
            var sceneCaptureBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
            };

            var sceneCaptureUniformBuffers = new nint[6];
            var sceneCaptureBindGroups = new nint[6];
            for (var face = 0; face < 6; face++)
            {
                var sceneCaptureUniformBuffer = _api.DeviceCreateBuffer(_device, &sceneCaptureBufferDescriptor);
                if (sceneCaptureUniformBuffer == null)
                {
                    ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    _api.BindGroupRelease(bindGroup);
                    _meshTextures.ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture uniform buffer for mesh '{instance.Name}'.");
                }

                sceneCaptureUniformBuffers[face] = (nint)sceneCaptureUniformBuffer;
                var sceneCaptureBufferBindGroupEntry = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = sceneCaptureUniformBuffer,
                    Offset = 0,
                    Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                };

                var sceneCaptureBindGroupEntries = new BindGroupEntry[3];
                sceneCaptureBindGroupEntries[0] = sceneCaptureBufferBindGroupEntry;
                sceneCaptureBindGroupEntries[1] = samplerBindGroupEntry;
                sceneCaptureBindGroupEntries[2] = textureBindGroupEntry;

                BindGroup* sceneCaptureBindGroup = null;
                fixed (BindGroupEntry* sceneCaptureBindGroupEntriesPointer = sceneCaptureBindGroupEntries)
                {
                    var sceneCaptureBindGroupDescriptor = new BindGroupDescriptor
                    {
                        Layout = _sceneCaptureBindGroupLayout,
                        EntryCount = 3,
                        Entries = sceneCaptureBindGroupEntriesPointer
                    };
                    sceneCaptureBindGroup = _api.DeviceCreateBindGroup(_device, &sceneCaptureBindGroupDescriptor);
                }

                if (sceneCaptureBindGroup == null)
                {
                    ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    _api.BindGroupRelease(bindGroup);
                    _meshTextures.ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture bind group for mesh '{instance.Name}'.");
                }

                sceneCaptureBindGroups[face] = (nint)sceneCaptureBindGroup;
            }

            // Group 1: generic per-material custom uniform buffer, built for every mesh regardless of
            // shader (unused shaders simply never declare @group(1) in their .wgsl source).
            var customUniformLayout = GetCustomUniformLayout(instance.ShaderName);
            var customUniformData = new byte[customUniformLayout.BufferSize];
            BuildCustomMaterialUniformData(instance, customUniformLayout, customUniformData);

            var customBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)customUniformData.Length
            };
            var customUniformBuffer = _api.DeviceCreateBuffer(_device, &customBufferDescriptor);
            if (customUniformBuffer == null)
            {
                ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                _api.BindGroupRelease(bindGroup);
                _meshTextures.ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU custom uniform buffer for mesh '{instance.Name}'.");
            }

            var customBindGroupEntries = new BindGroupEntry[1];
            customBindGroupEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = customUniformBuffer,
                Offset = 0,
                Size = (ulong)customUniformData.Length
            };

            BindGroup* customBindGroup = null;
            fixed (BindGroupEntry* customBindGroupEntriesPointer = customBindGroupEntries)
            {
                var customBindGroupDescriptor = new BindGroupDescriptor
                {
                    Layout = _customUniformBindGroupLayout,
                    EntryCount = 1,
                    Entries = customBindGroupEntriesPointer
                };
                customBindGroup = _api.DeviceCreateBindGroup(_device, &customBindGroupDescriptor);
            }

            if (customBindGroup == null)
            {
                ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                _api.BindGroupRelease(bindGroup);
                _meshTextures.ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                _api.BufferRelease(customUniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU custom uniform bind group for mesh '{instance.Name}'.");
            }

            fixed (byte* customUniformDataPointer = customUniformData)
            {
                _api.QueueWriteBuffer(_queue, customUniformBuffer, 0, customUniformDataPointer, (nuint)customUniformData.Length);
            }

            var sceneMesh = new ManaWebGpuSceneMesh(
                _api,
                instance,
                mesh,
                uniformBuffer,
                textureResource,
                materialMaps,
                _meshTextures.ReleaseMeshTexture,
                bindGroup,
                sceneCaptureUniformBuffers,
                sceneCaptureBindGroups,
                customUniformBuffer,
                customBindGroup,
                customUniformLayout,
                customUniformData);
            return sceneMesh;
        }

        private void ReleaseSceneCaptureResources(IReadOnlyList<nint> uniformBuffers, IReadOnlyList<nint> bindGroups)
        {
            foreach (var bindGroup in bindGroups)
            {
                if (bindGroup != 0)
                {
                    _api.BindGroupRelease((BindGroup*)bindGroup);
                }
            }

            foreach (var uniformBuffer in uniformBuffers)
            {
                if (uniformBuffer != 0)
                {
                    _api.BufferRelease((WebGpuBuffer*)uniformBuffer);
                }
            }
        }

        private const ulong SceneMeshDeferredDisposeFrameDelay = 6;

        // Two per-frame ceilings on fresh scene-mesh creation (geometry buffers, uniform buffer, bind group,
        // texture binding), because that cost has two independent components and either one alone can blow a
        // frame. Whatever doesn't fit is retried next frame: a mesh being replaced keeps drawing its old
        // geometry meanwhile, a brand-new one appears a frame or two late, and at least one always uploads so
        // a single mesh over budget can't stall forever.
        //
        // Count, for the per-mesh fixed cost -- measured at roughly 4ms each regardless of size, so the first
        // unsuspended frame creating a whole planet's worth of patches at once cost ~880ms even though their
        // combined geometry was only 1.6M indexes.
        private const int MaxSceneMeshCreationsPerFrame = 8;

        // Indexes, for the size-proportional part -- a handful of terrain patches at up to ~1M vertices each
        // finishing together would pass the count cap while still moving far too much data in one frame.
        private const ulong MaxSceneMeshIndexUploadsPerFrame = 4_000_000;

        private void ReleaseSceneMeshes(bool immediate = false)
        {
            foreach (var sceneMesh in _sceneMeshes)
            {
                if (immediate)
                {
                    sceneMesh.Dispose();
                }
                else
                {
                    DeferSceneMeshDispose(sceneMesh);
                }
            }

            _sceneMeshes.Clear();
            _sceneMeshMap.Clear();

            if (immediate)
            {
                DrainDeferredSceneMeshDisposals(force: true);
            }
        }

        private void DeferSceneMeshDispose(ManaWebGpuSceneMesh sceneMesh)
        {
            _deferredSceneMeshDisposals.Enqueue(new DeferredSceneMeshDisposal(
                _frameSerial + SceneMeshDeferredDisposeFrameDelay,
                sceneMesh));
        }

        private void DrainDeferredSceneMeshDisposals(bool force = false)
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

        private void RefreshRenderScene()
        {
            var renderScene = _renderSceneProvider?.Invoke();
            if (renderScene == null)
            {
                return;
            }

            IReadOnlyList<ManaRenderMeshInstance> renderableMeshes;
            using (ManaFrameProfiler.Measure("GPU: Gather Renderables"))
            {
                renderableMeshes = GetRenderableMeshes(renderScene);
            }

            _activeRenderScene = renderScene;
            using (ManaFrameProfiler.Measure("GPU: Reconcile Scene Meshes"))
            {
                ReconcileSceneMeshes(renderableMeshes);
            }
        }

        private void ReconcileSceneMeshes(IReadOnlyList<ManaRenderMeshInstance> renderableMeshes)
        {
            var nextSceneMeshes = new List<ManaWebGpuSceneMesh>(renderableMeshes.Count);
            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            var uploadedCount = 0;
            var removedCount = 0;
            var deferredCount = 0;
            ulong uploadedIndexes = 0;
            ulong removedIndexes = 0;
            List<string>? changedMeshSummaries = null;

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

                    // Same mesh, same topology, new vertex data -- CPU-skinned characters produce this
                    // every frame. Rewriting the two geometry buffers keeps the (far more expensive)
                    // uniform buffers, bind groups and texture binding alive.
                    if (CanUpdateSceneMeshGeometryInPlace(existing.Instance, instance) &&
                        existing.Mesh.TryUpdateGeometryInPlace(_queue, instance.MeshData))
                    {
                        existing.Instance = instance;
                        existing.InvalidateDerivedGeometry();
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    // Out of upload budget: hold the existing scene mesh, Instance and all, so it keeps
                    // drawing last frame's geometry rather than vanishing. Its key stays in activeKeys,
                    // so the stale sweep below leaves it alone and the swap retries next frame.
                    if (IsUploadBudgetSpent(uploadedCount, uploadedIndexes))
                    {
                        deferredCount++;
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    removedCount++;
                    removedIndexes += existing.Mesh.IndexCount;
                    DeferSceneMeshDispose(existing);
                    _sceneMeshMap.Remove(key);
                }
                else if (IsUploadBudgetSpent(uploadedCount, uploadedIndexes))
                {
                    // Nothing to fall back on for a first-time upload, so this mesh simply isn't drawn
                    // until a later frame has room for it.
                    deferredCount++;
                    continue;
                }

                // Scoped separately from the reconcile pass around it: walking the scene is cheap and
                // steady, while buffer creation and upload is the part that spikes with a burst of
                // finished terrain, and only one of those two is worth a budget.
                ManaWebGpuSceneMesh created;
                using (ManaFrameProfiler.Measure("GPU: Mesh Buffer Upload"))
                {
                    created = CreateSceneMesh(instance);
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
                    DeferSceneMeshDispose(staleMesh);
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
                    $"residentMeshes={_sceneMeshes.Count}, residentIndexes={residentIndexes}.");

                if (changedMeshSummaries is { Count: > 0 })
                {
                    ManaConsole.WriteDiagnostic($"WebGPU changed meshes: {string.Join("; ", changedMeshSummaries)}.");
                }
            }
        }

        // Either ceiling closes the budget. Never true with nothing uploaded yet, so a mesh bigger than the
        // whole per-frame allowance still gets created rather than being deferred forever.
        private static bool IsUploadBudgetSpent(int uploadedCount, ulong uploadedIndexes)
        {
            if (uploadedCount == 0)
            {
                return false;
            }

            return uploadedCount >= MaxSceneMeshCreationsPerFrame
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

        /// <summary>
        /// Whether two meshes wear the same maps. Every channel counts: the textures are baked into the
        /// bind group, so a mesh that keeps its GPU resources keeps whatever it was built with -- which is
        /// why assigning a map to a live material has to force a rebuild rather than merely a redraw.
        /// </summary>
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

        // Narrower than CanReuseSceneMesh: same GPU resources are still valid, but the vertex/index
        // bytes need rewriting. Deliberately requires the same MeshKey -- a different source mesh that
        // happens to share a vertex count would also need a different texture and bind group.
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

        private static List<ManaRenderMeshInstance> GetRenderableMeshes(ManaRenderScene renderScene)
        {
            return renderScene.Meshes
                .Where(instance => instance.MeshData.Positions.Length > 0 && instance.MeshData.Indexes.Length > 0)
                .ToList();
        }

        private static Matrix4x4 CreateNormalizedWorldTransform(ManaMeshCpuData meshData)
        {
            if (meshData.Positions.Length == 0)
            {
                return Matrix4x4.Identity;
            }

            var min = meshData.Positions[0];
            var max = meshData.Positions[0];
            foreach (var position in meshData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var center = (min + max) * 0.5f;
            var extents = max - min;
            var maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            var scale = maxExtent <= 0.0001f ? 1.0f : 2.0f / maxExtent;
            return Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale);
        }

        // internal, not private: the overlay passes under Overlays/ take one of these per draw call.
        internal sealed class ManaWebGpuSceneMesh : IDisposable
        {
            private readonly WebGPU _api;
            private readonly Action<ManaWebGpuMeshTexture> _releaseTexture;

            public ManaWebGpuSceneMesh(
                WebGPU api,
                ManaRenderMeshInstance instance,
                ManaWebGpuMesh mesh,
                WebGpuBuffer* uniformBuffer,
                ManaWebGpuMeshTexture texture,
                ManaWebGpuMeshTexture[] materialMaps,
                Action<ManaWebGpuMeshTexture> releaseTexture,
                BindGroup* bindGroup,
                nint[] sceneCaptureUniformBuffers,
                nint[] sceneCaptureBindGroups,
                WebGpuBuffer* customUniformBuffer = null,
                BindGroup* customBindGroup = null,
                ManaWebGpuCustomUniformSchema? customUniformLayout = null,
                byte[]? customUniformData = null)
            {
                _api = api;
                _releaseTexture = releaseTexture;
                Instance = instance;
                Mesh = mesh;
                UniformBuffer = uniformBuffer;
                Texture = texture;
                MaterialMaps = materialMaps;
                ObjectBindGroup = bindGroup;
                SceneCaptureUniformBuffers = sceneCaptureUniformBuffers;
                SceneCaptureBindGroups = sceneCaptureBindGroups;
                CustomUniformBuffer = customUniformBuffer;
                CustomBindGroup = customBindGroup;
                CustomUniformLayout = customUniformLayout ?? ManaWebGpuCustomUniformSchema.Empty;
                CustomUniformData = customUniformData ?? new byte[CustomUniformLayout.BufferSize];
            }

            public ManaRenderMeshInstance Instance { get; set; }
            public ManaWebGpuMesh Mesh { get; }
            public WebGpuBuffer* UniformBuffer { get; }
            public ManaWebGpuMeshTexture Texture { get; }

            /// <summary>Normal, emission, metallic, roughness, occlusion and opacity, in binding order.</summary>
            public ManaWebGpuMeshTexture[] MaterialMaps { get; }
            public BindGroup* ObjectBindGroup { get; }
            public nint[] SceneCaptureUniformBuffers { get; private set; }
            public nint[] SceneCaptureBindGroups { get; private set; }
            public WebGpuBuffer* CustomUniformBuffer { get; }
            public BindGroup* CustomBindGroup { get; }
            public ManaWebGpuCustomUniformSchema CustomUniformLayout { get; }
            public byte[] CustomUniformData { get; }

            // Dirty-check caches: the object/custom uniform buffers are only rewritten when the
            // freshly-built payload differs from what's already resident on the GPU, instead of every
            // frame regardless. null on first use, so the first real draw always uploads once.
            private ManaWebGpuObjectUniform? _lastUploadedObjectUniform;
            private byte[]? _lastUploadedCustomUniformData;

            // Lazily built the first time the wireframe overlay is actually enabled (see
            // ManaWebGpuContext.Demo.cs's DrawWireframeOverlay), not eagerly for every mesh -- this is
            // a rarely-toggled debug view, so the ~2000 resident meshes in a typical scene shouldn't
            // all pay edge-extraction cost unconditionally. Cached for this object's whole lifetime:
            // a ManaWebGpuSceneMesh is only ever kept across frames when the index data is identical
            // (CanReuseSceneMesh requires a matching MeshVersion or the same MeshData reference;
            // CanUpdateSceneMeshGeometryInPlace requires a matching index count on the same source
            // mesh), so a real topology change always builds a brand new ManaWebGpuSceneMesh and this
            // cache needs no extra invalidation bookkeeping.
            private WebGpuBuffer* _wireframeIndexBuffer;
            private uint _wireframeIndexCount;

            // Same lazy/lifetime reasoning as the wireframe buffer above, except this one bakes the
            // actual positions and normals, so an in-place geometry rewrite does invalidate it.
            private ManaWebGpuMesh? _normalLinesMesh;

            // Called when ReconcileSceneMeshes rewrites this mesh's vertex buffer in place rather than
            // rebuilding the whole scene mesh (skinned meshes, every frame). The wireframe index buffer
            // survives -- it is derived from topology alone, which an in-place update never changes.
            public void InvalidateDerivedGeometry()
            {
                _normalLinesMesh?.Dispose();
                _normalLinesMesh = null;
            }

            // Builds a separate LineList mesh whose vertices alternate base/tip -- the tip positions
            // don't exist in the source mesh, so unlike the wireframe this can't be done with an index
            // buffer over the original vertices.
            //
            // Both vertices of a line are stored at the surface position; uv0.x is 0 on the base and 1 on
            // the tip, and normal_lines.wgsl extrudes the tip by normal * uv0.x * length. Keeping length
            // out of the buffer is what lets it be a live setting instead of a rebuild.
            public ManaWebGpuMesh? GetOrCreateNormalLinesMesh(WebGPU api, Device* device, Queue* queue)
            {
                if (_normalLinesMesh != null)
                {
                    return _normalLinesMesh;
                }

                var meshData = Instance.MeshData;
                var positions = meshData.Positions;
                var normals = meshData.Normals;
                if (positions.Length == 0 || normals.Length != positions.Length)
                {
                    return null;
                }

                var linePositions = new Vector3[positions.Length * 2];
                var lineNormals = new Vector3[positions.Length * 2];
                var lineColors = new Vector4[positions.Length * 2];
                var lineTexCoords = new Vector2[positions.Length * 2];
                var lineIndexes = new uint[positions.Length * 2];
                for (var i = 0; i < positions.Length; i++)
                {
                    var normal = normals[i];
                    var baseIndex = i * 2;
                    linePositions[baseIndex] = positions[i];
                    linePositions[baseIndex + 1] = positions[i];
                    lineNormals[baseIndex] = normal;
                    lineNormals[baseIndex + 1] = normal;
                    lineTexCoords[baseIndex] = Vector2.Zero;
                    lineTexCoords[baseIndex + 1] = Vector2.UnitX;

                    // Tip carries the direction as colour, base stays dark, so each line reads as a
                    // gradient pointing away from the surface.
                    lineColors[baseIndex] = new Vector4(0.05f, 0.05f, 0.05f, 1.0f);
                    lineColors[baseIndex + 1] = new Vector4((normal * 0.5f) + new Vector3(0.5f), 1.0f);
                    lineIndexes[baseIndex] = (uint)baseIndex;
                    lineIndexes[baseIndex + 1] = (uint)(baseIndex + 1);
                }

                var lineData = new ManaMeshCpuData
                {
                    Positions = linePositions,
                    Normals = lineNormals,
                    Colors = lineColors,
                    TextureCoordinates0 = lineTexCoords,
                    TextureCoordinates1 = Array.Empty<Vector2>(),
                    Indexes = lineIndexes
                };

                _normalLinesMesh = ManaWebGpuMesh.CreateFromMeshData(api, device, queue, lineData, "NormalLines");
                return _normalLinesMesh;
            }

            // Pointer types can't be used as a generic type argument (e.g. a (WebGpuBuffer*, uint)
            // tuple), hence out-parameters instead of a tuple return here.
            public bool TryGetOrCreateWireframeIndexBuffer(WebGPU api, Device* device, Queue* queue, out WebGpuBuffer* buffer, out uint indexCount)
            {
                if (_wireframeIndexBuffer != null)
                {
                    buffer = _wireframeIndexBuffer;
                    indexCount = _wireframeIndexCount;
                    return true;
                }

                buffer = null;
                indexCount = 0;

                var triangleIndexes = Instance.MeshData.Indexes;
                var edges = new HashSet<(uint A, uint B)>();
                for (var i = 0; i + 2 < triangleIndexes.Length; i += 3)
                {
                    var a = triangleIndexes[i];
                    var b = triangleIndexes[i + 1];
                    var c = triangleIndexes[i + 2];
                    AddEdge(edges, a, b);
                    AddEdge(edges, b, c);
                    AddEdge(edges, c, a);
                }

                if (edges.Count == 0)
                {
                    return false;
                }

                var lineIndexes = new uint[edges.Count * 2];
                var write = 0;
                foreach (var (a, b) in edges)
                {
                    lineIndexes[write++] = a;
                    lineIndexes[write++] = b;
                }

                var bufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.Index | BufferUsage.CopyDst,
                    Size = (ulong)lineIndexes.Length * sizeof(uint)
                };
                var createdBuffer = api.DeviceCreateBuffer(device, &bufferDescriptor);
                if (createdBuffer == null)
                {
                    return false;
                }

                fixed (uint* indexPointer = lineIndexes)
                {
                    api.QueueWriteBuffer(queue, createdBuffer, 0, indexPointer, (nuint)bufferDescriptor.Size);
                }

                _wireframeIndexBuffer = createdBuffer;
                _wireframeIndexCount = (uint)lineIndexes.Length;
                buffer = _wireframeIndexBuffer;
                indexCount = _wireframeIndexCount;
                return true;
            }

            private static void AddEdge(HashSet<(uint A, uint B)> edges, uint a, uint b)
            {
                edges.Add(a < b ? (a, b) : (b, a));
            }

            public bool TryUpdateObjectUniform(WebGPU api, Queue* queue, in ManaWebGpuObjectUniform next)
            {
                if (_lastUploadedObjectUniform is { } last)
                {
                    var lastBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<ManaWebGpuObjectUniform>(in last));
                    var nextBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<ManaWebGpuObjectUniform>(in next));
                    if (lastBytes.SequenceEqual(nextBytes))
                    {
                        return false;
                    }
                }

                var value = next;
                api.QueueWriteBuffer(queue, UniformBuffer, 0, &value, (nuint)sizeof(ManaWebGpuObjectUniform));
                _lastUploadedObjectUniform = next;
                return true;
            }

            public bool TryUpdateCustomUniform(WebGPU api, Queue* queue, byte[] next)
            {
                if (_lastUploadedCustomUniformData != null && _lastUploadedCustomUniformData.AsSpan().SequenceEqual(next))
                {
                    return false;
                }

                fixed (byte* nextPointer = next)
                {
                    api.QueueWriteBuffer(queue, CustomUniformBuffer, 0, nextPointer, (nuint)next.Length);
                }
                _lastUploadedCustomUniformData = (byte[])next.Clone();
                return true;
            }

            public WebGpuBuffer* GetSceneCaptureUniformBuffer(int face)
            {
                return (WebGpuBuffer*)SceneCaptureUniformBuffers[Math.Clamp(face, 0, SceneCaptureUniformBuffers.Length - 1)];
            }

            public BindGroup* GetSceneCaptureBindGroup(int face)
            {
                return (BindGroup*)SceneCaptureBindGroups[Math.Clamp(face, 0, SceneCaptureBindGroups.Length - 1)];
            }

            public void Dispose()
            {
                Mesh.Dispose();

                if (_wireframeIndexBuffer != null)
                {
                    _api.BufferRelease(_wireframeIndexBuffer);
                    _wireframeIndexBuffer = null;
                }

                _normalLinesMesh?.Dispose();
                _normalLinesMesh = null;

                foreach (var sceneCaptureBindGroup in SceneCaptureBindGroups)
                {
                    if (sceneCaptureBindGroup != 0)
                    {
                        _api.BindGroupRelease((BindGroup*)sceneCaptureBindGroup);
                    }
                }
                SceneCaptureBindGroups = Array.Empty<nint>();

                foreach (var sceneCaptureUniformBuffer in SceneCaptureUniformBuffers)
                {
                    if (sceneCaptureUniformBuffer != 0)
                    {
                        _api.BufferRelease((WebGpuBuffer*)sceneCaptureUniformBuffer);
                    }
                }
                SceneCaptureUniformBuffers = Array.Empty<nint>();

                if (ObjectBindGroup != null)
                {
                    _api.BindGroupRelease(ObjectBindGroup);
                }
                if (CustomBindGroup != null)
                {
                    _api.BindGroupRelease(CustomBindGroup);
                }
                _releaseTexture(Texture);

                // Ref-counted and shared, so this is a decrement rather than a free for anything another
                // mesh is still using -- including the neutral 1x1s, which nearly every mesh holds.
                foreach (var map in MaterialMaps)
                {
                    _releaseTexture(map);
                }
                if (UniformBuffer != null)
                {
                    _api.BufferRelease(UniformBuffer);
                }
                if (CustomUniformBuffer != null)
                {
                    _api.BufferRelease(CustomUniformBuffer);
                }
            }
        }

        private static ManaRenderMeshInstance SelectPrimaryMesh(ManaRenderScene renderScene)
        {
            var startupVoxels = renderScene.Meshes.FirstOrDefault(mesh =>
                mesh.Name.Contains("Startup Voxels", StringComparison.OrdinalIgnoreCase) ||
                mesh.Name.Contains("Voxel", StringComparison.OrdinalIgnoreCase));

            if (startupVoxels != null)
            {
                return startupVoxels;
            }

            return renderScene.Meshes
                .OrderByDescending(mesh => mesh.MeshData.Indexes.Length)
                .First();
        }

    }
}
