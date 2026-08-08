using System.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ManaCore.Diagnostics
{
    /// <summary>
    /// Shared Serilog logger, reachable from every project (lives in ManaCore, which nothing else
    /// depends on) -- ManaGraphics.Code.Diagnostics.ManaConsole forwards its existing
    /// WriteDiagnostic/Shutdown/DiagnosticLogPath API here so both share one logger/log file, and adds
    /// its own console-window-attach behavior on top. Everything else (Mana, ManaGeoTools, ManaVoxel)
    /// can use this directly.
    /// </summary>
    public static class ManaLog
    {
        private const int AsyncBufferSize = 8_192;
        private static readonly object LoggerLock = new();
        private static readonly LoggingLevelSwitch LevelSwitch = new(ParseMinimumLevel());
        private static Logger? _logger;

        public static string DiagnosticLogPath { get; } =
            Path.Combine(Environment.CurrentDirectory, "ManaGraphicsStartup.log");

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

        /// <summary>
        /// For one-off/perf-investigation logging that would otherwise mean throwaway Console.WriteLine +
        /// Stopwatch boilerplate at the call site. Off by default (Verbose sits below the default Debug
        /// minimum level) -- set the MANA_LOG_LEVEL environment variable to "Verbose" to see these
        /// without touching code. See also TimeVerbose for timing a block of code in one line.
        /// </summary>
        public static void WriteVerbose(string message)
        {
            try
            {
                GetLogger().Verbose("{DiagnosticMessage}", message);
            }
            catch
            {
                // Logging must never take down the renderer.
            }
        }

        /// <summary>
        /// Times the caller's scope and writes "{label} tookMs=..." at Verbose level when disposed --
        /// use as `using var _ = ManaLog.TimeVerbose("SplitTiming child=3");` instead of hand-rolling a
        /// Stopwatch. Returns a ref struct rather than IDisposable so `using` disposal (pattern-matched
        /// on Dispose(), not the interface) costs zero heap allocation.
        ///
        /// Being a ref struct, it CANNOT be held alive across an `await` -- the compiler will reject
        /// `using (ManaLog.TimeVerbose(...)) { await ...; }`. For a span that needs to cover an awaited
        /// call, use GetTimestamp()/WriteVerboseElapsed() instead (see their doc comments).
        /// </summary>
        public static ScopedTimer TimeVerbose(string label)
        {
            return new ScopedTimer(label);
        }

        /// <summary>Pairs with WriteVerboseElapsed -- see TimeVerbose's doc comment for why this exists.</summary>
        public static long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Writes "{label} tookMs=..." at Verbose level, same as TimeVerbose's scope would on Dispose --
        /// for spans that cross an `await` and so can't use the ref-struct TimeVerbose scope:
        /// <code>
        /// var start = ManaLog.GetTimestamp();
        /// var result = await SomethingAsync();
        /// ManaLog.WriteVerboseElapsed("label", start);
        /// </code>
        /// </summary>
        public static void WriteVerboseElapsed(string label, long startTimestamp)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            WriteVerbose($"{label} tookMs={elapsedMs:F1}");
        }

        public static void WriteVerboseElapsedSignificant(string label, long startTimestamp)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            if (elapsedMs >= 16.0)
            {
                WriteVerbose($"{label} tookMs={elapsedMs:F1}");
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
                .MinimumLevel.ControlledBy(LevelSwitch)
                .WriteTo.Async(
                    sinks =>
                    {
                        sinks.Console(
                            restrictedToMinimumLevel: LogEventLevel.Verbose,
                            outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                        sinks.File(
                            DiagnosticLogPath,
                            restrictedToMinimumLevel: LogEventLevel.Verbose,
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

        private static LogEventLevel ParseMinimumLevel()
        {
            var env = Environment.GetEnvironmentVariable("MANA_LOG_LEVEL");
            return Enum.TryParse<LogEventLevel>(env, ignoreCase: true, out var level) ? level : LogEventLevel.Debug;
        }

        /// <summary>See TimeVerbose. A ref struct: stack-only, no heap allocation per timed scope.</summary>
        public readonly ref struct ScopedTimer
        {
            private readonly string _label;
            private readonly long _startTimestamp;

            internal ScopedTimer(string label)
            {
                _label = label;
                _startTimestamp = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                WriteVerboseElapsed(_label, _startTimestamp);
            }
        }
    }
}
