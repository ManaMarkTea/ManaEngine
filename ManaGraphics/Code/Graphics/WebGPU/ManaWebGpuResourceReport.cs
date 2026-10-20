#if !MANA_BROWSER
using ManaCore.Graphics;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Publishes wgpu-native's registry of every object it made as the app's GPU resource report (desktop and mobile:
    /// the browser's WebGPU cannot list what it holds). Diagnostic only; never touches a resource.
    /// </summary>
    internal sealed unsafe class ManaWebGpuResourceReport : IDisposable
    {
        private readonly ManaWebGpuNativeDevice _native;
        private readonly Func<IReadOnlyDictionary<string, long>?> _provider;
        private bool _retracted;

        public ManaWebGpuResourceReport(ManaWebGpuNativeDevice native)
        {
            _native = native;
            _provider = ReadResourceCounts;
            ManaGpuResourceReport.Provider = _provider;
        }

        public void Dispose()
        {
            _retracted = true;
            // Only our own: a newer context (a pop-out, a reopened editor) may have replaced the provider already.
            if (ManaGpuResourceReport.Provider == _provider)
            {
                ManaGpuResourceReport.Provider = null;
            }
        }

        private IReadOnlyDictionary<string, long>? ReadResourceCounts()
        {
            if (_retracted || _native.NativeExtension is not { } extension || _native.Instance == null)
            {
                return null;
            }

            var report = new GlobalReport();
            extension.GenerateReport(_native.Instance, &report);

            var counts = new Dictionary<string, long>();
            // Only the backend actually in use has anything in it; summing them all keeps that from mattering.
            foreach (var hub in new[] { report.Vulkan, report.Metal, report.Dx12, report.Gl })
            {
                Add(counts, "adapters", hub.Adapters);
                Add(counts, "devices", hub.Devices);
                Add(counts, "queues", hub.Queues);
                Add(counts, "pipelineLayouts", hub.PipelineLayouts);
                Add(counts, "shaderModules", hub.ShaderModules);
                Add(counts, "bindGroupLayouts", hub.BindGroupLayouts);
                Add(counts, "bindGroups", hub.BindGroups);
                Add(counts, "commandBuffers", hub.CommandBuffers);
                Add(counts, "renderBundles", hub.RenderBundles);
                Add(counts, "renderPipelines", hub.RenderPipelines);
                Add(counts, "computePipelines", hub.ComputePipelines);
                Add(counts, "querySets", hub.QuerySets);
                Add(counts, "buffers", hub.Buffers);
                Add(counts, "textures", hub.Textures);
                Add(counts, "textureViews", hub.TextureViews);
                Add(counts, "samplers", hub.Samplers);
            }

            return counts;
        }

        private static void Add(Dictionary<string, long> counts, string kind, RegistryReport registry)
        {
            var held = (long)registry.NumKeptFromUser;
            var internalOnly = (long)registry.NumAllocated - held;
            counts[kind] = counts.GetValueOrDefault(kind) + held;
            if (internalOnly != 0)
            {
                counts[kind + "+internal"] = counts.GetValueOrDefault(kind + "+internal") + internalOnly;
            }
        }
    }
}
#endif
