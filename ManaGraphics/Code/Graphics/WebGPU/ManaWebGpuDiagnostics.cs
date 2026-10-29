using static ManaGraphics.Code.Graphics.ManaWebGpuMemoryDiagnostics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics.Pipelines;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>What the context reports about itself, gathered in one place so diagnostics never reach into it.</summary>
    internal readonly record struct ManaWebGpuDiagnosticsState(
        string? RequestedBackend,
        Vector2D<int> WindowSize,
        double FramesPerSecond,
        double UpdatesPerSecond,
        IInputContext? Input,
        Vector2D<int> FramebufferSize,
        TextureFormat SurfaceFormat,
        TextureFormat DepthFormat,
        TextureFormat SceneColorFormat,
        bool HasScene,
        bool HasSceneProvider,
        bool SurfaceConfigured,
        bool SceneCapture,
        ManaEnvironmentReflectionSource? EnvironmentReflectionSource,
        bool HasEnvironmentMap,
        long ParticlesRequested,
        long ParticlesDrawn,
        IReadOnlyList<ManaWebGpuSceneMesh> Meshes,
        int MeshTextureCount,
        int ShaderModuleCount,
        int PipelineCount);

    /// <summary>
    /// The diagnostics panel's snapshot and the startup log report: the adapter and device, their limits and features,
    /// the surface, renderer state and resource counts. Reads the device; never changes anything.
    /// </summary>
    internal sealed unsafe class ManaWebGpuDiagnostics
    {
        private readonly WebGPU _api;
        private readonly ManaWebGpuNativeDevice _native;

        public ManaWebGpuDiagnostics(WebGPU api, ManaWebGpuNativeDevice native)
        {
            _api = api;
            _native = native;
        }

        private const int MaxReportedFeatureCount = 256;

        /// <summary>The snapshot the diagnostics panel shows.</summary>
        public ManaGraphicsDiagnosticsSnapshot Capture(in ManaWebGpuDiagnosticsState state)
        {
            try
            {
                var properties = new AdapterProperties();
                _api.AdapterGetProperties(_native.Adapter, &properties);

                var adapterLimits = CreateSupportedLimitsQuery();
                var hasAdapterLimits = _api.AdapterGetLimits(_native.Adapter, &adapterLimits);
                var deviceLimits = CreateSupportedLimitsQuery();
                var hasDeviceLimits = _api.DeviceGetLimits(_native.Device, &deviceLimits);
                var effectiveLimits = deviceLimits.Limits;
                var unavailable = ManaGraphicsDiagnosticsCommonSections.NotExposed;
                var sections = new List<ManaGraphicsDiagnosticSection>
                {
                    ManaGraphicsDiagnosticsCommonSections.CreateSystem(),
                    ManaGraphicsDiagnosticsCommonSections.CreateAdapter(
                        graphicsApi: "WebGPU",
#if MANA_BROWSER
                        renderer: Browser.ManaBrowserInterop.GpuAdapter(),
#else
                        renderer: ReadDiagnosticString(properties.Name),
#endif
                        vendor: $"{ReadDiagnosticString(properties.VendorName)} (0x{properties.VendorID:X4})",
                        deviceId: $"0x{properties.DeviceID:X4}",
                        driver: ReadDiagnosticString(properties.DriverDescription),
#if MANA_BROWSER
                        apiVersion: "Browser WebGPU via Emscripten",
#else
                        apiVersion: "WebGPU via wgpu-native",
#endif
                        shaderLanguage: "WGSL",
                        adapterType: properties.AdapterType.ToString(),
                        architecture: ReadDiagnosticString(properties.Architecture),
                        backend: properties.BackendType.ToString(),
#if MANA_BROWSER
                        contextProfile: $"instance=browser; requested={(state.RequestedBackend ?? "Automatic")}",
#else
                        contextProfile: $"instance={_native.InstanceBackend}; requested={(state.RequestedBackend ?? "Automatic")}",
#endif
                        powerPreference: PowerPreference.HighPerformance.ToString()),
                    CreateVideoMemorySection(properties),
                    ManaGraphicsDiagnosticsCommonSections.CreateCommonLimits(
                        maximumTexture2D: hasDeviceLimits ? $"{FormatLimit(effectiveLimits.MaxTextureDimension2D)} px" : unavailable,
                        maximumTexture3D: hasDeviceLimits ? $"{FormatLimit(effectiveLimits.MaxTextureDimension3D)} px" : unavailable,
                        maximumCubeTexture: hasDeviceLimits ? $"{FormatLimit(effectiveLimits.MaxTextureDimension2D)} px" : unavailable,
                        textureArrayLayers: hasDeviceLimits ? FormatLimit(effectiveLimits.MaxTextureArrayLayers) : unavailable,
                        textureUnitsOrBindings: hasDeviceLimits ? $"sampled textures {FormatLimit(effectiveLimits.MaxSampledTexturesPerShaderStage)}; samplers {FormatLimit(effectiveLimits.MaxSamplersPerShaderStage)}" : unavailable,
                        vertexInput: hasDeviceLimits ? $"buffers {FormatLimit(effectiveLimits.MaxVertexBuffers)}; attributes {FormatLimit(effectiveLimits.MaxVertexAttributes)}; stride {FormatLimit(effectiveLimits.MaxVertexBufferArrayStride)}" : unavailable,
                        maximumUniformBinding: hasDeviceLimits ? FormatLimitBytes(effectiveLimits.MaxUniformBufferBindingSize) : unavailable,
                        colorAttachments: hasDeviceLimits ? FormatLimit(effectiveLimits.MaxColorAttachments) : unavailable,
                        sampleCount: "1 (current renderer)",
                        computeWorkgroup: hasDeviceLimits ? $"{FormatLimit(effectiveLimits.MaxComputeWorkgroupSizeX)} x {FormatLimit(effectiveLimits.MaxComputeWorkgroupSizeY)} x {FormatLimit(effectiveLimits.MaxComputeWorkgroupSizeZ)}; {FormatLimit(effectiveLimits.MaxComputeInvocationsPerWorkgroup)} invocations" : unavailable)
                };

                sections.Add(CreateLimitsComparisonSection(
                    hasDeviceLimits,
                    deviceLimits.Limits,
                    hasAdapterLimits,
                    adapterLimits.Limits));

                var environmentSource = ManaWebGpuEnvironment.EffectiveSource(
                    state.EnvironmentReflectionSource);
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateDisplay(
                    backend: properties.BackendType.ToString(),
                    windowSize: $"{state.WindowSize.X} x {state.WindowSize.Y}",
                    framebufferSize: $"{state.FramebufferSize.X} x {state.FramebufferSize.Y}",
                    colorFormat: state.SurfaceFormat.ToString(),
                    depthFormat: state.DepthFormat.ToString(),
                    stencilFormat: "None",
                    presentation: PresentMode.Fifo.ToString(),
                    sampleCount: "1",
                    viewport: $"0, 0 — {state.FramebufferSize.X} x {state.FramebufferSize.Y}",
                    hdr: $"{FormatBoolean(state.HasScene)} ({state.SceneColorFormat})",
                    depthTest: "Enabled",
                    blending: "Pipeline-dependent",
                    faceCulling: "Pipeline-dependent",
                    new ManaGraphicsDiagnosticEntry("Alpha mode", CompositeAlphaMode.Opaque.ToString(), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Surface configured", FormatBoolean(state.SurfaceConfigured), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Mesh pipeline", FormatBoolean(state.HasScene), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Scene capture", FormatBoolean(state.SceneCapture), ManaGraphicsDiagnosticScope.WebGpu)));
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateInputAndTiming(
                    targetFrameRate: $"{state.FramesPerSecond:0.##} Hz",
                    targetUpdateRate: $"{state.UpdatesPerSecond:0.##} Hz",
                    keyboards: state.Input?.Keyboards.Count.ToString("N0") ?? "Unavailable",
                    mice: state.Input?.Mice.Count.ToString("N0") ?? "Unavailable",
                    gamepads: state.Input?.Gamepads.Count.ToString("N0") ?? "Unavailable",
                    joysticks: state.Input?.Joysticks.Count.ToString("N0") ?? "Unavailable"));
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateEnvironment(
                    environmentSource,
                    state.HasEnvironmentMap));
                sections.Add(CreateSection("Rendering Quality",
                    ("Profile", ManaWebGpuDevice.UseMobileRendering ? "Mobile (adaptive particles)" : "Desktop"),
                    ("Sky background", ManaWebGpuSkyPass.Mode switch { 0 => "Simple gradients and procedural stars", 1 => "Simple gradients and the star catalogue", _ => "Per-pixel atmosphere" }),
                    ("GPU particles requested", state.ParticlesRequested.ToString("N0")),
                    ("GPU particles drawn", state.ParticlesDrawn.ToString("N0"))));

                var totalWebGpuPolygons = (state.Meshes.Sum(mesh => (long)mesh.Mesh.IndexCount) / 3).ToString("N0");
                var totalWebGpuVertices = state.Meshes.Sum(mesh => (long)mesh.Mesh.VertexCount).ToString("N0");

                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateResources(
                    sceneMeshes: state.Meshes.Count.ToString("N0"),
                    meshTextures: state.MeshTextureCount.ToString("N0"),
                    shaders: state.ShaderModuleCount.ToString("N0"),
                    pipelines: state.PipelineCount.ToString("N0"),
                    totalPolygons: totalWebGpuPolygons,
                    totalVertices: totalWebGpuVertices));

                return new ManaGraphicsDiagnosticsSnapshot(DateTimeOffset.Now, sections);
            }
            catch (Exception exception)
            {
                return new ManaGraphicsDiagnosticsSnapshot(
                    DateTimeOffset.Now,
                    new[]
                    {
                        CreateSection(
                            "Diagnostics Error",
                            ("Message", exception.Message),
                            ("Details", exception.ToString()))
                    });
            }
        }

        private static ManaGraphicsDiagnosticSection CreateLimitsComparisonSection(
            bool hasDeviceLimits,
            Limits device,
            bool hasAdapterLimits,
            Limits adapter)
        {
            var unavailable = ManaGraphicsDiagnosticsCommonSections.NotExposed;
            string DeviceValue(uint value) => hasDeviceLimits ? FormatLimit(value) : unavailable;
            string AdapterValue(uint value) => hasAdapterLimits ? FormatLimit(value) : unavailable;
            string DeviceBytes(ulong value) => hasDeviceLimits ? FormatLimitBytes(value) : unavailable;
            string AdapterBytes(ulong value) => hasAdapterLimits ? FormatLimitBytes(value) : unavailable;

            var rows = new[]
            {
                new ManaGraphicsDiagnosticEntry("Maximum texture 1D", DeviceValue(device.MaxTextureDimension1D), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxTextureDimension1D)),
                new ManaGraphicsDiagnosticEntry("Maximum texture 2D", DeviceValue(device.MaxTextureDimension2D), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxTextureDimension2D)),
                new ManaGraphicsDiagnosticEntry("Maximum texture 3D", DeviceValue(device.MaxTextureDimension3D), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxTextureDimension3D)),
                new ManaGraphicsDiagnosticEntry("Texture array layers", DeviceValue(device.MaxTextureArrayLayers), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxTextureArrayLayers)),
                new ManaGraphicsDiagnosticEntry("Bind groups", DeviceValue(device.MaxBindGroups), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxBindGroups)),
                new ManaGraphicsDiagnosticEntry("Bind groups + vertex buffers", DeviceValue(device.MaxBindGroupsPlusVertexBuffers), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxBindGroupsPlusVertexBuffers)),
                new ManaGraphicsDiagnosticEntry("Bindings per bind group", DeviceValue(device.MaxBindingsPerBindGroup), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxBindingsPerBindGroup)),
                new ManaGraphicsDiagnosticEntry("Dynamic uniform buffers", DeviceValue(device.MaxDynamicUniformBuffersPerPipelineLayout), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxDynamicUniformBuffersPerPipelineLayout)),
                new ManaGraphicsDiagnosticEntry("Dynamic storage buffers", DeviceValue(device.MaxDynamicStorageBuffersPerPipelineLayout), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxDynamicStorageBuffersPerPipelineLayout)),
                new ManaGraphicsDiagnosticEntry("Sampled textures / shader stage", DeviceValue(device.MaxSampledTexturesPerShaderStage), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxSampledTexturesPerShaderStage)),
                new ManaGraphicsDiagnosticEntry("Samplers / shader stage", DeviceValue(device.MaxSamplersPerShaderStage), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxSamplersPerShaderStage)),
                new ManaGraphicsDiagnosticEntry("Storage buffers / shader stage", DeviceValue(device.MaxStorageBuffersPerShaderStage), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxStorageBuffersPerShaderStage)),
                new ManaGraphicsDiagnosticEntry("Storage textures / shader stage", DeviceValue(device.MaxStorageTexturesPerShaderStage), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxStorageTexturesPerShaderStage)),
                new ManaGraphicsDiagnosticEntry("Uniform buffers / shader stage", DeviceValue(device.MaxUniformBuffersPerShaderStage), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxUniformBuffersPerShaderStage)),
                new ManaGraphicsDiagnosticEntry("Maximum buffer size", DeviceBytes(device.MaxBufferSize), ManaGraphicsDiagnosticScope.WebGpu, AdapterBytes(adapter.MaxBufferSize)),
                new ManaGraphicsDiagnosticEntry("Maximum uniform binding", DeviceBytes(device.MaxUniformBufferBindingSize), ManaGraphicsDiagnosticScope.WebGpu, AdapterBytes(adapter.MaxUniformBufferBindingSize)),
                new ManaGraphicsDiagnosticEntry("Maximum storage binding", DeviceBytes(device.MaxStorageBufferBindingSize), ManaGraphicsDiagnosticScope.WebGpu, AdapterBytes(adapter.MaxStorageBufferBindingSize)),
                new ManaGraphicsDiagnosticEntry("Minimum uniform offset alignment (lower is better)", DeviceValue(device.MinUniformBufferOffsetAlignment), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MinUniformBufferOffsetAlignment)),
                new ManaGraphicsDiagnosticEntry("Minimum storage offset alignment (lower is better)", DeviceValue(device.MinStorageBufferOffsetAlignment), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MinStorageBufferOffsetAlignment)),
                new ManaGraphicsDiagnosticEntry("Vertex buffers", DeviceValue(device.MaxVertexBuffers), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxVertexBuffers)),
                new ManaGraphicsDiagnosticEntry("Vertex attributes", DeviceValue(device.MaxVertexAttributes), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxVertexAttributes)),
                new ManaGraphicsDiagnosticEntry("Vertex buffer stride", DeviceValue(device.MaxVertexBufferArrayStride), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxVertexBufferArrayStride)),
                new ManaGraphicsDiagnosticEntry("Inter-stage shader components", DeviceValue(device.MaxInterStageShaderComponents), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxInterStageShaderComponents)),
                new ManaGraphicsDiagnosticEntry("Inter-stage shader variables", DeviceValue(device.MaxInterStageShaderVariables), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxInterStageShaderVariables)),
                new ManaGraphicsDiagnosticEntry("Maximum simultaneous color attachments", DeviceValue(device.MaxColorAttachments), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxColorAttachments)),
                new ManaGraphicsDiagnosticEntry("Color attachment bytes / sample", DeviceValue(device.MaxColorAttachmentBytesPerSample), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxColorAttachmentBytesPerSample)),
                new ManaGraphicsDiagnosticEntry("Compute workgroup storage", DeviceBytes(device.MaxComputeWorkgroupStorageSize), ManaGraphicsDiagnosticScope.WebGpu, AdapterBytes(adapter.MaxComputeWorkgroupStorageSize)),
                new ManaGraphicsDiagnosticEntry("Compute invocations / workgroup", DeviceValue(device.MaxComputeInvocationsPerWorkgroup), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxComputeInvocationsPerWorkgroup)),
                new ManaGraphicsDiagnosticEntry("Compute workgroup size X", DeviceValue(device.MaxComputeWorkgroupSizeX), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxComputeWorkgroupSizeX)),
                new ManaGraphicsDiagnosticEntry("Compute workgroup size Y", DeviceValue(device.MaxComputeWorkgroupSizeY), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxComputeWorkgroupSizeY)),
                new ManaGraphicsDiagnosticEntry("Compute workgroup size Z", DeviceValue(device.MaxComputeWorkgroupSizeZ), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxComputeWorkgroupSizeZ)),
                new ManaGraphicsDiagnosticEntry("Compute workgroups / dimension", DeviceValue(device.MaxComputeWorkgroupsPerDimension), ManaGraphicsDiagnosticScope.WebGpu, AdapterValue(adapter.MaxComputeWorkgroupsPerDimension))
            };

            return new ManaGraphicsDiagnosticSection(
                "WebGPU Limits",
                rows,
                valueHeading: "Device enabled / requested",
                comparisonHeading: "Adapter capability");
        }

        private static ManaGraphicsDiagnosticSection CreateSection(
            string name,
            params (string Label, string Value)[] entries)
        {
            return new ManaGraphicsDiagnosticSection(
                name,
                entries.Select(entry => new ManaGraphicsDiagnosticEntry(
                    entry.Label,
                    entry.Value,
                    ManaGraphicsDiagnosticScope.WebGpu)).ToArray());
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "Yes" : "No";
        }

#if !MANA_BROWSER
        /// <summary>The device and renderer report written to the log once, after startup.</summary>
        public void LogStartup(in ManaWebGpuDiagnosticsState state)
        {
            try
            {
                var properties = new AdapterProperties();
                _api.AdapterGetProperties(_native.Adapter, &properties);

                var requestedBackendName = string.IsNullOrWhiteSpace(state.RequestedBackend)
                    ? "automatic"
                    : state.RequestedBackend.Trim();
                var instanceBackend = _native.InstanceBackend;
                var environmentSource = ManaWebGpuEnvironment.EffectiveSource(
                    state.EnvironmentReflectionSource);

                ManaConsole.WriteDiagnostic("[WebGPU Diagnostics] ===== Device and renderer report =====");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Runtime: {RuntimeInformation.FrameworkDescription}; " +
                    $"OS={RuntimeInformation.OSDescription}; process={RuntimeInformation.ProcessArchitecture}; " +
                    $"OS architecture={RuntimeInformation.OSArchitecture}.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Backend: requested={requestedBackendName}; instance={instanceBackend}; " +
                    $"adapter={properties.BackendType}; powerPreference={PowerPreference.HighPerformance}.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Adapter: name='{ReadDiagnosticString(properties.Name)}'; " +
                    $"type={properties.AdapterType}; architecture='{ReadDiagnosticString(properties.Architecture)}'.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Adapter IDs: vendor='{ReadDiagnosticString(properties.VendorName)}' " +
                    $"(0x{properties.VendorID:X4}); device=0x{properties.DeviceID:X4}; " +
                    $"driver='{ReadDiagnosticString(properties.DriverDescription)}'.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Adapter features ({GetAdapterFeatureCount()}): {GetAdapterFeatureSummary()}.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Device features ({GetDeviceFeatureCount()} enabled): {GetDeviceFeatureSummary()}.");

                LogAdapterLimits();
                LogDeviceLimits();

                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Surface: {state.FramebufferSize.X}x{state.FramebufferSize.Y}; " +
                    $"color={state.SurfaceFormat}; depth={state.DepthFormat}; sceneColor={state.SceneColorFormat}; " +
                    $"present={PresentMode.Fifo}; alpha={CompositeAlphaMode.Opaque}; samples=1.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Renderer: configured={state.SurfaceConfigured}; HDR={state.HasScene}; " +
                    $"meshPipeline={state.HasScene}; sceneProvider={state.HasSceneProvider}; " +
                    $"fallbackDemoRequested={ManaWebGpuSceneRenderer.DemoRequested}; sceneCapture={state.SceneCapture}.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Environment: source={environmentSource.SourceKind}; " +
                    $"projection={environmentSource.Projection}; cube={environmentSource.EnvironmentCubeSize}; " +
                    $"irradiance={environmentSource.IrradianceCubeSize}; prefilter={environmentSource.PrefilterCubeSize}; " +
                    $"BRDF LUT={environmentSource.BrdfLutSize}; refresh={environmentSource.SceneCaptureRefreshIntervalFrames} frames.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Resources: sceneMeshes={state.Meshes.Count}; meshTextures={state.MeshTextureCount}; " +
                    $"shaderModules={state.ShaderModuleCount}; renderPipelines={state.PipelineCount}; " +
                    $"environmentMap={state.HasEnvironmentMap}.");

                LogVideoMemoryDiagnostics(properties);
                ManaConsole.WriteDiagnostic("[WebGPU Diagnostics] ===== End report =====");
            }
            catch (Exception exception)
            {
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Capability report failed without affecting renderer startup: {exception}");
            }
        }
#endif

        private void LogAdapterLimits()
        {
            var supportedLimits = new SupportedLimits();
            if (_api.AdapterGetLimits(_native.Adapter, &supportedLimits))
            {
                LogLimits("Adapter limits", supportedLimits.Limits);
            }
            else
            {
                ManaConsole.WriteDiagnostic("[WebGPU Diagnostics] Adapter limits: unavailable.");
            }
        }

        private void LogDeviceLimits()
        {
            var supportedLimits = new SupportedLimits();
            if (_api.DeviceGetLimits(_native.Device, &supportedLimits))
            {
                LogLimits("Device limits", supportedLimits.Limits);
            }
            else
            {
                ManaConsole.WriteDiagnostic("[WebGPU Diagnostics] Device limits: unavailable.");
            }
        }

        private static void LogLimits(string label, Limits limits)
        {
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / textures: 1D={limits.MaxTextureDimension1D:N0}; " +
                $"2D={limits.MaxTextureDimension2D:N0}; 3D={limits.MaxTextureDimension3D:N0}; " +
                $"arrayLayers={limits.MaxTextureArrayLayers:N0}.");
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / bindings: groups={limits.MaxBindGroups:N0}; " +
                $"groups+vertexBuffers={limits.MaxBindGroupsPlusVertexBuffers:N0}; " +
                $"perGroup={limits.MaxBindingsPerBindGroup:N0}; dynamicUniform={limits.MaxDynamicUniformBuffersPerPipelineLayout:N0}; " +
                $"dynamicStorage={limits.MaxDynamicStorageBuffersPerPipelineLayout:N0}.");
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / per shader stage: sampledTextures={limits.MaxSampledTexturesPerShaderStage:N0}; " +
                $"samplers={limits.MaxSamplersPerShaderStage:N0}; storageBuffers={limits.MaxStorageBuffersPerShaderStage:N0}; " +
                $"storageTextures={limits.MaxStorageTexturesPerShaderStage:N0}; uniformBuffers={limits.MaxUniformBuffersPerShaderStage:N0}.");
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / buffers: max={FormatBytes(limits.MaxBufferSize)}; " +
                $"uniformBinding={FormatBytes(limits.MaxUniformBufferBindingSize)}; " +
                $"storageBinding={FormatBytes(limits.MaxStorageBufferBindingSize)}; " +
                $"uniformAlignment={limits.MinUniformBufferOffsetAlignment:N0}; " +
                $"storageAlignment={limits.MinStorageBufferOffsetAlignment:N0}.");
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / vertex and render: vertexBuffers={limits.MaxVertexBuffers:N0}; " +
                $"attributes={limits.MaxVertexAttributes:N0}; stride={limits.MaxVertexBufferArrayStride:N0}; " +
                $"interStageComponents={limits.MaxInterStageShaderComponents:N0}; interStageVariables={limits.MaxInterStageShaderVariables:N0}; " +
                $"colorAttachments={limits.MaxColorAttachments:N0}; colorBytesPerSample={limits.MaxColorAttachmentBytesPerSample:N0}.");
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] {label} / compute: workgroupStorage={FormatBytes(limits.MaxComputeWorkgroupStorageSize)}; " +
                $"invocations={limits.MaxComputeInvocationsPerWorkgroup:N0}; " +
                $"workgroupSize={limits.MaxComputeWorkgroupSizeX:N0}x{limits.MaxComputeWorkgroupSizeY:N0}x{limits.MaxComputeWorkgroupSizeZ:N0}; " +
                $"workgroupsPerDimension={limits.MaxComputeWorkgroupsPerDimension:N0}.");
        }

        private int GetAdapterFeatureCount()
        {
            return GetFeatureCount(_api.AdapterEnumerateFeatures(_native.Adapter, (FeatureName*)null));
        }

        private int GetDeviceFeatureCount()
        {
            return GetFeatureCount(_api.DeviceEnumerateFeatures(_native.Device, (FeatureName*)null));
        }

        private string GetAdapterFeatureSummary()
        {
            var featureCount = GetAdapterFeatureCount();
            if (featureCount == 0)
            {
                return "none";
            }

            if (featureCount > MaxReportedFeatureCount)
            {
                return $"not enumerated (unexpected feature count {featureCount})";
            }

            var features = stackalloc FeatureName[featureCount];
            var written = GetFeatureCount(_api.AdapterEnumerateFeatures(_native.Adapter, features));
            return FormatFeatures(features, Math.Min(featureCount, written));
        }

        private string GetDeviceFeatureSummary()
        {
            var featureCount = GetDeviceFeatureCount();
            if (featureCount == 0)
            {
                return "none";
            }

            if (featureCount > MaxReportedFeatureCount)
            {
                return $"not enumerated (unexpected feature count {featureCount})";
            }

            var features = stackalloc FeatureName[featureCount];
            var written = GetFeatureCount(_api.DeviceEnumerateFeatures(_native.Device, features));
            return FormatFeatures(features, Math.Min(featureCount, written));
        }

        private static int GetFeatureCount(nuint featureCount)
        {
            return featureCount > int.MaxValue ? int.MaxValue : (int)featureCount;
        }

        private static string FormatFeatures(FeatureName* features, int featureCount)
        {
            var names = new string[featureCount];
            for (var index = 0; index < featureCount; index++)
            {
                names[index] = features[index].ToString();
            }

            Array.Sort(names, StringComparer.Ordinal);
            return names.Length == 0 ? "none" : string.Join(", ", names);
        }

        private static string FormatLimit(uint value)
        {
            return value == uint.MaxValue
                ? "Undefined / not reported"
                : value.ToString("N0");
        }

        private static string FormatLimitBytes(ulong value)
        {
            return FormatBytes(value);
        }

        private static SupportedLimits CreateSupportedLimitsQuery()
        {
            return new SupportedLimits();
        }
    }
}
