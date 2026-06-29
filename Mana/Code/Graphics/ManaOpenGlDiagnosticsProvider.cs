using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public sealed unsafe class ManaOpenGlDiagnosticsProvider : ManaGraphicsDiagnosticsProvider
    {
        private const uint GpuMemoryInfoDedicatedVideoMemoryNvx = 0x9047;
        private const uint GpuMemoryInfoTotalAvailableMemoryNvx = 0x9048;
        private const uint GpuMemoryInfoCurrentAvailableVideoMemoryNvx = 0x9049;
        private const uint GpuMemoryInfoEvictionCountNvx = 0x904A;
        private const uint GpuMemoryInfoEvictedMemoryNvx = 0x904B;
        private const uint VboFreeMemoryAti = 0x87FB;
        private const uint TextureFreeMemoryAti = 0x87FC;
        private const uint RenderbufferFreeMemoryAti = 0x87FD;

        private readonly ManaContext _context;

        public ManaOpenGlDiagnosticsProvider(ManaContext context)
        {
            _context = context;
        }

        protected override ManaGraphicsDiagnosticsSnapshot CaptureSnapshotCore()
        {
            var context = _context;
            try
            {
                var gl = context.RequireOpenGl();
                var extensions = GetExtensions(gl);
                var majorVersion = gl.GetInteger(GLEnum.MajorVersion);
                var minorVersion = gl.GetInteger(GLEnum.MinorVersion);
                var sections = new List<ManaGraphicsDiagnosticSection>
                {
                    ManaGraphicsDiagnosticsCommonSections.CreateSystem(),
                    ManaGraphicsDiagnosticsCommonSections.CreateAdapter(
                        graphicsApi: "OpenGL",
                        renderer: GetString(gl, StringName.Renderer),
                        vendor: GetString(gl, StringName.Vendor),
                        deviceId: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                        driver: GetString(gl, StringName.Version),
                        apiVersion: $"OpenGL {majorVersion}.{minorVersion}",
                        shaderLanguage: GetString(gl, StringName.ShadingLanguageVersion),
                        adapterType: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                        architecture: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                        backend: "OpenGL",
                        contextProfile: $"{TryGetInteger(gl, GLEnum.ContextProfileMask, DescribeProfile)}; {TryGetInteger(gl, GLEnum.ContextFlags, DescribeContextFlags)}",
                        powerPreference: ManaGraphicsDiagnosticsCommonSections.NotExposed)
                };

                sections.Add(CaptureSection(
                    "Video Memory",
                    () => CreateVideoMemorySection(gl, extensions)));
                sections.Add(CaptureSection(
                    "Common Graphics Limits",
                    () => CreateLimitsSection(gl, majorVersion, minorVersion)));
                sections.Add(CaptureSection(
                    "Display and Rendering",
                    () => CreateFramebufferSection(gl, context)));
                sections.Add(CreateSection(
                    "Input and Timing",
                    ("Target frame rate", $"{context.Window.FramesPerSecond:0.##} Hz"),
                    ("Target update rate", $"{context.Window.UpdatesPerSecond:0.##} Hz"),
                    ("Keyboards", context.Input.Keyboards.Count.ToString("N0")),
                    ("Mice", context.Input.Mice.Count.ToString("N0")),
                    ("Gamepads", context.Input.Gamepads.Count.ToString("N0")),
                    ("Joysticks", context.Input.Joysticks.Count.ToString("N0"))));
                sections.Add(CreateSection(
                    "Extensions",
                    ("Count", extensions.Count.ToString("N0")),
                    ("Available extensions", extensions.Count == 0 ? "None reported" : string.Join(", ", extensions))));

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

        public override ManaGraphicsDiagnosticsSnapshot RefreshLiveDiagnosticsSnapshot()
        {
            if (DiagnosticsSnapshot.Sections.Count == 0)
            {
                return CaptureInitializationSnapshot();
            }

            var liveSnapshot = CaptureSnapshotCore();
            var liveMemorySection = liveSnapshot.Sections.FirstOrDefault(
                section => section.Name == "Video Memory");
            if (liveMemorySection == null)
            {
                return DiagnosticsSnapshot;
            }

            var mergedSections = DiagnosticsSnapshot.Sections
                .Select(section => section.Name == "Video Memory" ? liveMemorySection : section)
                .ToArray();
            DiagnosticsSnapshot = new ManaGraphicsDiagnosticsSnapshot(
                liveSnapshot.CapturedAt,
                mergedSections,
                "Pre-ImGui capabilities/state with refreshed live memory");
            return DiagnosticsSnapshot;
        }

        private static ManaGraphicsDiagnosticSection CreateVideoMemorySection(
            GL gl,
            IReadOnlyCollection<string> extensions)
        {
            if (HasExtension(gl, extensions, "GL_NVX_gpu_memory_info"))
            {
                var dedicated = GetInteger(gl, GpuMemoryInfoDedicatedVideoMemoryNvx);
                var totalAvailable = GetInteger(gl, GpuMemoryInfoTotalAvailableMemoryNvx);
                var currentAvailable = GetInteger(gl, GpuMemoryInfoCurrentAvailableVideoMemoryNvx);
                var used = Math.Max(0, totalAvailable - currentAvailable);
                var usagePercent = totalAvailable > 0 ? used * 100.0 / totalAvailable : 0.0;

                return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                    telemetrySource: "GL_NVX_gpu_memory_info",
                    scope: "Associated GPU; system/driver-wide estimate including other applications",
                    physicalDedicated: FormatKibibytes(dedicated),
                    budgetOrTotal: FormatKibibytes(totalAvailable),
                    currentUsage: $"{FormatKibibytes(used)} ({usagePercent:N1}%)",
                    remainingOrAvailable: FormatKibibytes(currentAvailable),
                    reservation: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    sharedMemory: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    notes: "Approximate overall GPU utilization, not this process alone. Usage is estimated as total available minus currently available.",
                    new ManaGraphicsDiagnosticEntry("Eviction count", GetInteger(gl, GpuMemoryInfoEvictionCountNvx).ToString("N0"), ManaGraphicsDiagnosticScope.OpenGl),
                    new ManaGraphicsDiagnosticEntry("Evicted memory", FormatKibibytes(GetInteger(gl, GpuMemoryInfoEvictedMemoryNvx)), ManaGraphicsDiagnosticScope.OpenGl));
            }

            if (HasExtension(gl, extensions, "GL_ATI_meminfo"))
            {
                var vbo = GetAtiMemory(gl, VboFreeMemoryAti);
                var texture = GetAtiMemory(gl, TextureFreeMemoryAti);
                var renderbuffer = GetAtiMemory(gl, RenderbufferFreeMemoryAti);
                return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                    telemetrySource: "GL_ATI_meminfo",
                    scope: "Associated GPU driver pools; not process-scoped",
                    physicalDedicated: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    budgetOrTotal: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    currentUsage: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    remainingOrAvailable: FormatKibibytes(texture.TotalFree),
                    reservation: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    sharedMemory: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                    notes: "ATI_meminfo exposes free pools rather than a unified usage budget.",
                    new ManaGraphicsDiagnosticEntry("VBO free", FormatKibibytes(vbo.TotalFree), ManaGraphicsDiagnosticScope.OpenGl),
                    new ManaGraphicsDiagnosticEntry("Largest VBO block", FormatKibibytes(vbo.LargestBlock), ManaGraphicsDiagnosticScope.OpenGl),
                    new ManaGraphicsDiagnosticEntry("Largest texture block", FormatKibibytes(texture.LargestBlock), ManaGraphicsDiagnosticScope.OpenGl),
                    new ManaGraphicsDiagnosticEntry("Renderbuffer free", FormatKibibytes(renderbuffer.TotalFree), ManaGraphicsDiagnosticScope.OpenGl),
                    new ManaGraphicsDiagnosticEntry("Largest renderbuffer block", FormatKibibytes(renderbuffer.LargestBlock), ManaGraphicsDiagnosticScope.OpenGl));
            }

            return ManaGraphicsDiagnosticsCommonSections.CreateVideoMemory(
                telemetrySource: "Unavailable",
                scope: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                physicalDedicated: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                budgetOrTotal: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                currentUsage: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                remainingOrAvailable: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                reservation: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                sharedMemory: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                notes: "Core OpenGL has no portable VRAM telemetry; this driver exposes neither NVX_gpu_memory_info nor ATI_meminfo.");
        }

        private static ManaGraphicsDiagnosticSection CreateLimitsSection(GL gl, int major, int minor)
        {
            var additionalEntries = new List<ManaGraphicsDiagnosticEntry>
            {
                new("Uniform buffer bindings", TryGetInteger(gl, GLEnum.MaxUniformBufferBindings, value => value.ToString("N0"))),
                new("Shader uniform components", $"vertex {TryGetInteger(gl, GLEnum.MaxVertexUniformComponents, value => value.ToString("N0"))}; fragment {TryGetInteger(gl, GLEnum.MaxFragmentUniformComponents, value => value.ToString("N0"))}"),
                new("Draw buffers", TryGetInteger(gl, GLEnum.MaxDrawBuffers, value => value.ToString("N0"))),
                new("Maximum renderbuffer", TryGetInteger(gl, GLEnum.MaxRenderbufferSize, value => $"{value:N0} px")),
                new("Maximum viewport", TryGetMaxViewport(gl)),
                new("Recommended draw range", $"vertices {TryGetInteger(gl, GLEnum.MaxElementsVertices, value => value.ToString("N0"))}; indices {TryGetInteger(gl, GLEnum.MaxElementsIndices, value => value.ToString("N0"))}")
            };

            var computeWorkgroup = ManaGraphicsDiagnosticsCommonSections.NotExposed;
            if (major > 4 || major == 4 && minor >= 3)
            {
                try
                {
                    var maxComputeCount = stackalloc int[3];
                    var maxComputeSize = stackalloc int[3];
                    for (uint dimension = 0; dimension < 3; dimension++)
                    {
                        gl.GetInteger(GLEnum.MaxComputeWorkGroupCount, dimension, maxComputeCount + dimension);
                        gl.GetInteger(GLEnum.MaxComputeWorkGroupSize, dimension, maxComputeSize + dimension);
                    }

                    computeWorkgroup = $"{maxComputeSize[0]:N0} x {maxComputeSize[1]:N0} x {maxComputeSize[2]:N0}; {gl.GetInteger(GLEnum.MaxComputeWorkGroupInvocations):N0} invocations";
                    additionalEntries.Add(new("Compute workgroup count", $"{maxComputeCount[0]:N0} x {maxComputeCount[1]:N0} x {maxComputeCount[2]:N0}"));
                }
                catch (Exception exception)
                {
                    computeWorkgroup = $"Unavailable ({exception.Message})";
                }

                additionalEntries.Add(new("Shader storage bindings", TryGetInteger(gl, GLEnum.MaxShaderStorageBufferBindings, value => value.ToString("N0"))));
                additionalEntries.Add(new("Maximum shader storage block", TryGetInteger(gl, GLEnum.MaxShaderStorageBlockSize, value => FormatBytes((ulong)Math.Max(0, value)))));
                additionalEntries.Add(new("Compute shared memory", TryGetInteger(gl, GLEnum.MaxComputeSharedMemorySize, value => FormatBytes((ulong)Math.Max(0, value)))));
            }

            additionalEntries = additionalEntries
                .Select(entry => entry with { Scope = ManaGraphicsDiagnosticScope.OpenGl })
                .ToList();

            return ManaGraphicsDiagnosticsCommonSections.CreateCommonLimits(
                maximumTexture2D: TryGetInteger(gl, GLEnum.MaxTextureSize, value => $"{value:N0} px"),
                maximumTexture3D: TryGetInteger(gl, GLEnum.Max3DTextureSize, value => $"{value:N0} px"),
                maximumCubeTexture: TryGetInteger(gl, GLEnum.MaxCubeMapTextureSize, value => $"{value:N0} px"),
                textureArrayLayers: TryGetInteger(gl, GLEnum.MaxArrayTextureLayers, value => value.ToString("N0")),
                textureUnitsOrBindings: $"fragment {TryGetInteger(gl, GLEnum.MaxTextureImageUnits, value => value.ToString("N0"))}; vertex {TryGetInteger(gl, GLEnum.MaxVertexTextureImageUnits, value => value.ToString("N0"))}; combined {TryGetInteger(gl, GLEnum.MaxCombinedTextureImageUnits, value => value.ToString("N0"))}",
                vertexInput: $"attributes {TryGetInteger(gl, GLEnum.MaxVertexAttribs, value => value.ToString("N0"))}",
                maximumUniformBinding: TryGetInteger(gl, GLEnum.MaxUniformBlockSize, value => FormatBytes((ulong)Math.Max(0, value))),
                colorAttachments: TryGetInteger(gl, GLEnum.MaxColorAttachments, value => value.ToString("N0")),
                sampleCount: TryGetInteger(gl, GLEnum.MaxSamples, value => value.ToString("N0")),
                computeWorkgroup: computeWorkgroup,
                additionalEntries.ToArray());
        }

        private static ManaGraphicsDiagnosticSection CreateFramebufferSection(GL gl, ManaContext context)
        {
            return ManaGraphicsDiagnosticsCommonSections.CreateDisplay(
                backend: context.GraphicsBackend.ToString(),
                windowSize: $"{context.Window.Size.X} x {context.Window.Size.Y}",
                framebufferSize: $"{context.Window.FramebufferSize.X} x {context.Window.FramebufferSize.Y}",
                colorFormat: "Default framebuffer (driver-selected)",
                depthFormat: $"{context.Window.PreferredDepthBufferBits?.ToString() ?? "Default"} bits requested",
                stencilFormat: $"{context.Window.PreferredStencilBufferBits?.ToString() ?? "Default"} bits requested",
                presentation: context.Window.VSync.ToString(),
                sampleCount: "Default framebuffer",
                viewport: TryGetViewport(gl),
                hdr: ManaGraphicsDiagnosticsCommonSections.NotExposed,
                depthTest: TryGetEnabled(gl, EnableCap.DepthTest),
                blending: TryGetEnabled(gl, EnableCap.Blend),
                faceCulling: TryGetEnabled(gl, EnableCap.CullFace),
                new ManaGraphicsDiagnosticEntry("State capture timing", "Captured before ImGui initializes or binds UI render state.", ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Depth writes", TryGetBoolean(gl, GLEnum.DepthWritemask), ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Scissor test", TryGetEnabled(gl, EnableCap.ScissorTest), ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Framebuffer sRGB", TryGetEnabled(gl, EnableCap.FramebufferSrgb), ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Draw framebuffer", TryGetInteger(gl, GLEnum.DrawFramebufferBinding, value => value.ToString("N0")), ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Read framebuffer", TryGetInteger(gl, GLEnum.ReadFramebufferBinding, value => value.ToString("N0")), ManaGraphicsDiagnosticScope.OpenGl),
                new ManaGraphicsDiagnosticEntry("Window API", $"{context.Window.API.API} {context.Window.API.Version.MajorVersion}.{context.Window.API.Version.MinorVersion} {context.Window.API.Profile}/{context.Window.API.Flags}", ManaGraphicsDiagnosticScope.OpenGl));
        }

        private static List<string> GetExtensions(GL gl)
        {
            var extensions = new List<string>();
            try
            {
                var extensionCount = gl.GetInteger(GLEnum.NumExtensions);
                for (uint index = 0; index < extensionCount; index++)
                {
                    var extension = gl.GetStringS(GLEnum.Extensions, index);
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        extensions.Add(extension);
                    }
                }
            }
            catch
            {
                var extensionString = GetString(gl, StringName.Extensions);
                if (!extensionString.StartsWith("Unavailable", StringComparison.Ordinal))
                {
                    extensions.AddRange(extensionString.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            extensions.Sort(StringComparer.Ordinal);
            return extensions;
        }

        private static bool HasExtension(
            GL gl,
            IReadOnlyCollection<string> extensions,
            string extensionName)
        {
            if (extensions.Contains(extensionName, StringComparer.Ordinal))
            {
                return true;
            }

            try
            {
                return gl.IsExtensionPresent(extensionName);
            }
            catch
            {
                return false;
            }
        }

        private static string GetString(GL gl, StringName name)
        {
            try
            {
                return gl.GetStringS(name);
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.GetType().Name})";
            }
        }

        private static string TryGetInteger(GL gl, GLEnum name, Func<int, string> formatter)
        {
            try
            {
                return formatter(gl.GetInteger(name));
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.Message})";
            }
        }

        private static string TryGetViewport(GL gl)
        {
            try
            {
                var viewport = stackalloc int[4];
                gl.GetInteger(GLEnum.Viewport, viewport);
                return $"{viewport[0]}, {viewport[1]} — {viewport[2]} x {viewport[3]}";
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.Message})";
            }
        }

        private static string TryGetMaxViewport(GL gl)
        {
            try
            {
                var dimensions = stackalloc int[2];
                gl.GetInteger(GLEnum.MaxViewportDims, dimensions);
                return $"{dimensions[0]:N0} x {dimensions[1]:N0}";
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.Message})";
            }
        }

        private static string TryGetEnabled(GL gl, EnableCap capability)
        {
            try
            {
                return FormatBoolean(gl.IsEnabled(capability));
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.Message})";
            }
        }

        private static string TryGetBoolean(GL gl, GLEnum name)
        {
            try
            {
                return FormatBoolean(gl.GetBoolean(name));
            }
            catch (Exception exception)
            {
                return $"Unavailable ({exception.Message})";
            }
        }

        private static ManaGraphicsDiagnosticSection CaptureSection(
            string name,
            Func<ManaGraphicsDiagnosticSection> capture)
        {
            try
            {
                return capture();
            }
            catch (Exception exception)
            {
                return CreateSection(
                    name,
                    ("Availability", "Some queries are unsupported by this OpenGL context."),
                    ("Error", exception.Message));
            }
        }

        private static int GetInteger(GL gl, uint name)
        {
            return gl.GetInteger((GLEnum)name);
        }

        private static AtiMemoryInfo GetAtiMemory(GL gl, uint name)
        {
            var values = stackalloc int[4];
            gl.GetInteger((GLEnum)name, values);
            return new AtiMemoryInfo(values[0], values[1]);
        }

        private static string DescribeProfile(int profileMask)
        {
            var profiles = new List<string>();
            if ((profileMask & 0x00000001) != 0)
            {
                profiles.Add("Core");
            }

            if ((profileMask & 0x00000002) != 0)
            {
                profiles.Add("Compatibility");
            }

            return profiles.Count == 0 ? $"Unknown (0x{profileMask:X})" : string.Join(", ", profiles);
        }

        private static string DescribeContextFlags(int contextFlags)
        {
            var flags = new List<string>();
            if ((contextFlags & 0x00000001) != 0) flags.Add("Forward compatible");
            if ((contextFlags & 0x00000002) != 0) flags.Add("Debug");
            if ((contextFlags & 0x00000004) != 0) flags.Add("Robust access");
            if ((contextFlags & 0x00000008) != 0) flags.Add("No error");
            return flags.Count == 0 ? "None" : string.Join(", ", flags);
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
                    ManaGraphicsDiagnosticScope.OpenGl)).ToArray());
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "Enabled" : "Disabled";
        }

        private static string FormatKibibytes(long kibibytes)
        {
            return FormatBytes((ulong)Math.Max(0, kibibytes) * 1024UL);
        }

        private static string FormatBytes(ulong bytes)
        {
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

        private readonly record struct AtiMemoryInfo(int TotalFree, int LargestBlock);
    }
}
