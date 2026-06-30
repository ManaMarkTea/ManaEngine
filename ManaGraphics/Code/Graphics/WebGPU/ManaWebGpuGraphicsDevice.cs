using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics
{
    public sealed class ManaWebGpuGraphicsDevice : IManaGraphicsDevice
    {
        public ManaWebGpuGraphicsDevice(string? requestedBackend)
        {
            RequestedBackend = string.IsNullOrWhiteSpace(requestedBackend) ? null : requestedBackend;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public string DisplayName => RequestedBackend == null ? "WebGPU" : $"WebGPU ({RequestedBackend})";
        public string? RequestedBackend { get; }
    }
}
