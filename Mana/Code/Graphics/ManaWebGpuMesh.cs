using System.Runtime.InteropServices;
using Mana.Code;
using Mana.Code.Shapes;
using Silk.NET.WebGPU;

namespace Mana.Code.Graphics
{
    public unsafe sealed class ManaWebGpuMesh : IDisposable
    {
        public const ulong VertexStride = (ulong)(sizeof(float) * (3 + 4 + 2));

        private readonly WebGPU _api;
        private Silk.NET.WebGPU.Buffer* _vertexBuffer;
        private Silk.NET.WebGPU.Buffer* _indexBuffer;

        public uint IndexCount { get; }

        private ManaWebGpuMesh(WebGPU api, Silk.NET.WebGPU.Buffer* vertexBuffer, Silk.NET.WebGPU.Buffer* indexBuffer, uint indexCount)
        {
            _api = api;
            _vertexBuffer = vertexBuffer;
            _indexBuffer = indexBuffer;
            IndexCount = indexCount;
        }

        public static ManaWebGpuMesh CreateFromGeometryContainer(WebGPU api, Device* device, Queue* queue, ManaDynamicGeometryContainer geometry)
        {
            var positions = geometry.Positions;
            var colors = geometry.Colors;
            var texCoords0 = geometry.TexCoords0;
            var indexes = geometry.Indexes.ToArray();
            var vertices = new ManaWebGpuVertex[positions.Count];

            for (var i = 0; i < vertices.Length; i++)
            {
                var uv = texCoords0.Count == positions.Count ? texCoords0[i] : Vector2.Zero;
                vertices[i] = new ManaWebGpuVertex(positions[i], colors[i], uv);
            }

            var vertexBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = (ulong)vertices.Length * VertexStride
            };
            var vertexBuffer = api.DeviceCreateBuffer(device, &vertexBufferDescriptor);
            if (vertexBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU vertex buffer.");
            }

            fixed (ManaWebGpuVertex* vertexPointer = vertices)
            {
                api.QueueWriteBuffer(queue, vertexBuffer, 0, vertexPointer, (nuint)vertexBufferDescriptor.Size);
            }

            var indexBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Index | BufferUsage.CopyDst,
                Size = (ulong)(indexes.Length * sizeof(uint))
            };
            var indexBuffer = api.DeviceCreateBuffer(device, &indexBufferDescriptor);
            if (indexBuffer == null)
            {
                api.BufferRelease(vertexBuffer);
                throw new InvalidOperationException("Failed to create WebGPU index buffer.");
            }

            fixed (uint* indexPointer = indexes)
            {
                api.QueueWriteBuffer(queue, indexBuffer, 0, indexPointer, (nuint)indexBufferDescriptor.Size);
            }

            return new ManaWebGpuMesh(api, vertexBuffer, indexBuffer, (uint)indexes.Length);
        }

        public static ManaWebGpuMesh CreateFromManaMesh(WebGPU api, Device* device, Queue* queue, ManaMesh mesh)
        {
            var cpuData = mesh.GetOrCreateCpuData();
            var vertices = new ManaWebGpuVertex[cpuData.Positions.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                var color = cpuData.Colors.Length == cpuData.Positions.Length
                    ? cpuData.Colors[i]
                    : Vector4.One;
                var uv = cpuData.TextureCoordinates0.Length == cpuData.Positions.Length
                    ? cpuData.TextureCoordinates0[i]
                    : Vector2.Zero;
                vertices[i] = new ManaWebGpuVertex(cpuData.Positions[i], color, uv);
            }

            var vertexBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = (ulong)vertices.Length * VertexStride
            };
            var vertexBuffer = api.DeviceCreateBuffer(device, &vertexBufferDescriptor);
            if (vertexBuffer == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU vertex buffer for mesh '{mesh.Name}'.");
            }

            fixed (ManaWebGpuVertex* vertexPointer = vertices)
            {
                api.QueueWriteBuffer(queue, vertexBuffer, 0, vertexPointer, (nuint)vertexBufferDescriptor.Size);
            }

            var indexBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Index | BufferUsage.CopyDst,
                Size = (ulong)(cpuData.Indexes.Length * sizeof(uint))
            };
            var indexBuffer = api.DeviceCreateBuffer(device, &indexBufferDescriptor);
            if (indexBuffer == null)
            {
                api.BufferRelease(vertexBuffer);
                throw new InvalidOperationException($"Failed to create WebGPU index buffer for mesh '{mesh.Name}'.");
            }

            fixed (uint* indexPointer = cpuData.Indexes)
            {
                api.QueueWriteBuffer(queue, indexBuffer, 0, indexPointer, (nuint)indexBufferDescriptor.Size);
            }

            return new ManaWebGpuMesh(api, vertexBuffer, indexBuffer, (uint)cpuData.Indexes.Length);
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
            public readonly Vector4 Color;
            public readonly Vector2 TexCoord0;

            public ManaWebGpuVertex(Vector3 position, Vector4 color, Vector2 texCoord0)
            {
                Position = position;
                Color = color;
                TexCoord0 = texCoord0;
            }
        }
    }
}