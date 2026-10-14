using System.Runtime.InteropServices;
using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed class ManaWebGpuMesh : IManaGpuMesh
    {
        public const ulong VertexStride = (ulong)(sizeof(float) * (3 + 3 + 4 + 2));


        private readonly WebGPU _api;
        private WebGpuBuffer* _vertexBuffer;
        private WebGpuBuffer* _indexBuffer;

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public uint IndexCount { get; }
        public uint VertexCount { get; }

        private ManaWebGpuMesh(WebGPU api, WebGpuBuffer* vertexBuffer, WebGpuBuffer* indexBuffer, uint indexCount, uint vertexCount)
        {
            _api = api;
            _vertexBuffer = vertexBuffer;
            _indexBuffer = indexBuffer;
            IndexCount = indexCount;
            VertexCount = vertexCount;
        }

        public static ManaWebGpuMesh CreateFromMeshData(WebGPU api, Device* device, Queue* queue, ManaMeshCpuData meshData, string meshName)
        {
            var vertices = BuildVertices(meshData);

            var vertexBufferDescriptor = new BufferDescriptor
            {
                // CopySrc costs nothing functionally and lets mana_debug_readback_buffer (see
                // mana_webgpu.c) inspect this buffer's real GPU-side bytes during future debugging.
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst | BufferUsage.CopySrc,
                Size = (ulong)vertices.Length * VertexStride
            };
            var vertexBuffer = api.DeviceCreateBuffer(device, &vertexBufferDescriptor);
            if (vertexBuffer == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU vertex buffer for mesh '{meshName}'.");
            }

            fixed (ManaWebGpuVertex* vertexPointer = vertices)
            {
                api.QueueWriteBuffer(queue, vertexBuffer, 0, vertexPointer, (nuint)vertexBufferDescriptor.Size);
            }

            var indexBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Index | BufferUsage.CopyDst,
                Size = (ulong)(meshData.Indexes.Length * sizeof(uint))
            };
            var indexBuffer = api.DeviceCreateBuffer(device, &indexBufferDescriptor);
            if (indexBuffer == null)
            {
                api.BufferRelease(vertexBuffer);
                throw new InvalidOperationException($"Failed to create WebGPU index buffer for mesh '{meshName}'.");
            }

            fixed (uint* indexPointer = meshData.Indexes)
            {
                api.QueueWriteBuffer(queue, indexBuffer, 0, indexPointer, (nuint)indexBufferDescriptor.Size);
            }

            return new ManaWebGpuMesh(api, vertexBuffer, indexBuffer, (uint)meshData.Indexes.Length, (uint)meshData.Positions.Length);
        }

        // Rewrites this mesh's existing buffers instead of building new ones. Only valid when the
        // topology is unchanged (same vertex and index counts) -- the caller checks that. CPU-skinned
        // meshes hit this every frame, and rebuilding them the normal way would mean recreating a
        // uniform buffer, seven bind groups and a texture binding per character per frame.
        public bool TryUpdateGeometryInPlace(Queue* queue, ManaMeshCpuData meshData)
        {
            if (_vertexBuffer == null || _indexBuffer == null ||
                meshData.Positions.Length != VertexCount ||
                meshData.Indexes.Length != IndexCount)
            {
                return false;
            }

            var vertices = BuildVertices(meshData);
            fixed (ManaWebGpuVertex* vertexPointer = vertices)
            {
                _api.QueueWriteBuffer(queue, _vertexBuffer, 0, vertexPointer, (nuint)((ulong)vertices.Length * VertexStride));
            }

            fixed (uint* indexPointer = meshData.Indexes)
            {
                _api.QueueWriteBuffer(queue, _indexBuffer, 0, indexPointer, (nuint)(meshData.Indexes.Length * sizeof(uint)));
            }

            return true;
        }

        private static ManaWebGpuVertex[] BuildVertices(ManaMeshCpuData meshData)
        {
            var vertices = new ManaWebGpuVertex[meshData.Positions.Length];
            // Read as plain floats: iOS's AOT code loads a Vector3/Vector2 element with a 16-byte load, which ran off
            // the end of an array that ended at a page boundary and crashed (EXC_BAD_ACCESS here, after a level change).
            var positions = MemoryMarshal.Cast<Vector3, float>(meshData.Positions.AsSpan());
            var hasNormals = meshData.Normals.Length == meshData.Positions.Length;
            var normals = MemoryMarshal.Cast<Vector3, float>(meshData.Normals.AsSpan());
            var hasUvs = meshData.TextureCoordinates0.Length == meshData.Positions.Length;
            var uvs = MemoryMarshal.Cast<Vector2, float>(meshData.TextureCoordinates0.AsSpan());
            var hasColors = meshData.Colors.Length == meshData.Positions.Length;

            for (var i = 0; i < vertices.Length; i++)
            {
                var color = hasColors ? meshData.Colors[i] : Vector4.One;
                var uv = hasUvs ? new Vector2(uvs[i * 2], uvs[i * 2 + 1]) : Vector2.Zero;
                var normal = hasNormals ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]) : Vector3.UnitY;
                var position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                vertices[i] = new ManaWebGpuVertex(position, normal, color, uv);
            }

            return vertices;
        }

        public void Draw(RenderPassEncoder* renderPass)
        {
            if (_vertexBuffer == null || _indexBuffer == null || IndexCount == 0)
            {
                return;
            }

            _api.RenderPassEncoderSetVertexBuffer(renderPass, 0, _vertexBuffer, 0, ulong.MaxValue);
            _api.RenderPassEncoderSetIndexBuffer(renderPass, _indexBuffer, IndexFormat.Uint32, 0, ulong.MaxValue);
            _api.RenderPassEncoderDrawIndexed(renderPass, IndexCount, 1, 0, 0, 0);
        }

        // Draws this mesh's own vertex buffer against a CALLER-supplied index buffer instead of the
        // mesh's own -- used for the wireframe overlay pass, which draws the same vertices through a
        // separate line-topology edge list (see ManaWebGpuSceneMesh.GetOrCreateWireframeIndexBuffer)
        // rather than the mesh's normal triangle-list index buffer.
        public void DrawWithIndexBuffer(RenderPassEncoder* renderPass, WebGpuBuffer* indexBuffer, uint indexCount)
        {
            if (_vertexBuffer == null || indexBuffer == null || indexCount == 0)
            {
                return;
            }

            _api.RenderPassEncoderSetVertexBuffer(renderPass, 0, _vertexBuffer, 0, ulong.MaxValue);
            _api.RenderPassEncoderSetIndexBuffer(renderPass, indexBuffer, IndexFormat.Uint32, 0, ulong.MaxValue);
            _api.RenderPassEncoderDrawIndexed(renderPass, indexCount, 1, 0, 0, 0);
        }

        public void Dispose()
        {
            if (_indexBuffer != null)
            {
                _api.BufferRelease(_indexBuffer);
                _indexBuffer = null;
            }

            if (_vertexBuffer != null)
            {
                _api.BufferRelease(_vertexBuffer);
                _vertexBuffer = null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct ManaWebGpuVertex
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector4 Color;
            public readonly Vector2 TexCoord0;

            public ManaWebGpuVertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 texCoord0)
            {
                Position = position;
                Normal = normal;
                Color = color;
                TexCoord0 = texCoord0;
            }
        }
    }
}
