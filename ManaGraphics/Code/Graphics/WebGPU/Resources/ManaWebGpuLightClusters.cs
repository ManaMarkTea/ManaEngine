using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The placed lights and their clusters (<see cref="ManaLightClusters"/>) on the GPU: three read-only storage buffers
    /// in the frame group (bindings 3-5), filled once a frame from the main camera. Sized for the most a frame can hold,
    /// so the bind group never changes; a frame uploads only what it uses.
    /// </summary>
    internal sealed unsafe class ManaWebGpuLightClusters : ManaWebGpuDeviceObject
    {
        private readonly ManaLightClusters _clusters = new();

        public ManaWebGpuLightClusters(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                LightBuffer = Create((ulong)(ManaRenderScene.MaxLocalLights * 2 * sizeof(Vector4)));
                CellBuffer = Create((ulong)(ManaLightClusters.CellCount * 2 * sizeof(uint)));
                IndexBuffer = Create((ulong)(ManaLightClusters.MaxIndices * sizeof(uint)));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public WebGpuBuffer* LightBuffer { get; private set; }
        public WebGpuBuffer* CellBuffer { get; private set; }
        public WebGpuBuffer* IndexBuffer { get; private set; }

        public ulong LightBufferSize => (ulong)(ManaRenderScene.MaxLocalLights * 2 * sizeof(Vector4));
        public ulong CellBufferSize => (ulong)(ManaLightClusters.CellCount * 2 * sizeof(uint));
        public ulong IndexBufferSize => (ulong)(ManaLightClusters.MaxIndices * sizeof(uint));

        /// <summary>The clusters as last built, for the frame uniform.</summary>
        public ManaLightClusters Clusters => _clusters;

        private bool _overflowReported;

        /// <summary>Bins the scene's lights for its camera and a viewport of <paramref name="viewport"/> pixels, and uploads them.</summary>
        public void Update(ManaRenderScene scene, Vector2 viewport)
        {
            _clusters.Build(scene.LocalLights, scene.Camera, viewport);
            if (_clusters.Overflowed && !_overflowReported)
            {
                _overflowReported = true;
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                    $"[Lights] More light-in-cell pairs than {ManaLightClusters.MaxIndices}: some lights are dropped where they crowd.");
            }

            fixed (Vector4* lights = _clusters.Lights)
            fixed (uint* cells = _clusters.Cells)
            fixed (uint* indices = _clusters.Indices)
            {
                if (_clusters.LightCount > 0)
                {
                    Api.QueueWriteBuffer(Queue, LightBuffer, 0, lights, (nuint)(_clusters.LightCount * 2 * sizeof(Vector4)));
                }
                Api.QueueWriteBuffer(Queue, CellBuffer, 0, cells, (nuint)CellBufferSize);
                if (_clusters.IndexCount > 0)
                {
                    Api.QueueWriteBuffer(Queue, IndexBuffer, 0, indices, (nuint)(_clusters.IndexCount * sizeof(uint)));
                }
            }
        }

        private WebGpuBuffer* Create(ulong size)
        {
            var descriptor = new BufferDescriptor { Usage = BufferUsage.Storage | BufferUsage.CopyDst, Size = size };
            var buffer = Api.DeviceCreateBuffer(Device, &descriptor);
            if (buffer == null)
            {
                throw new InvalidOperationException("Failed to create a WebGPU light cluster buffer.");
            }
            return buffer;
        }

        public override void Dispose()
        {
            if (LightBuffer != null) { Api.BufferRelease(LightBuffer); LightBuffer = null; }
            if (CellBuffer != null) { Api.BufferRelease(CellBuffer); CellBuffer = null; }
            if (IndexBuffer != null) { Api.BufferRelease(IndexBuffer); IndexBuffer = null; }
        }
    }
}
