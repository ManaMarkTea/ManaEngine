using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ManaGraphics.Code.Diagnostics
{
    public static class ManaConsole
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;
        private static readonly object LogLock = new object();

        public static string DiagnosticLogPath { get; } =
            Path.Combine(Environment.CurrentDirectory, "ManaGraphicsStartup.log");

        public static void AttachForDiagnostics(string title)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            if (!AttachConsole(AttachParentProcess))
            {
                AllocConsole();
                Console.Title = title;
            }

            ReopenStandardStreams();
            WriteDiagnostic($"Attached diagnostics console. Log: {DiagnosticLogPath}");
        }

        public static void WriteDiagnostic(string message)
        {
            var line = $"[{DateTime.Now:O}] {message}";

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // Console output is best-effort for WinExe launches.
            }

            lock (LogLock)
            {
                File.AppendAllText(DiagnosticLogPath, line + Environment.NewLine);
            }
        }

        public static void WriteDiagnostic(Exception exception)
        {
            WriteDiagnostic(exception.ToString());
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
