using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

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
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuTransformUniform)
            };
            var uniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
            if (uniformBuffer == null)
            {
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU uniform buffer for mesh '{instance.Name}'.");
            }

            var textureResource = AcquireMeshTexture(meshData);
            var environmentReflectionMap = GetOrCreateEnvironmentReflectionMap();

            var bindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = uniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuTransformUniform)
            };

            var samplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 1,
                Sampler = _demoSampler
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

            var bindGroupEntries = new BindGroupEntry[8];
            bindGroupEntries[0] = bindGroupEntry;
            bindGroupEntries[1] = samplerBindGroupEntry;
            bindGroupEntries[2] = textureBindGroupEntry;
            bindGroupEntries[3] = environmentMapBindGroupEntry;
            bindGroupEntries[4] = irradianceMapBindGroupEntry;
            bindGroupEntries[5] = prefilteredReflectionMapBindGroupEntry;
            bindGroupEntries[6] = brdfLutBindGroupEntry;
            bindGroupEntries[7] = clampSamplerBindGroupEntry;

            BindGroup* bindGroup = null;
            fixed (BindGroupEntry* bindGroupEntriesPointer = bindGroupEntries)
            {
                var bindGroupDescriptor = new BindGroupDescriptor
                {
                    Layout = _demoBindGroupLayout,
                    EntryCount = 8,
                    Entries = bindGroupEntriesPointer
                };
                bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
            }

            if (bindGroup == null)
            {
                ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU bind group for mesh '{instance.Name}'.");
            }

            var sceneCaptureUniformBuffers = new nint[6];
            var sceneCaptureBindGroups = new nint[6];
            for (var face = 0; face < 6; face++)
            {
                var sceneCaptureUniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
                if (sceneCaptureUniformBuffer == null)
                {
                    ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    _api.BindGroupRelease(bindGroup);
                    ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture uniform buffer for mesh '{instance.Name}'.");
                }

                sceneCaptureUniformBuffers[face] = (nint)sceneCaptureUniformBuffer;
                var sceneCaptureBufferBindGroupEntry = bindGroupEntry;
                sceneCaptureBufferBindGroupEntry.Buffer = sceneCaptureUniformBuffer;

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
                    ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture bind group for mesh '{instance.Name}'.");
                }

                sceneCaptureBindGroups[face] = (nint)sceneCaptureBindGroup;
            }

            // Group 1: generic per-material custom uniform buffer, built for every mesh regardless of
            // shader (unused shaders simply never declare @group(1) in their .wgsl source).
            var customBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuCustomMaterialUniform)
            };
            var customUniformBuffer = _api.DeviceCreateBuffer(_device, &customBufferDescriptor);
            if (customUniformBuffer == null)
            {
                ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                _api.BindGroupRelease(bindGroup);
                ReleaseMeshTexture(textureResource);
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
                Size = (ulong)sizeof(ManaWebGpuCustomMaterialUniform)
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
                ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                _api.BufferRelease(customUniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU custom uniform bind group for mesh '{instance.Name}'.");
            }

            var customUniform = BuildCustomMaterialUniform(instance);
            _api.QueueWriteBuffer(_queue, customUniformBuffer, 0, &customUniform, (nuint)sizeof(ManaWebGpuCustomMaterialUniform));

            var sceneMesh = new ManaWebGpuSceneMesh(_api, instance, mesh, uniformBuffer, textureResource, ReleaseMeshTexture, bindGroup, sceneCaptureUniformBuffers, sceneCaptureBindGroups, customUniformBuffer, customBindGroup);
            UpdateDemoUniforms(instance, uniformBuffer);
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

        private void ReleaseSceneMeshes()
        {
            foreach (var sceneMesh in _sceneMeshes)
            {
                sceneMesh.Dispose();
            }

            _sceneMeshes.Clear();
            _sceneMeshMap.Clear();
        }

        private void RefreshRenderScene()
        {
            var renderScene = _renderSceneProvider?.Invoke();
            if (renderScene == null)
            {
                return;
            }

            var renderableMeshes = GetRenderableMeshes(renderScene);
            _activeRenderScene = renderScene;
            ReconcileSceneMeshes(renderableMeshes);
        }

        private void ReconcileSceneMeshes(IReadOnlyList<ManaRenderMeshInstance> renderableMeshes)
        {
            var nextSceneMeshes = new List<ManaWebGpuSceneMesh>(renderableMeshes.Count);
            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            var uploadedCount = 0;
            var removedCount = 0;
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

                    existing.Dispose();
                    _sceneMeshMap.Remove(key);
                    removedCount++;
                    removedIndexes += existing.Mesh.IndexCount;
                }

                var created = CreateSceneMesh(instance);
                _sceneMeshMap[key] = created;
                nextSceneMeshes.Add(created);
                uploadedCount++;
                uploadedIndexes += created.Mesh.IndexCount;
                changedMeshSummaries ??= new List<string>();
                changedMeshSummaries.Add(FormatSceneMeshDelta(instance, key, existing?.Mesh.IndexCount, created.Mesh.IndexCount));
            }

            var staleKeys = _sceneMeshMap.Keys
                .Where(key => !activeKeys.Contains(key))
                .ToArray();
            foreach (var staleKey in staleKeys)
            {
                if (_sceneMeshMap.Remove(staleKey, out var staleMesh))
                {
                    staleMesh.Dispose();
                    removedCount++;
                    removedIndexes += staleMesh.Mesh.IndexCount;
                    changedMeshSummaries ??= new List<string>();
                    changedMeshSummaries.Add(FormatSceneMeshDelta(staleMesh.Instance, staleKey, staleMesh.Mesh.IndexCount, null));
                }
            }

            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(nextSceneMeshes);

            if (uploadedCount > 0 || removedCount > 0)
            {
                var residentIndexes = _sceneMeshes.Sum(mesh => mesh.Mesh.IndexCount);
                ManaConsole.WriteDiagnostic(
                    $"WebGPU scene mesh delta: uploaded={uploadedCount} (indexes={uploadedIndexes}), " +
                    $"removed={removedCount} (indexes={removedIndexes}), residentMeshes={_sceneMeshes.Count}, " +
                    $"residentIndexes={residentIndexes}.");

                if (changedMeshSummaries is { Count: > 0 })
                {
                    ManaConsole.WriteDiagnostic($"WebGPU changed meshes: {string.Join("; ", changedMeshSummaries)}.");
                }
            }
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

        private static bool CanReuseSceneMesh(ManaRenderMeshInstance current, ManaRenderMeshInstance incoming)
        {
            if (!string.Equals(current.Name, incoming.Name, StringComparison.Ordinal) ||
                !string.Equals(current.MeshKey, incoming.MeshKey, StringComparison.Ordinal) ||
                current.MeshData.Indexes.Length != incoming.MeshData.Indexes.Length ||
                current.MeshData.Positions.Length != incoming.MeshData.Positions.Length ||
                !string.Equals(current.MeshData.AlbedoTexturePath, incoming.MeshData.AlbedoTexturePath, StringComparison.Ordinal))
            {
                return false;
            }

            if (current.MeshVersion != 0 || incoming.MeshVersion != 0)
            {
                return current.MeshVersion == incoming.MeshVersion;
            }

            return ReferenceEquals(current.MeshData, incoming.MeshData);
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

        private sealed class ManaWebGpuSceneMesh : IDisposable
        {
            private readonly WebGPU _api;
            private readonly Action<ManaWebGpuMeshTexture> _releaseTexture;

            public ManaWebGpuSceneMesh(
                WebGPU api,
                ManaRenderMeshInstance instance,
                ManaWebGpuMesh mesh,
                WebGpuBuffer* uniformBuffer,
                ManaWebGpuMeshTexture texture,
                Action<ManaWebGpuMeshTexture> releaseTexture,
                BindGroup* bindGroup,
                nint[] sceneCaptureUniformBuffers,
                nint[] sceneCaptureBindGroups,
                WebGpuBuffer* customUniformBuffer = null,
                BindGroup* customBindGroup = null)
            {
                _api = api;
                _releaseTexture = releaseTexture;
                Instance = instance;
                Mesh = mesh;
                UniformBuffer = uniformBuffer;
                Texture = texture;
                BindGroup = bindGroup;
                SceneCaptureUniformBuffers = sceneCaptureUniformBuffers;
                SceneCaptureBindGroups = sceneCaptureBindGroups;
                CustomUniformBuffer = customUniformBuffer;
                CustomBindGroup = customBindGroup;
            }

            public ManaRenderMeshInstance Instance { get; set; }
            public ManaWebGpuMesh Mesh { get; }
            public WebGpuBuffer* UniformBuffer { get; }
            public ManaWebGpuMeshTexture Texture { get; }
            public BindGroup* BindGroup { get; }
            public nint[] SceneCaptureUniformBuffers { get; private set; }
            public nint[] SceneCaptureBindGroups { get; private set; }
            public WebGpuBuffer* CustomUniformBuffer { get; }
            public BindGroup* CustomBindGroup { get; }

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

                if (BindGroup != null)
                {
                    _api.BindGroupRelease(BindGroup);
                }
                if (CustomBindGroup != null)
                {
                    _api.BindGroupRelease(CustomBindGroup);
                }
                _releaseTexture(Texture);
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
