using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Reads the operating system's keyboard auto-repeat settings so ManaUI's synthesised repeat matches
    /// what the user has configured everywhere else.
    /// <para>
    /// This lives in the engine layer, not in ManaUI: the framework has no OS dependency by design, so it
    /// exposes the two timings as properties and the host fills them in -- the same arrangement as fonts,
    /// the clipboard and the cursor.
    /// </para>
    /// </summary>
    public static partial class ManaUiKeyboardSettings
    {
        private const uint SpiGetKeyboardDelay = 0x0016;
        private const uint SpiGetKeyboardSpeed = 0x000A;

        /// <summary>Used when the OS cannot be asked. Deliberately on the snappy side of typical.</summary>
        public const double FallbackDelaySeconds = 0.35;

        public const double FallbackIntervalSeconds = 0.03;

        /// <summary>
        /// The current settings, or the fallbacks. Never throws -- a UI that cannot start because a
        /// keyboard preference could not be read would be a poor trade.
        /// </summary>
        public static (double DelaySeconds, double IntervalSeconds) Read()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return ReadWindows();
                }

                if (OperatingSystem.IsMacOS())
                {
                    return ReadMacOs();
                }
            }
            catch
            {
                // Any failure falls through to the defaults below.
            }

            return (FallbackDelaySeconds, FallbackIntervalSeconds);
        }

        // ----- Windows --------------------------------------------------------------------------------

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SystemParametersInfoW(uint action, uint param, ref int value, uint winIni);

        private static (double, double) ReadWindows()
        {
            var delayIndex = 1;
            var speedIndex = 31;

            if (!SystemParametersInfoW(SpiGetKeyboardDelay, 0, ref delayIndex, 0))
            {
                delayIndex = 1;
            }

            if (!SystemParametersInfoW(SpiGetKeyboardSpeed, 0, ref speedIndex, 0))
            {
                speedIndex = 31;
            }

            return (DelayFromWindowsIndex(delayIndex), IntervalFromWindowsSpeed(speedIndex));
        }

        /// <summary>Windows reports the delay as 0-3, meaning roughly 250ms to 1000ms in 250ms steps.</summary>
        public static double DelayFromWindowsIndex(int index)
        {
            return Clamp((Math.Clamp(index, 0, 3) + 1) * 0.25, 0.1, 2.0);
        }

        /// <summary>
        /// Windows reports the repeat rate as 0-31, meaning roughly 2.5 to 30 repeats per second.
        /// </summary>
        public static double IntervalFromWindowsSpeed(int speed)
        {
            var repeatsPerSecond = 2.5 + Math.Clamp(speed, 0, 31) / 31.0 * (30.0 - 2.5);
            return Clamp(1.0 / repeatsPerSecond, 0.01, 1.0);
        }

        // ----- macOS ----------------------------------------------------------------------------------

        /// <summary>
        /// macOS stores both values in global preferences, counted in 1/60s ticks. Read through `defaults`
        /// rather than a native API so this stays free of an AppKit dependency; it runs once at startup.
        /// </summary>
        [SupportedOSPlatform("macos")]
        private static (double, double) ReadMacOs()
        {
            var initial = ReadMacOsDefault("InitialKeyRepeat");
            var repeat = ReadMacOsDefault("KeyRepeat");

            var delay = initial.HasValue
                ? Clamp(TicksToSeconds(initial.Value), 0.1, 2.0)
                : FallbackDelaySeconds;

            var interval = repeat.HasValue
                ? Clamp(TicksToSeconds(repeat.Value), 0.01, 1.0)
                : FallbackIntervalSeconds;

            return (delay, interval);
        }

        /// <summary>macOS counts these in 1/60 second ticks.</summary>
        public static double TicksToSeconds(double ticks)
        {
            return ticks / 60.0;
        }

        [SupportedOSPlatform("macos")]
        private static double? ReadMacOsDefault(string key)
        {
            using var process = Process.Start(new ProcessStartInfo("defaults", $"read -g {key}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();

            // Bounded wait: an unresponsive `defaults` must not hold up startup.
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Already gone.
                }

                return null;
            }

            // The key is simply absent when the user has never changed it from the default.
            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static double Clamp(double value, double min, double max)
        {
            return double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
        }
    }
}
