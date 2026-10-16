using System.Runtime.InteropServices;

namespace ManaCore.Graphics
{
    public abstract class ManaGraphicsDiagnosticsProvider
    {
        public ManaGraphicsDiagnosticsSnapshot DiagnosticsSnapshot { get; protected set; } =
            ManaGraphicsDiagnosticsSnapshot.Empty;

        public ManaGraphicsDiagnosticsSnapshot CaptureInitializationSnapshot()
        {
            DiagnosticsSnapshot = WithCapturePhase(
                CaptureSnapshotCore(),
                "Before UI initialization");
            return DiagnosticsSnapshot;
        }

        public ManaGraphicsDiagnosticsSnapshot RefreshDiagnosticsSnapshot()
        {
            DiagnosticsSnapshot = WithCapturePhase(CaptureSnapshotCore(), "Live refresh");
            return DiagnosticsSnapshot;
        }

        public virtual ManaGraphicsDiagnosticsSnapshot RefreshLiveDiagnosticsSnapshot()
        {
            return RefreshDiagnosticsSnapshot();
        }

        protected abstract ManaGraphicsDiagnosticsSnapshot CaptureSnapshotCore();

        protected static ManaGraphicsDiagnosticsSnapshot WithCapturePhase(
            ManaGraphicsDiagnosticsSnapshot snapshot,
            string capturePhase)
        {
            return new ManaGraphicsDiagnosticsSnapshot(
                snapshot.CapturedAt,
                snapshot.Sections,
                capturePhase);
        }
    }

    public sealed class ManaGraphicsDiagnosticsSnapshot
    {
        public static ManaGraphicsDiagnosticsSnapshot Empty { get; } = new(
            DateTimeOffset.MinValue,
            Array.Empty<ManaGraphicsDiagnosticSection>());

        public ManaGraphicsDiagnosticsSnapshot(
            DateTimeOffset capturedAt,
            IReadOnlyList<ManaGraphicsDiagnosticSection> sections,
            string capturePhase = "Runtime")
        {
            CapturedAt = capturedAt;
            Sections = sections;
            CapturePhase = capturePhase;
        }

        public DateTimeOffset CapturedAt { get; }
        public IReadOnlyList<ManaGraphicsDiagnosticSection> Sections { get; }
        public string CapturePhase { get; }
    }

    public sealed class ManaGraphicsDiagnosticSection
    {
        public ManaGraphicsDiagnosticSection(
            string name,
            IReadOnlyList<ManaGraphicsDiagnosticEntry> entries,
            string valueHeading = "Value",
            string? comparisonHeading = null)
        {
            Name = name;
            Entries = entries;
            ValueHeading = valueHeading;
            ComparisonHeading = comparisonHeading;
        }

        public string Name { get; }
        public IReadOnlyList<ManaGraphicsDiagnosticEntry> Entries { get; }
        public string ValueHeading { get; }
        public string? ComparisonHeading { get; }
    }

    public enum ManaGraphicsDiagnosticScope
    {
        Common,
        WebGpu
    }

    public readonly record struct ManaGraphicsDiagnosticEntry(
        string Label,
        string Value,
        ManaGraphicsDiagnosticScope Scope = ManaGraphicsDiagnosticScope.Common,
        string? ComparisonValue = null);

    public static class ManaGraphicsDiagnosticsCommonSections
    {
        public const string NotExposed = "Not exposed by this API";

        public static ManaGraphicsDiagnosticSection CreateSystem()
        {
            return Create(
                "System",
                ("Runtime", RuntimeInformation.FrameworkDescription),
                ("Operating system", RuntimeInformation.OSDescription),
                ("Process architecture", RuntimeInformation.ProcessArchitecture.ToString()),
                ("OS architecture", RuntimeInformation.OSArchitecture.ToString()));
        }

        public static ManaGraphicsDiagnosticSection CreateAdapter(
            string graphicsApi,
            string renderer,
            string vendor,
            string deviceId,
            string driver,
            string apiVersion,
            string shaderLanguage,
            string adapterType,
            string architecture,
            string backend,
            string contextProfile,
            string powerPreference)
        {
            return Create(
                "Graphics Adapter",
                ("Graphics API", graphicsApi),
                ("Renderer", renderer),
                ("Vendor", vendor),
                ("Device ID", deviceId),
                ("Driver", driver),
                ("API version", apiVersion),
                ("Shader language", shaderLanguage),
                ("Adapter type", adapterType),
                ("Architecture", architecture),
                ("Backend", backend),
                ("Context / profile", contextProfile),
                ("Power preference", powerPreference));
        }

        public static ManaGraphicsDiagnosticSection CreateVideoMemory(
            string telemetrySource,
            string scope,
            string physicalDedicated,
            string budgetOrTotal,
            string currentUsage,
            string remainingOrAvailable,
            string reservation,
            string sharedMemory,
            string notes,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Telemetry source", telemetrySource),
                new("Scope", scope),
                new("Physical dedicated memory", physicalDedicated),
                new("Budget / total available", budgetOrTotal),
                new("Current usage", currentUsage),
                new("Remaining / currently available", remainingOrAvailable),
                new("Current reservation", reservation),
                new("Shared memory", sharedMemory),
                new("Notes", notes)
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Video Memory", entries);
        }

        public static ManaGraphicsDiagnosticSection CreateDisplay(
            string backend,
            string windowSize,
            string framebufferSize,
            string colorFormat,
            string depthFormat,
            string stencilFormat,
            string presentation,
            string sampleCount,
            string viewport,
            string hdr,
            string depthTest,
            string blending,
            string faceCulling,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Backend", backend),
                new("Window size", windowSize),
                new("Framebuffer size", framebufferSize),
                new("Color format", colorFormat),
                new("Depth format", depthFormat),
                new("Stencil format", stencilFormat),
                new("Presentation / VSync", presentation),
                new("Sample count", sampleCount),
                new("Viewport", viewport),
                new("HDR", hdr),
                new("Depth test", depthTest),
                new("Blending", blending),
                new("Face culling", faceCulling)
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Display and Rendering", entries);
        }

        public static ManaGraphicsDiagnosticSection CreateCommonLimits(
            string maximumTexture2D,
            string maximumTexture3D,
            string maximumCubeTexture,
            string textureArrayLayers,
            string textureUnitsOrBindings,
            string vertexInput,
            string maximumUniformBinding,
            string colorAttachments,
            string sampleCount,
            string computeWorkgroup,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Maximum 2D texture", maximumTexture2D),
                new("Maximum 3D texture", maximumTexture3D),
                new("Maximum cube texture", maximumCubeTexture),
                new("Texture array layers", textureArrayLayers),
                new("Texture units / bindings", textureUnitsOrBindings),
                new("Vertex input", vertexInput),
                new("Maximum uniform binding", maximumUniformBinding),
                new("Color attachments", colorAttachments),
                new("Sample count", sampleCount),
                new("Compute workgroup", computeWorkgroup)
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Common Graphics Limits", entries);
        }

        public static ManaGraphicsDiagnosticSection CreateInputAndTiming(
            string targetFrameRate,
            string targetUpdateRate,
            string keyboards,
            string mice,
            string gamepads,
            string joysticks,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Target frame rate", targetFrameRate),
                new("Target update rate", targetUpdateRate),
                new("Keyboards", keyboards),
                new("Mice", mice),
                new("Gamepads", gamepads),
                new("Joysticks", joysticks)
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Input and Timing", entries);
        }

        public static ManaGraphicsDiagnosticSection CreateEnvironment(
            ManaEnvironmentReflectionSource source,
            bool isMapReady,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Source", source.SourceKind.ToString()),
                new("Projection", source.Projection.ToString()),
                new("Environment cube", $"{source.EnvironmentCubeSize} px"),
                new("Irradiance cube", $"{source.IrradianceCubeSize} px"),
                new("Prefilter cube", $"{source.PrefilterCubeSize} px"),
                new("BRDF LUT", $"{source.BrdfLutSize} px"),
                new("Capture refresh", $"{source.SceneCaptureRefreshIntervalFrames} frames"),
                new("Environment map ready", isMapReady ? "Yes" : "No")
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Environment", entries);
        }

        public static ManaGraphicsDiagnosticSection CreateResources(
            string sceneMeshes,
            string meshTextures,
            string shaders,
            string pipelines,
            string totalPolygons,
            string totalVertices,
            params ManaGraphicsDiagnosticEntry[] additionalEntries)
        {
            var entries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Scene meshes", sceneMeshes),
                new("Mesh textures", meshTextures),
                new("Shader modules / programs", shaders),
                new("Render pipelines", pipelines),
                new("Total polygons", totalPolygons),
                new("Total vertices", totalVertices)
            };
            entries.AddRange(additionalEntries);
            return new ManaGraphicsDiagnosticSection("Resources", entries);
        }

        private static ManaGraphicsDiagnosticSection Create(
            string name,
            params (string Label, string Value)[] entries)
        {
            return new ManaGraphicsDiagnosticSection(
                name,
                entries.Select(entry => new ManaGraphicsDiagnosticEntry(entry.Label, entry.Value)).ToArray());
        }
    }
}
