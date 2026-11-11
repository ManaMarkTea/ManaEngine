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
    /// its own console-window-attach behavior on top. Everything else (Mana, ManaGeoTools, ManaEditor)
    /// can use this directly.
    /// </summary>
    public static class ManaLog
    {
        private const int AsyncBufferSize = 8_192;
        private static readonly object LoggerLock = new();
        private static readonly LoggingLevelSwitch LevelSwitch = new(ParseMinimumLevel());
        private static Logger? _logger;

        public static string DiagnosticLogPath { get; } = Path.Combine(FindLogFolder(), "ManaGraphicsStartup.log");

        // MANA_LOG_ROOT: where an app whose working folder is read-only (a phone's bundle) keeps its log.
        private static string FindLogFolder()
        {
            if (Environment.GetEnvironmentVariable("MANA_LOG_ROOT") is { Length: > 0 } root)
            {
                return Path.Combine(root, "Artifacts", "Logs");
            }
            if (FindCheckout() is { } checkout)
            {
                return Path.Combine(checkout, "Artifacts", "Logs");
            }

            // An installed app logs in its own per-user folder, never the folder it was started in: opening a file
            // starts it in that file's folder, which would fill with Artifacts/Logs.
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (appData.Length > 0)
            {
                var app = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Mana";
                return Path.Combine(appData, app, "Logs");
            }
            return Path.Combine(Environment.CurrentDirectory, "Artifacts", "Logs");
        }

        private static string? FindCheckout()
        {
            // Resolve the checkout even when dotnet run or the debugger starts in a project folder.
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                        File.Exists(Path.Combine(directory.FullName, "ManaRenderer.sln")))
                    {
                        return directory.FullName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Set the moment this type is first touched, which for every entry point is at or near the very
        /// top of Main. Every startup-timeline message below is measured against this, not against its own
        /// previous call, so the numbers describe "how long since launch" rather than requiring the reader
        /// to add up a column of deltas by hand.
        /// </summary>
        private static readonly long AppStartTimestamp = Stopwatch.GetTimestamp();

        public static void WriteDiagnostic(string message)
        {
            try
            {
                GetLogger().Debug("{DiagnosticMessage}", message);
            }
            catch (Exception exception)
            {
                // Logging must never take down the renderer -- but a Serilog failure that silently
                // eats every message (including real GPU/asset errors routed through this method) is
                // itself worth seeing, so fall back to the one thing that can't also fail this way.
                FallbackWrite(message, exception);
            }
        }

        public static void WriteDiagnostic(Exception exception)
        {
            try
            {
                GetLogger().Error(exception, "Graphics diagnostic exception");
            }
            catch (Exception loggingException)
            {
                FallbackWrite(exception.ToString(), loggingException);
            }
        }

        private static void FallbackWrite(string message, Exception loggingException)
        {
            try
            {
                Console.WriteLine($"[ManaLog fallback, logger threw {loggingException.GetType().Name}] {message}");
            }
            catch
            {
                // Truly nothing left to fall back to.
            }
        }

        /// <summary>
        /// Writes "{label} sinceLaunchMs=..." at Debug level -- visible in the diagnostics console by
        /// default, unlike WriteVerbose. For startup-timeline milestones specifically: which stage of
        /// bringing up a window and a graphics device is actually eating the time before the first frame,
        /// something no amount of reading the code answers on its own since most of the candidates are
        /// native driver calls.
        /// </summary>
        public static void WriteDiagnosticElapsed(string label)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(AppStartTimestamp).TotalMilliseconds;
            WriteDiagnostic($"{label} sinceLaunchMs={elapsedMs:F0}");
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
#if MANA_BROWSER
            // Async's drain loop is a real background Thread, which doesn't exist on single-threaded
            // browser-wasm -- every message (including GPU uncaptured errors routed through
            // WriteDiagnostic) was being enqueued and never drained, so the browser console showed
            // nothing at all regardless of what actually went wrong. Console output has to go out
            // synchronously here instead; there's also no meaningful place to put DiagnosticLogPath
            // (a desktop LocalApplicationData-style path) on the wasm virtual filesystem, so the file
            // sink is dropped rather than silently failing on every write.
            return new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                .WriteTo.Sink(new BrowserConsoleSink())
                .CreateLogger();
#else
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
#endif
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

#if MANA_BROWSER
        /// <summary>
        /// Serilog's own Console sink produced no output at all on browser-wasm -- no exception, just
        /// silence, even after Async (the actual known-broken piece) was removed -- so this writes
        /// straight to Console.WriteLine instead, the one output path already proven throughout this
        /// port to reach the browser's dev console reliably.
        /// </summary>
        private sealed class BrowserConsoleSink : ILogEventSink
        {
            private static readonly Serilog.Formatting.Display.MessageTemplateTextFormatter Formatter = new(
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}");

            public void Emit(LogEvent logEvent)
            {
                using var writer = new StringWriter();
                Formatter.Format(logEvent, writer);
                Console.Write(writer.ToString());
            }
        }
#endif
    }
}
