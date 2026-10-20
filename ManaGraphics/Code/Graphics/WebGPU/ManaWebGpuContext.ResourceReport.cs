#if !MANA_BROWSER
using ManaCore.Graphics;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext
    {
        private Func<IReadOnlyDictionary<string, long>?>? _resourceReportProvider;

        // wgpu-native keeps a registry of every object it made; this reads it (desktop and mobile only -- the browser's
        // WebGPU has no way to list what it holds). Diagnostic: never touches or releases a resource.
        private void PublishResourceReport()
        {
            _resourceReportProvider = ReadResourceCounts;
            ManaGpuResourceReport.Provider = _resourceReportProvider;
        }

        private void RetractResourceReport()
        {
            // Only our own: a newer context (a pop-out, a reopened editor) may have replaced the provider already.
            if (_resourceReportProvider != null && ManaGpuResourceReport.Provider == _resourceReportProvider)
            {
                ManaGpuResourceReport.Provider = null;
            }

            _resourceReportProvider = null;
        }

        private IReadOnlyDictionary<string, long>? ReadResourceCounts()
        {
            if (_disposed || _native?.NativeExtension is not { } extension || _native.Instance == null)
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
