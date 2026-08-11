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
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
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

            for (var i = 0; i < vertices.Length; i++)
            {
                var color = meshData.Colors.Length == meshData.Positions.Length
                    ? meshData.Colors[i]
                    : Vector4.One;
                var uv = meshData.TextureCoordinates0.Length == meshData.Positions.Length
                    ? meshData.TextureCoordinates0[i]
                    : Vector2.Zero;
                var normal = meshData.Normals.Length == meshData.Positions.Length
                    ? meshData.Normals[i]
                    : Vector3.UnitY;
                vertices[i] = new ManaWebGpuVertex(meshData.Positions[i], normal, color, uv);
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
