using System.Numerics;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics
{
    public sealed class ManaWebGpuGraphicsDevice : IManaGraphicsDevice, IHeightSamplingComputeDevice
    {
        public ManaWebGpuGraphicsDevice(string? requestedBackend)
        {
            RequestedBackend = string.IsNullOrWhiteSpace(requestedBackend) ? null : requestedBackend;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public string DisplayName => RequestedBackend == null ? "WebGPU" : $"WebGPU ({RequestedBackend})";
        public string? RequestedBackend { get; }

        // This device wrapper is constructed before ManaWebGpuContext exists (see
        // VoxelEditorGameMode.RunWebGpu -- the ManaContext/GraphicsDevice is built in the window's first
        // Load callback, while the real WebGPU context only becomes available slightly later, in the
        // webGpuReady callback). Set once it does; SampleHeightsAsync forwards to it.
        public ManaWebGpuContext? Context { get; set; }

        public Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            if (Context == null)
            {
                throw new InvalidOperationException("WebGPU context is not yet initialized.");
            }

            return Context.SampleHeightsAsync(lonLatPointsDegrees, tilesNewestFirst);
        }
    }
}
