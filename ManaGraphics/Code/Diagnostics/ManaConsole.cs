using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ManaGraphics.Code.Diagnostics
{
    public static class ManaConsole
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;
        private const int AsyncBufferSize = 8_192;
        private static readonly object LoggerLock = new();
        private static Logger? _logger;

        public static string DiagnosticLogPath { get; } =
            Path.Combine(Environment.CurrentDirectory, "ManaGraphicsStartup.log");

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
            try
            {
                GetLogger().Debug("{DiagnosticMessage}", message);
            }
            catch
            {
                // Logging must never take down the renderer.
            }
        }

        public static void WriteDiagnostic(Exception exception)
        {
            try
            {
                GetLogger().Error(exception, "Graphics diagnostic exception");
            }
            catch
            {
                // Logging must never take down the renderer.
            }
        }

        public static void Shutdown()
        {
            Logger? logger;
            lock (LoggerLock)
            {
                logger = _logger;
                _logger = null;
            }

            logger?.Dispose();
        }

        private static Logger GetLogger()
        {
            lock (LoggerLock)
            {
                return _logger ??= CreateLogger();
            }
        }

        private static Logger CreateLogger()
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(
                    sinks =>
                    {
                        sinks.Console(
                            restrictedToMinimumLevel: LogEventLevel.Debug,
                            outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                        sinks.File(
                            DiagnosticLogPath,
                            restrictedToMinimumLevel: LogEventLevel.Debug,
                            outputTemplate: "[{Timestamp:O} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                            buffered: true,
                            flushToDiskInterval: TimeSpan.FromSeconds(1),
                            fileSizeLimitBytes: 10 * 1024 * 1024,
                            rollOnFileSizeLimit: true,
                            retainedFileCountLimit: 3);
                    },
                    bufferSize: AsyncBufferSize,
                    blockWhenFull: false)
                .CreateLogger();
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
