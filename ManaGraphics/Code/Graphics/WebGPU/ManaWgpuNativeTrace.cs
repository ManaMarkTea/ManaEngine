#if !MANA_BROWSER
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// wgpu-native's own trace log routed into the app's (MANA_WGPU_NATIVE_TRACE=1). Opt-in only: Trace is the noisiest
    /// level across wgpu_core/wgpu_hal/wgpu, and the callback fires synchronously through ManaLog's single lock on
    /// whichever native thread logs. With several threads submitting at once (planet splits dispatching compute while
    /// the main thread renders) that traffic made the app appear to hang at the next present -- the cause of an
    /// intermittent "deadlock on patch click". For deep driver debugging only.
    /// </summary>
    internal static class ManaWgpuNativeTrace
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LogCallback(LogLevel level, string message, nint userdata);

        private enum LogLevel : int
        {
            Error = 1,
            Warn = 2,
            Info = 3,
            Debug = 4,
            Trace = 5,
        }

        [DllImport("wgpu_native", EntryPoint = "wgpuSetLogLevel", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void SetLogLevel(LogLevel level);

        [DllImport("wgpu_native", EntryPoint = "wgpuSetLogCallback", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void SetLogCallback(LogCallback callback, nint userdata);

        // Kept alive for the process: native code holds the function pointer.
        private static readonly LogCallback Callback = OnLog;

        public static void EnableIfRequested()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MANA_WGPU_NATIVE_TRACE")))
            {
                return;
            }

            try
            {
                Environment.SetEnvironmentVariable("RUST_LOG", "wgpu_core=trace,wgpu_hal=trace,wgpu=trace");
                Environment.SetEnvironmentVariable("RUST_BACKTRACE", "1");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Failed to configure Rust tracing environment variables: {ex.Message}");
            }

            try
            {
                SetLogLevel(LogLevel.Trace);
                SetLogCallback(Callback, IntPtr.Zero);
                ManaConsole.WriteDiagnostic("[WebGPU] Enabled native backend tracing.");
            }
            catch (DllNotFoundException ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Native wgpu tracing library is unavailable: {ex.Message}");
            }
            catch (EntryPointNotFoundException ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Native wgpu tracing entry points are unavailable: {ex.Message}");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"[WebGPU] Failed to enable native backend tracing: {ex.Message}");
            }
        }

        private static void OnLog(LogLevel level, string message, nint userdata)
        {
            var levelName = level switch
            {
                LogLevel.Error => "error",
                LogLevel.Warn => "warn",
                LogLevel.Info => "info",
                LogLevel.Debug => "debug",
                _ => "trace"
            };

            ManaConsole.WriteDiagnostic($"[wgpu:{levelName}] {message}");
        }
    }
}
#endif
