namespace ManaCore.Diagnostics
{
    /// <summary>
    /// The GPU this run draws with, as the graphics backend found it ("NVIDIA GeForce GTX 1650 (Vulkan, discrete)", or a
    /// browser's vendor and architecture) -- shown where someone asks why a machine runs slowly. Empty until it is known.
    /// </summary>
    public static class ManaGpuInfo
    {
        public static string Description { get; set; } = string.Empty;
    }
}
