using System.Runtime.InteropServices;
using System.Linq;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext
    {
        private const int MaxReportedFeatureCount = 256;

        protected override ManaGraphicsDiagnosticsSnapshot CaptureSnapshotCore()
        {
            try
            {
                var properties = new AdapterProperties();
                _api.AdapterGetProperties(_adapter, &properties);

                var adapterLimits = CreateSupportedLimitsQuery();
                var hasAdapterLimits = _api.AdapterGetLimits(_adapter, &adapterLimits);
                var deviceLimits = CreateSupportedLimitsQuery();
                var hasDeviceLimits = _api.DeviceGetLimits(_device, &deviceLimits);
                var effectiveLimits = deviceLimits.Limits;
                var unavailable = ManaGraphicsDiagnosticsCommonSections.NotExposed;
                var sections = new List<ManaGraphicsDiagnosticSection>
                {
                    ManaGraphicsDiagnosticsCommonSections.CreateSystem(),
                    ManaGraphicsDiagnosticsCommonSections.CreateAdapter(
                        graphicsApi: "WebGPU",
                        renderer: ReadDiagnosticString(properties.Name),
                        vendor: $"{ReadDiagnosticString(properties.VendorName)} (0x{properties.VendorID:X4})",
                        deviceId: $"0x{properties.DeviceID:X4}",
                        driver: ReadDiagnosticString(properties.DriverDescription),
                        apiVersion: "WebGPU via wgpu-native",
                        shaderLanguage: "WGSL",
                        adapterType: properties.AdapterType.ToString(),
                        architecture: ReadDiagnosticString(properties.Architecture),
                        backend: properties.BackendType.ToString(),
#if MANA_BROWSER
                        contextProfile: $"instance=browser; requested={(_requestedBackend ?? "Automatic")}",
#else
                        contextProfile: $"instance={_instanceBackend}; requested={(_requestedBackend ?? "Automatic")}",
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

                var environmentSource = GetEffectiveEnvironmentReflectionSource(
                    _activeRenderScene?.EnvironmentReflectionSource);
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateDisplay(
                    backend: properties.BackendType.ToString(),
                    windowSize: $"{_window.Size.X} x {_window.Size.Y}",
                    framebufferSize: $"{_framebufferSize.X} x {_framebufferSize.Y}",
                    colorFormat: _surfaceFormat.ToString(),
                    depthFormat: _depthFormat.ToString(),
                    stencilFormat: "None",
                    presentation: PresentMode.Fifo.ToString(),
                    sampleCount: "1",
                    viewport: $"0, 0 — {_framebufferSize.X} x {_framebufferSize.Y}",
                    hdr: $"{FormatBoolean(_hdrPostProcessor != null)} ({GetSceneColorFormat()})",
                    depthTest: "Enabled",
                    blending: "Pipeline-dependent",
                    faceCulling: "Pipeline-dependent",
                    new ManaGraphicsDiagnosticEntry("Alpha mode", CompositeAlphaMode.Opaque.ToString(), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Surface configured", FormatBoolean(_configured), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Mesh pipeline", FormatBoolean(_demoPipelineLayout != null), ManaGraphicsDiagnosticScope.WebGpu),
                    new ManaGraphicsDiagnosticEntry("Scene capture", FormatBoolean(UseSceneCapture), ManaGraphicsDiagnosticScope.WebGpu)));
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateInputAndTiming(
                    targetFrameRate: $"{_window.FramesPerSecond:0.##} Hz",
                    targetUpdateRate: $"{_window.UpdatesPerSecond:0.##} Hz",
                    keyboards: _input?.Keyboards.Count.ToString("N0") ?? "Unavailable",
                    mice: _input?.Mice.Count.ToString("N0") ?? "Unavailable",
                    gamepads: _input?.Gamepads.Count.ToString("N0") ?? "Unavailable",
                    joysticks: _input?.Joysticks.Count.ToString("N0") ?? "Unavailable"));
                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateEnvironment(
                    environmentSource,
                    _environmentReflectionMap != null));
                sections.Add(CreateSection("Rendering Quality",
                    ("Profile", UseMobileRendering ? "Mobile (adaptive particles)" : "Desktop"),
                    ("Sky background", UseMobileRendering ? "Simple gradients and procedural stars" : "Per-pixel atmosphere"),
                    ("GPU particles requested", RequestedGpuParticleCount.ToString("N0")),
                    ("GPU particles drawn", LastGpuParticleCount.ToString("N0"))));

                var totalWebGpuPolygons = (_sceneMeshes.Sum(mesh => (long)mesh.Mesh.IndexCount) / 3).ToString("N0");
                var totalWebGpuVertices = _sceneMeshes.Sum(mesh => (long)mesh.Mesh.VertexCount).ToString("N0");

                sections.Add(ManaGraphicsDiagnosticsCommonSections.CreateResources(
                    sceneMeshes: _sceneMeshes.Count.ToString("N0"),
                    meshTextures: _meshTextures.Count.ToString("N0"),
                    shaders: _shaderModules.Count.ToString("N0"),
                    pipelines: _pipelines.Count.ToString("N0"),
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

        private static ManaGraphicsDiagnosticSection CreateVideoMemorySection(AdapterProperties properties)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                    telemetrySource: "Unavailable",
                    scope: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    physicalDedicated: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    budgetOrTotal: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    currentUsage: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    remainingOrAvailable: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    reservation: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    sharedMemory: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    notes: "Core WebGPU does not expose live memory telemetry on this platform.");
            }

            if (!TryGetDxgiMemorySnapshot(properties, out var snapshot))
            {
                return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                    telemetrySource: "DXGI unavailable",
                    scope: "Current process",
                    physicalDedicated: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    budgetOrTotal: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    currentUsage: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    remainingOrAvailable: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    reservation: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    sharedMemory: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    notes: "DXGI could not match or query the selected WebGPU adapter.");
            }

            var localRemaining = snapshot.Local.Budget > snapshot.Local.CurrentUsage
                ? snapshot.Local.Budget - snapshot.Local.CurrentUsage
                : 0;
            var usagePercent = snapshot.Local.Budget > 0
                ? snapshot.Local.CurrentUsage * 100.0 / snapshot.Local.Budget
                : 0.0;
            var additionalEntries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Adapter", snapshot.AdapterName, ManaGraphicsDiagnosticScope.WebGpu),
                new("Available for reservation", FormatBytes(snapshot.Local.AvailableForReservation), ManaGraphicsDiagnosticScope.WebGpu)
            };

            var sharedMemory = ManaGraphicsDiagnosticsCommonSections.NotExposed;
            if (snapshot.HasNonLocal)
            {
                var nonLocalRemaining = snapshot.NonLocal.Budget > snapshot.NonLocal.CurrentUsage
                    ? snapshot.NonLocal.Budget - snapshot.NonLocal.CurrentUsage
                    : 0;
                sharedMemory = $"physical {FormatBytes(snapshot.SharedCapacity)}; used {FormatBytes(snapshot.NonLocal.CurrentUsage)}; remaining {FormatBytes(nonLocalRemaining)}";
                additionalEntries.Add(new("Shared budget", FormatBytes(snapshot.NonLocal.Budget), ManaGraphicsDiagnosticScope.WebGpu));
            }

            return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                telemetrySource: "DXGI",
                scope: "Current process budget",
                physicalDedicated: FormatBytes(snapshot.DedicatedCapacity),
                budgetOrTotal: FormatBytes(snapshot.Local.Budget),
                currentUsage: $"{FormatBytes(snapshot.Local.CurrentUsage)} ({usagePercent:N1}%)",
                remainingOrAvailable: FormatBytes(localRemaining),
                reservation: FormatBytes(snapshot.Local.CurrentReservation),
                sharedMemory: sharedMemory,
                notes: "Budget and usage are assigned by the operating system and can change dynamically.",
                additionalEntries.ToArray());
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
        private void LogWebGpuDiagnostics(string? requestedBackend, InstanceBackend instanceBackend)
        {
            try
            {
                var properties = new AdapterProperties();
                _api.AdapterGetProperties(_adapter, &properties);

                var requestedBackendName = string.IsNullOrWhiteSpace(requestedBackend)
                    ? "automatic"
                    : requestedBackend.Trim();
                var environmentSource = GetEffectiveEnvironmentReflectionSource(
                    _activeRenderScene?.EnvironmentReflectionSource);

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
                    $"[WebGPU Diagnostics] Surface: {_framebufferSize.X}x{_framebufferSize.Y}; " +
                    $"color={_surfaceFormat}; depth={_depthFormat}; sceneColor={GetSceneColorFormat()}; " +
                    $"present={PresentMode.Fifo}; alpha={CompositeAlphaMode.Opaque}; samples=1.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Renderer: configured={_configured}; HDR={_hdrPostProcessor != null}; " +
                    $"meshPipeline={_demoPipelineLayout != null}; sceneProvider={_renderSceneProvider != null}; " +
                    $"fallbackDemoRequested={IsDemoMeshPipelineEnabled()}; sceneCapture={UseSceneCapture}.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Environment: source={environmentSource.SourceKind}; " +
                    $"projection={environmentSource.Projection}; cube={environmentSource.EnvironmentCubeSize}; " +
                    $"irradiance={environmentSource.IrradianceCubeSize}; prefilter={environmentSource.PrefilterCubeSize}; " +
                    $"BRDF LUT={environmentSource.BrdfLutSize}; refresh={environmentSource.SceneCaptureRefreshIntervalFrames} frames.");
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Resources: sceneMeshes={_sceneMeshes.Count}; meshTextures={_meshTextures.Count}; " +
                    $"shaderModules={_shaderModules.Count}; renderPipelines={_pipelines.Count}; " +
#if MANA_BROWSER
                    $"environmentMap={_environmentReflectionMap != null}.");
#else
                    $"environmentMap={_environmentReflectionMap != null}.");
#endif

                LogVideoMemoryDiagnostics(properties);
                CaptureInitializationSnapshot();
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
            if (_api.AdapterGetLimits(_adapter, &supportedLimits))
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
            if (_api.DeviceGetLimits(_device, &supportedLimits))
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
            return GetFeatureCount(_api.AdapterEnumerateFeatures(_adapter, (FeatureName*)null));
        }

        private int GetDeviceFeatureCount()
        {
            return GetFeatureCount(_api.DeviceEnumerateFeatures(_device, (FeatureName*)null));
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
            var written = GetFeatureCount(_api.AdapterEnumerateFeatures(_adapter, features));
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
            var written = GetFeatureCount(_api.DeviceEnumerateFeatures(_device, features));
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

        private static string ReadDiagnosticString(byte* value)
        {
            var text = ReadMessage(value).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == ulong.MaxValue)
            {
                return "Undefined / not reported";
            }

            const double kibibyte = 1024.0;
            const double mebibyte = kibibyte * 1024.0;
            const double gibibyte = mebibyte * 1024.0;

            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / gibibyte:N2} GiB",
                >= 1_048_576 => $"{bytes / mebibyte:N2} MiB",
                >= 1_024 => $"{bytes / kibibyte:N2} KiB",
                _ => $"{bytes:N0} B"
            };
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

        private static void LogVideoMemoryDiagnostics(AdapterProperties properties)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ManaConsole.WriteDiagnostic(
                    "[WebGPU Diagnostics] Video memory: live usage and budget are not exposed by core WebGPU; " +
                    "the DXGI probe is available only on Windows.");
                return;
            }

            if (!TryGetDxgiMemorySnapshot(properties, out var snapshot))
            {
                ManaConsole.WriteDiagnostic(
                    "[WebGPU Diagnostics] Video memory: DXGI could not match/query the selected WebGPU adapter.");
                return;
            }

            var localRemaining = snapshot.Local.Budget > snapshot.Local.CurrentUsage
                ? snapshot.Local.Budget - snapshot.Local.CurrentUsage
                : 0;
            ManaConsole.WriteDiagnostic(
                $"[WebGPU Diagnostics] Video memory (DXGI, current process, '{snapshot.AdapterName}'): " +
                $"physicalDedicated={FormatBytes(snapshot.DedicatedCapacity)}; budget={FormatBytes(snapshot.Local.Budget)}; " +
                $"used={FormatBytes(snapshot.Local.CurrentUsage)}; remainingBudget={FormatBytes(localRemaining)}; " +
                $"reserved={FormatBytes(snapshot.Local.CurrentReservation)}; " +
                $"availableForReservation={FormatBytes(snapshot.Local.AvailableForReservation)}.");

            if (snapshot.HasNonLocal)
            {
                var nonLocalRemaining = snapshot.NonLocal.Budget > snapshot.NonLocal.CurrentUsage
                    ? snapshot.NonLocal.Budget - snapshot.NonLocal.CurrentUsage
                    : 0;
                ManaConsole.WriteDiagnostic(
                    $"[WebGPU Diagnostics] Shared video memory (DXGI, current process): " +
                    $"physicalShared={FormatBytes(snapshot.SharedCapacity)}; budget={FormatBytes(snapshot.NonLocal.Budget)}; " +
                    $"used={FormatBytes(snapshot.NonLocal.CurrentUsage)}; remainingBudget={FormatBytes(nonLocalRemaining)}.");
            }
        }

        private static bool TryGetDxgiMemorySnapshot(
            AdapterProperties properties,
            out DxgiMemorySnapshot snapshot)
        {
            snapshot = default;
            var factoryInterfaceId = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
            void* factory = null;
            if (CreateDXGIFactory1(&factoryInterfaceId, &factory) < 0 || factory == null)
            {
                return false;
            }

            try
            {
                var factoryVtable = *(void***)factory;
                var enumAdapters =
                    (delegate* unmanaged[Stdcall]<void*, uint, void**, int>)factoryVtable[12];

                for (uint adapterIndex = 0; ; adapterIndex++)
                {
                    void* adapter = null;
                    if (enumAdapters(factory, adapterIndex, &adapter) < 0 || adapter == null)
                    {
                        break;
                    }

                    try
                    {
                        var adapterVtable = *(void***)adapter;
                        var getDescription =
                            (delegate* unmanaged[Stdcall]<void*, DxgiAdapterDescription*, int>)adapterVtable[10];
                        var description = new DxgiAdapterDescription();
                        if (getDescription(adapter, &description) < 0)
                        {
                            continue;
                        }

                        var adapterName = new string(description.Description).TrimEnd('\0');
                        if (!IsMatchingDxgiAdapter(properties, description, adapterName))
                        {
                            continue;
                        }

                        var adapter3InterfaceId = new Guid("645967A4-1392-4310-A798-8053CE3E93FD");
                        void* adapter3 = null;
                        var queryInterface =
                            (delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)adapterVtable[0];
                        if (queryInterface(adapter, &adapter3InterfaceId, &adapter3) < 0 || adapter3 == null)
                        {
                            return false;
                        }

                        try
                        {
                            var adapter3Vtable = *(void***)adapter3;
                            var queryMemoryInfo =
                                (delegate* unmanaged[Stdcall]<void*, uint, DxgiMemorySegmentGroup, DxgiQueryVideoMemoryInfo*, int>)adapter3Vtable[14];
                            var local = new DxgiQueryVideoMemoryInfo();
                            if (queryMemoryInfo(adapter3, 0, DxgiMemorySegmentGroup.Local, &local) < 0)
                            {
                                return false;
                            }

                            var nonLocal = new DxgiQueryVideoMemoryInfo();
                            var hasNonLocal = queryMemoryInfo(
                                adapter3,
                                0,
                                DxgiMemorySegmentGroup.NonLocal,
                                &nonLocal) >= 0;
                            snapshot = new DxgiMemorySnapshot
                            {
                                AdapterName = adapterName,
                                DedicatedCapacity = (ulong)description.DedicatedVideoMemory,
                                SharedCapacity = (ulong)description.SharedSystemMemory,
                                Local = local,
                                NonLocal = nonLocal,
                                HasNonLocal = hasNonLocal
                            };
                            return true;
                        }
                        finally
                        {
                            ReleaseComObject(adapter3);
                        }
                    }
                    finally
                    {
                        ReleaseComObject(adapter);
                    }
                }

                return false;
            }
            finally
            {
                ReleaseComObject(factory);
            }
        }

        private static bool IsMatchingDxgiAdapter(
            AdapterProperties properties,
            DxgiAdapterDescription description,
            string dxgiName)
        {
            if (properties.VendorID != 0 && properties.DeviceID != 0)
            {
                return description.VendorID == properties.VendorID &&
                    description.DeviceID == properties.DeviceID;
            }

            var webGpuName = ReadDiagnosticString(properties.Name);
            return dxgiName.Contains(webGpuName, StringComparison.OrdinalIgnoreCase) ||
                webGpuName.Contains(dxgiName, StringComparison.OrdinalIgnoreCase);
        }

        private static void ReleaseComObject(void* value)
        {
            if (value == null)
            {
                return;
            }

            var vtable = *(void***)value;
            var release = (delegate* unmanaged[Stdcall]<void*, uint>)vtable[2];
            release(value);
        }

        [DllImport("dxgi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int CreateDXGIFactory1(Guid* interfaceId, void** factory);

        private enum DxgiMemorySegmentGroup : uint
        {
            Local,
            NonLocal
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DxgiAdapterDescription
        {
            public fixed char Description[128];
            public uint VendorID;
            public uint DeviceID;
            public uint SubSystemID;
            public uint Revision;
            public nuint DedicatedVideoMemory;
            public nuint DedicatedSystemMemory;
            public nuint SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DxgiQueryVideoMemoryInfo
        {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        private struct DxgiMemorySnapshot
        {
            public string AdapterName;
            public ulong DedicatedCapacity;
            public ulong SharedCapacity;
            public DxgiQueryVideoMemoryInfo Local;
            public DxgiQueryVideoMemoryInfo NonLocal;
            public bool HasNonLocal;
        }
    }
}
