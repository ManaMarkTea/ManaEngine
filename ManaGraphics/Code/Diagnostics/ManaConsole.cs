using System.Runtime.InteropServices;
using ManaCore.Diagnostics;

namespace ManaGraphics.Code.Diagnostics
{
    /// <summary>
    /// Console-window attach/allocate behavior for this app, on top of the shared ManaLog logger (see
    /// ManaCore.Diagnostics.ManaLog -- lives in ManaCore so it's reachable from every project, not just
    /// ones that can reference ManaGraphics).
    /// </summary>
    public static class ManaConsole
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;

        public static string DiagnosticLogPath => ManaLog.DiagnosticLogPath;

        public static void AttachForDiagnostics(string title)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!AttachConsole(AttachParentProcess))
                {
                    AllocConsole();
                    Console.Title = title;
                }

                ReopenStandardStreams();
            }

            WriteDiagnostic($"Attached diagnostics console. Log: {DiagnosticLogPath}");
        }

        public static void WriteDiagnostic(string message)
        {
            ManaLog.WriteDiagnostic(message);
        }

        public static void WriteDiagnostic(Exception exception)
        {
            ManaLog.WriteDiagnostic(exception);
        }

        /// <summary>See <see cref="ManaLog.WriteDiagnosticElapsed"/>. Startup-timeline milestones.</summary>
        public static void WriteDiagnosticElapsed(string label)
        {
            ManaLog.WriteDiagnosticElapsed(label);
        }

        public static void Shutdown()
        {
            ManaLog.Shutdown();
        }

        private static void ReopenStandardStreams()
        {
            var output = CreateWriter(Console.OpenStandardOutput());
            var error = CreateWriter(Console.OpenStandardError());
            Console.SetOut(output);
            Console.SetError(error);
        }

        private static StreamWriter CreateWriter(Stream stream)
        {
            return new StreamWriter(stream)
            {
                AutoFlush = true
            };
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
    }
}
