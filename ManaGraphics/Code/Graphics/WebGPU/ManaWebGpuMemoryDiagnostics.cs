using ManaCore.Graphics;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    // OS memory telemetry does not own or mutate renderer resources.
    internal static unsafe class ManaWebGpuMemoryDiagnostics
    {
        internal static ManaGraphicsDiagnosticSection CreateVideoMemorySection(AdapterProperties properties)
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

        internal static string ReadDiagnosticString(byte* value)
        {
            var text = (value == null ? string.Empty : Marshal.PtrToStringUTF8((nint)value) ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
        }

        internal static string FormatBytes(ulong bytes)
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

        internal static void LogVideoMemoryDiagnostics(AdapterProperties properties)
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

