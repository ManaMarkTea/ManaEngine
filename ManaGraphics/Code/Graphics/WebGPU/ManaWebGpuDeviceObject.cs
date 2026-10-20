using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Owns GPU objects made on the context's device: every pass, resource and renderer. The device is borrowed, and
    /// every owner is disposed before the context releases it. Handles released by their owner (a mesh texture, a cube
    /// face resource) are not owners.
    /// </summary>
    internal abstract unsafe class ManaWebGpuDeviceObject : IDisposable
    {
        protected ManaWebGpuDeviceObject(ManaWebGpuDevice gpu)
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
