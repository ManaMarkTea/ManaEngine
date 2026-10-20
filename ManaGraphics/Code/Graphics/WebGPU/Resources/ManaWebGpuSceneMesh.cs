using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>One resident scene mesh: its geometry and the GPU bindings it draws with.</summary>
    internal sealed unsafe class ManaWebGpuSceneMesh : ManaWebGpuDeviceObject
    {
        private readonly Action<ManaWebGpuMeshTexture> _releaseTexture;

        public ManaWebGpuSceneMesh(
            ManaWebGpuDevice gpu,
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
            : base(gpu)
        {
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

        /// <summary>Uploads one mesh and builds its bindings; a failure releases whatever was made.</summary>
        public static ManaWebGpuSceneMesh Create(
            ManaWebGpuDevice gpu,
            ManaWebGpuMeshTextureCache meshTextures,
            ManaWebGpuSceneBindings bindings,
            ManaWebGpuEnvironmentReflectionMap environmentReflectionMap,
            ManaRenderMeshInstance instance)
        {
            var api = gpu.Api;
            var device = gpu.Device;
            var queue = gpu.Queue;
            var textures = meshTextures;
            var meshData = instance.MeshData;
            var mesh = ManaWebGpuMesh.CreateFromMeshData(gpu, meshData, instance.Name);

            WebGpuBuffer* uniformBuffer = null;
            WebGpuBuffer* customUniformBuffer = null;
            BindGroup* bindGroup = null;
            BindGroup* customBindGroup = null;
            ManaWebGpuMeshTexture? textureResource = null;
            ManaWebGpuMeshTexture[] materialMaps = [];
            var sceneCaptureUniformBuffers = new nint[6];
            var sceneCaptureBindGroups = new nint[6];
            var transferred = false;
            try
            {
                var bufferDescriptor = new BufferDescriptor
                {
                    // CopySrc costs nothing functionally and lets mana_debug_readback_buffer (see
                    // mana_webgpu.c) inspect this buffer's real GPU-side bytes during future debugging.
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc,
                    Size = (ulong)sizeof(ManaWebGpuObjectUniform)
                };
                uniformBuffer = api.DeviceCreateBuffer(device, &bufferDescriptor);
                if (uniformBuffer == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU uniform buffer for mesh '{instance.Name}'.");
                }

                textureResource = textures.AcquireMeshTexture(meshData);

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
                    Sampler = meshData.NearestFiltering ? bindings.NearestSampler : bindings.LinearSampler
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
                    Sampler = bindings.ClampSampler
                };

                // The rest of the maps, in binding order. Each is a real texture or the neutral 1x1 for its
                // kind, because a bind group has to fill every binding its layout declares.
                materialMaps = textures.AcquireMaterialMaps(meshData);

                var bindGroupEntries = new BindGroupEntry[ManaWebGpuSceneBindings.ObjectBindingCount];
                bindGroupEntries[0] = bindGroupEntry;
                bindGroupEntries[1] = samplerBindGroupEntry;
                bindGroupEntries[2] = textureBindGroupEntry;
                bindGroupEntries[3] = environmentMapBindGroupEntry;
                bindGroupEntries[4] = irradianceMapBindGroupEntry;
                bindGroupEntries[5] = prefilteredReflectionMapBindGroupEntry;
                bindGroupEntries[6] = brdfLutBindGroupEntry;
                bindGroupEntries[7] = clampSamplerBindGroupEntry;

                for (var map = 0; map < ManaWebGpuSceneBindings.MaterialMapCount; map++)
                {
                    bindGroupEntries[ManaWebGpuSceneBindings.FirstMaterialMapBinding + map] = new BindGroupEntry
                    {
                        Binding = (uint)(ManaWebGpuSceneBindings.FirstMaterialMapBinding + map),
                        TextureView = materialMaps[map].TextureView
                    };
                }

                fixed (BindGroupEntry* bindGroupEntriesPointer = bindGroupEntries)
                {
                    var bindGroupDescriptor = new BindGroupDescriptor
                    {
                        Layout = bindings.ObjectLayout,
                        EntryCount = ManaWebGpuSceneBindings.ObjectBindingCount,
                        Entries = bindGroupEntriesPointer
                    };
                    bindGroup = api.DeviceCreateBindGroup(device, &bindGroupDescriptor);
                }

                if (bindGroup == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU bind group for mesh '{instance.Name}'.");
                }

                // Capture uses its own, larger uniform layout (ManaWebGpuSceneCaptureUniform).
                var sceneCaptureBufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                    Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                };

                for (var face = 0; face < 6; face++)
                {
                    var sceneCaptureUniformBuffer = api.DeviceCreateBuffer(device, &sceneCaptureBufferDescriptor);
                    if (sceneCaptureUniformBuffer == null)
                    {
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
                            Layout = bindings.SceneCaptureGroupLayout,
                            EntryCount = 3,
                            Entries = sceneCaptureBindGroupEntriesPointer
                        };
                        sceneCaptureBindGroup = api.DeviceCreateBindGroup(device, &sceneCaptureBindGroupDescriptor);
                    }

                    if (sceneCaptureBindGroup == null)
                    {
                        throw new InvalidOperationException($"Failed to create WebGPU scene capture bind group for mesh '{instance.Name}'.");
                    }

                    sceneCaptureBindGroups[face] = (nint)sceneCaptureBindGroup;
                }

                // Group 1: generic per-material custom uniform buffer, built for every mesh regardless of
                // shader (unused shaders simply never declare @group(1) in their .wgsl source).
                var customUniformLayout = ManaWebGpuCustomUniforms.Layout(instance.ShaderName);
                var customUniformData = new byte[customUniformLayout.BufferSize];
                ManaWebGpuCustomUniforms.Fill(instance, customUniformLayout, customUniformData);

                var customBufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                    Size = (ulong)customUniformData.Length
                };
                customUniformBuffer = api.DeviceCreateBuffer(device, &customBufferDescriptor);
                if (customUniformBuffer == null)
                {
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

                fixed (BindGroupEntry* customBindGroupEntriesPointer = customBindGroupEntries)
                {
                    var customBindGroupDescriptor = new BindGroupDescriptor
                    {
                        Layout = bindings.CustomUniformGroupLayout,
                        EntryCount = 1,
                        Entries = customBindGroupEntriesPointer
                    };
                    customBindGroup = api.DeviceCreateBindGroup(device, &customBindGroupDescriptor);
                }

                if (customBindGroup == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU custom uniform bind group for mesh '{instance.Name}'.");
                }

                fixed (byte* customUniformDataPointer = customUniformData)
                {
                    api.QueueWriteBuffer(queue, customUniformBuffer, 0, customUniformDataPointer, (nuint)customUniformData.Length);
                }

                var sceneMesh = new ManaWebGpuSceneMesh(
                    gpu,
                    instance,
                    mesh,
                    uniformBuffer,
                    textureResource,
                    materialMaps,
                    textures.ReleaseMeshTexture,
                    bindGroup,
                    sceneCaptureUniformBuffers,
                    sceneCaptureBindGroups,
                    customUniformBuffer,
                    customBindGroup,
                    customUniformLayout,
                    customUniformData);
                transferred = true;
                return sceneMesh;
            }
            finally
            {
                if (!transferred)
                {
                    ReleaseCaptureResources(api, sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    if (customBindGroup != null) { api.BindGroupRelease(customBindGroup); }
                    if (bindGroup != null) { api.BindGroupRelease(bindGroup); }
                    if (customUniformBuffer != null) { api.BufferRelease(customUniformBuffer); }
                    if (uniformBuffer != null) { api.BufferRelease(uniformBuffer); }
                    foreach (var map in materialMaps)
                    {
                        textures.ReleaseMeshTexture(map);
                    }
                    if (textureResource != null)
                    {
                        textures.ReleaseMeshTexture(textureResource);
                    }
                    mesh.Dispose();
                }
            }
        }

        private static void ReleaseCaptureResources(WebGPU api, IReadOnlyList<nint> uniformBuffers, IReadOnlyList<nint> bindGroups)
        {
            foreach (var bindGroup in bindGroups)
            {
                if (bindGroup != 0)
                {
                    api.BindGroupRelease((BindGroup*)bindGroup);
                }
            }

            foreach (var uniformBuffer in uniformBuffers)
            {
                if (uniformBuffer != 0)
                {
                    api.BufferRelease((WebGpuBuffer*)uniformBuffer);
                }
            }
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

        // Last uploaded payloads: a buffer is rewritten only when its contents change.
        private ManaWebGpuObjectUniform? _lastUploadedObjectUniform;
        private byte[]? _lastUploadedCustomUniformData;

        // Built the first time the wireframe overlay is on. Topology never changes for the life of this object, so the
        // buffer never needs invalidating.
        private WebGpuBuffer* _wireframeIndexBuffer;
        private uint _wireframeIndexCount;

        // Same lazy/lifetime reasoning as the wireframe buffer above, except this one bakes the
        // actual positions and normals, so an in-place geometry rewrite does invalidate it.
        private ManaWebGpuMesh? _normalLinesMesh;

        // After an in-place vertex rewrite; the wireframe depends on topology alone and survives.
        public void InvalidateDerivedGeometry()
        {
            _normalLinesMesh?.Dispose();
            _normalLinesMesh = null;
        }

        // A LineList mesh with each vertex twice, base then tip (uv0.x 0 or 1); the shader extrudes the tip along the
        // normal, so the length stays a live setting.
        public ManaWebGpuMesh? GetOrCreateNormalLinesMesh()
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

            _normalLinesMesh = ManaWebGpuMesh.CreateFromMeshData(Gpu, lineData, "NormalLines");
            return _normalLinesMesh;
        }

        // Pointer types can't be used as a generic type argument (e.g. a (WebGpuBuffer*, uint)
        // tuple), hence out-parameters instead of a tuple return here.
        public bool TryGetOrCreateWireframeIndexBuffer(out WebGpuBuffer* buffer, out uint indexCount)
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
            var createdBuffer = Api.DeviceCreateBuffer(Device, &bufferDescriptor);
            if (createdBuffer == null)
            {
                return false;
            }

            fixed (uint* indexPointer = lineIndexes)
            {
                Api.QueueWriteBuffer(Queue, createdBuffer, 0, indexPointer, (nuint)bufferDescriptor.Size);
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

        public bool TryUpdateObjectUniform(in ManaWebGpuObjectUniform next)
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
            Api.QueueWriteBuffer(Queue, UniformBuffer, 0, &value, (nuint)sizeof(ManaWebGpuObjectUniform));
            _lastUploadedObjectUniform = next;
            return true;
        }

        public bool TryUpdateCustomUniform(byte[] next)
        {
            if (_lastUploadedCustomUniformData != null && _lastUploadedCustomUniformData.AsSpan().SequenceEqual(next))
            {
                return false;
            }

            fixed (byte* nextPointer = next)
            {
                Api.QueueWriteBuffer(Queue, CustomUniformBuffer, 0, nextPointer, (nuint)next.Length);
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

        public override void Dispose()
        {
            Mesh.Dispose();

            if (_wireframeIndexBuffer != null)
            {
                Api.BufferRelease(_wireframeIndexBuffer);
                _wireframeIndexBuffer = null;
            }

            _normalLinesMesh?.Dispose();
            _normalLinesMesh = null;

            foreach (var sceneCaptureBindGroup in SceneCaptureBindGroups)
            {
                if (sceneCaptureBindGroup != 0)
                {
                    Api.BindGroupRelease((BindGroup*)sceneCaptureBindGroup);
                }
            }
            SceneCaptureBindGroups = Array.Empty<nint>();

            foreach (var sceneCaptureUniformBuffer in SceneCaptureUniformBuffers)
            {
                if (sceneCaptureUniformBuffer != 0)
                {
                    Api.BufferRelease((WebGpuBuffer*)sceneCaptureUniformBuffer);
                }
            }
            SceneCaptureUniformBuffers = Array.Empty<nint>();

            if (ObjectBindGroup != null)
            {
                Api.BindGroupRelease(ObjectBindGroup);
            }
            if (CustomBindGroup != null)
            {
                Api.BindGroupRelease(CustomBindGroup);
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
                Api.BufferRelease(UniformBuffer);
            }
            if (CustomUniformBuffer != null)
            {
                Api.BufferRelease(CustomUniformBuffer);
            }
        }
    }
}
