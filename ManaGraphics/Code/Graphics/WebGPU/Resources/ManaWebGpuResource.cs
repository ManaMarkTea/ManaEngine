using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Something that owns GPU objects made on the context's device: passes, shadow map, bindings, targets. The device is
    /// borrowed; every resource is disposed before the context releases it.
    /// </summary>
    internal abstract unsafe class ManaWebGpuResource : IDisposable
    {
        protected ManaWebGpuResource(ManaWebGpuDevice gpu)
        {
            Gpu = gpu;
        }

        protected ManaWebGpuDevice Gpu { get; }
        protected WebGPU Api => Gpu.Api;
        protected Device* Device => Gpu.Device;
        protected Queue* Queue => Gpu.Queue;

        public abstract void Dispose();
    }
}
