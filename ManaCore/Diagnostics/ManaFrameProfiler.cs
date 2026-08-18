using System.Diagnostics;

namespace ManaCore.Diagnostics
{
    /// <summary>
    /// Per-frame scope timings with a rolling peak, plus a hitch log.
    /// <para>
    /// Built for the one question a live "this frame" readout cannot answer: a hitch is by definition over
    /// by the time anyone looks at the panel, and the frame on screen is a good one. So every scope keeps
    /// the last <see cref="HistoryFrames"/> frames and reports its PEAK alongside its current value -- the
    /// worst spike in the last couple of seconds is still on screen a second later. Anything whose peak is
    /// far above its average is a stutter source even if its average looks harmless.
    /// </para>
    /// <para>
    /// GC counts and allocation volume are sampled per frame for the same reason: a hitch where no scope
    /// stands out is usually a collection, and that is invisible to wall-clock timers alone.
    /// </para>
    /// <para>
    /// Lives in ManaCore so every project can instrument itself. Timing is a timestamp pair and a
    /// dictionary lookup under a lock -- worth roughly nothing next to anything worth measuring, and the
    /// lock means a background thread can time its own work without corrupting the table.
    /// </para>
    /// </summary>
    public static class ManaFrameProfiler
    {
        /// <summary>Frames of history per scope. 120 is about two seconds -- long enough that a spike survives being looked at.</summary>
        public const int HistoryFrames = 120;

        /// <summary>Frames a hitch report waits before it may fire again, so a bad patch of frames can't flood the log (and make itself worse).</summary>
        private const int HitchLogCooldownFrames = 30;

        private const double BytesPerMegabyte = 1024.0 * 1024.0;

        private static readonly object Gate = new();
        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

        private static string[] _names = Array.Empty<string>();
        private static bool _namesStale = true;

        private static int _cursor;
        private static long _frameIndex;
        // Negative-but-small, NOT long.MinValue: the cooldown test below is a subtraction, and a MinValue
        // sentinel overflows unchecked long arithmetic into a huge negative number that fails the test
        // forever -- so no hitch would ever be reported. (Same trap as the tick-count sentinel in
        // VoxelEditorGameMode.LogMemoryStatsPeriodically.) This value also lets the very first hitch report
        // immediately rather than waiting out a cooldown that nothing has used yet.
        private static long _lastHitchFrame = -HitchLogCooldownFrames;

        private static long _lastAllocatedBytes;
        private static int _lastGen0;
        private static int _lastGen1;
        private static int _lastGen2;

        /// <summary>
        /// Master switch. Off means <see cref="Measure"/> hands back a scope that does nothing on dispose,
        /// so instrumented code doesn't need its own guards.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// A frame at or over this is reported to the log with its breakdown. Defaults to the 60fps budget
        /// plus a little slack -- a vsynced frame lands near 16.7ms, so 20 catches dropped frames without
        /// reporting every ordinary one. Override with MANA_HITCH_MS to tighten it without a rebuild.
        /// </summary>
        public static double HitchThresholdMs { get; set; } = ParseHitchThresholdMs();

        private static double ParseHitchThresholdMs()
        {
            var configured = Environment.GetEnvironmentVariable("MANA_HITCH_MS");
            return double.TryParse(configured, out var milliseconds) && milliseconds > 0.0
                ? milliseconds
                : 20.0;
        }

        /// <summary>Frames over <see cref="HitchThresholdMs"/> since the last <see cref="Reset"/>.</summary>
        public static int HitchCount { get; private set; }

        /// <summary>Worst frame seen since the last <see cref="Reset"/>, in milliseconds.</summary>
        public static double WorstFrameMs { get; private set; }

        /// <summary>Frames measured since the last <see cref="Reset"/>.</summary>
        public static long FrameCount { get; private set; }

        /// <summary>Last frame's GC collection counts, as deltas -- what collected DURING that frame, not since process start.</summary>
        public static (int Gen0, int Gen1, int Gen2) LastGcDelta { get; private set; }

        /// <summary>Megabytes allocated during the last frame. High and steady is a GC hitch waiting to happen.</summary>
        public static double LastAllocatedMb { get; private set; }

        /// <summary>Peak of <see cref="LastAllocatedMb"/> over the history window.</summary>
        public static double PeakAllocatedMb { get; private set; }

        /// <summary>One scope's rolling numbers. Milliseconds throughout.</summary>
        public readonly record struct Sample(double LastMs, double PeakMs, double AverageMs, int LastCalls)
        {
            /// <summary>Peak over average -- how spiky this scope is. 1 means perfectly steady; large means it stutters.</summary>
            public double Spikiness => AverageMs > 0.0001 ? PeakMs / AverageMs : 0.0;
        }

        /// <summary>
        /// Times a block. Reentrant and additive: several calls in one frame sum into that frame's total for
        /// the name, with the call count kept alongside so "10ms" and "10ms across 400 calls" read differently.
        /// </summary>
        public static Scope Measure(string name)
        {
            return Enabled ? new Scope(name) : default;
        }

        /// <summary>Records a duration measured elsewhere (a backend's own stopwatch, say) as if it were a scope.</summary>
        public static void Record(string name, double milliseconds)
        {
            if (!Enabled)
            {
                return;
            }

            lock (Gate)
            {
                GetEntryLocked(name).Accumulate(milliseconds);
            }
        }

        /// <summary>
        /// Closes the frame: rolls every scope's total into history, samples GC, and reports the breakdown if
        /// the frame was a hitch. Call once per frame with the frame's own wall-clock delta -- passing the
        /// engine's delta rather than measuring internally keeps present/vsync accounted the same way the fps
        /// readout accounts for it.
        /// </summary>
        public static void EndFrame(double frameMs)
        {
            if (!Enabled)
            {
                return;
            }

            string? hitchReport = null;

            lock (Gate)
            {
                SampleGcLocked();

                _frameIndex++;
                FrameCount++;
                if (frameMs > WorstFrameMs)
                {
                    WorstFrameMs = frameMs;
                }

                var isHitch = frameMs >= HitchThresholdMs;
                if (isHitch)
                {
                    HitchCount++;
                }

                // Built before the cursor advances, so the report describes the frame that just ended.
                if (isHitch && _frameIndex - _lastHitchFrame >= HitchLogCooldownFrames)
                {
                    _lastHitchFrame = _frameIndex;
                    hitchReport = BuildHitchReportLocked(frameMs);
                }

                foreach (var entry in Entries.Values)
                {
                    entry.CloseFrame(_cursor);
                }

                _cursor = (_cursor + 1) % HistoryFrames;
            }

            // Debug level, not Verbose: a hitch report is the whole point of this type, and having to set
            // MANA_LOG_LEVEL before a stutter would report itself is one step too many.
            if (hitchReport != null)
            {
                ManaLog.WriteDiagnostic(hitchReport);
            }
        }

        /// <summary>Scope names, sorted, cached until the set itself changes -- suits a panel that rebuilds rows on key-set identity.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                lock (Gate)
                {
                    if (_namesStale)
                    {
                        var names = new string[Entries.Count];
                        Entries.Keys.CopyTo(names, 0);
                        Array.Sort(names, StringComparer.Ordinal);
                        _names = names;
                        _namesStale = false;
                    }

                    return _names;
                }
            }
        }

        public static bool TryGetSample(string name, out Sample sample)
        {
            lock (Gate)
            {
                if (!Entries.TryGetValue(name, out var entry))
                {
                    sample = default;
                    return false;
                }

                sample = entry.ToSample();
                return true;
            }
        }

        /// <summary>Clears history and the hitch/worst-frame counters. For a "start watching from now" button.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Entries.Clear();
                _namesStale = true;
                _cursor = 0;
                HitchCount = 0;
                WorstFrameMs = 0.0;
                FrameCount = 0;
                PeakAllocatedMb = 0.0;
                _lastHitchFrame = -HitchLogCooldownFrames;
            }
        }

        /// <summary>The worst scopes over the history window, peak first -- the "what should I look at" list.</summary>
        public static List<(string Name, Sample Sample)> TopByPeak(int count)
        {
            lock (Gate)
            {
                return TopByPeakLocked(count);
            }
        }

        private static List<(string Name, Sample Sample)> TopByPeakLocked(int count)
        {
            var samples = new List<(string Name, Sample Sample)>(Entries.Count);
            foreach (var (name, entry) in Entries)
            {
                samples.Add((name, entry.ToSample()));
            }

            samples.Sort(static (left, right) => right.Sample.PeakMs.CompareTo(left.Sample.PeakMs));
            if (samples.Count > count)
            {
                samples.RemoveRange(count, samples.Count - count);
            }

            return samples;
        }

        // Ranked by THIS frame's cost, not by peak: the question a hitch report answers is what made this
        // particular frame slow.
        private static string BuildHitchReportLocked(double frameMs)
        {
            var worst = new List<(string Name, double Ms, int Calls)>(Entries.Count);
            foreach (var (name, entry) in Entries)
            {
                if (entry.FrameMs >= 0.05)
                {
                    worst.Add((name, entry.FrameMs, entry.FrameCalls));
                }
            }

            worst.Sort(static (left, right) => right.Ms.CompareTo(left.Ms));

            var report = new System.Text.StringBuilder();
            report.Append($"[Hitch] frame={frameMs:F1}ms (budget {HitchThresholdMs:F0}ms)");
            report.Append($" alloc={LastAllocatedMb:F2}MB gc={LastGcDelta.Gen0}/{LastGcDelta.Gen1}/{LastGcDelta.Gen2}");
            report.Append(" |");

            if (worst.Count == 0)
            {
                // No scope owns it: the cost is outside every timer -- a GC pause, a driver stall, or an
                // uninstrumented path. The GC numbers above are the first place to look.
                report.Append(" no instrumented scope over 0.05ms");
                return report.ToString();
            }

            var reported = Math.Min(worst.Count, 8);
            for (var i = 0; i < reported; i++)
            {
                var (name, ms, calls) = worst[i];
                report.Append($" {name}={ms:F1}ms");
                if (calls > 1)
                {
                    report.Append($"(x{calls})");
                }
            }

            return report.ToString();
        }

        private static void SampleGcLocked()
        {
            var allocated = GC.GetTotalAllocatedBytes(precise: false);
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            // First frame has nothing to diff against, and would otherwise report the whole startup's
            // allocations as this frame's.
            if (_lastAllocatedBytes != 0)
            {
                LastAllocatedMb = Math.Max(0, allocated - _lastAllocatedBytes) / BytesPerMegabyte;
                LastGcDelta = (gen0 - _lastGen0, gen1 - _lastGen1, gen2 - _lastGen2);
                if (LastAllocatedMb > PeakAllocatedMb)
                {
                    PeakAllocatedMb = LastAllocatedMb;
                }
            }

            _lastAllocatedBytes = allocated;
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;
        }

        private static Entry GetEntryLocked(string name)
        {
            if (Entries.TryGetValue(name, out var entry))
            {
                return entry;
            }

            entry = new Entry();
            Entries[name] = entry;
            _namesStale = true;
            return entry;
        }

        /// <summary>
        /// A <c>using</c> scope. A struct with no allocation, and harmless when default-constructed (which is
        /// what <see cref="Measure"/> returns while disabled).
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            private readonly string? _name;
            private readonly long _startTimestamp;

            internal Scope(string name)
            {
                _name = name;
                _startTimestamp = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_name == null)
                {
                    return;
                }

                var elapsed = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
                lock (Gate)
                {
                    GetEntryLocked(_name).Accumulate(elapsed);
                }
            }
        }

        private sealed class Entry
        {
            private readonly double[] _history = new double[HistoryFrames];
            private int _filled;

            /// <summary>Accumulating total for the frame in progress.</summary>
            public double FrameMs { get; private set; }

            public int FrameCalls { get; private set; }

            private double LastMs { get; set; }
            private int LastCalls { get; set; }

            public void Accumulate(double milliseconds)
            {
                FrameMs += milliseconds;
                FrameCalls++;
            }

            public void CloseFrame(int cursor)
            {
                _history[cursor] = FrameMs;
                if (_filled < HistoryFrames)
                {
                    _filled++;
                }

                LastMs = FrameMs;
                LastCalls = FrameCalls;
                FrameMs = 0.0;
                FrameCalls = 0;
            }

            public Sample ToSample()
            {
                var peak = 0.0;
                var total = 0.0;
                for (var i = 0; i < _filled; i++)
                {
                    var value = _history[i];
                    total += value;
                    if (value > peak)
                    {
                        peak = value;
                    }
                }

                var average = _filled > 0 ? total / _filled : 0.0;
                return new Sample(LastMs, peak, average, LastCalls);
            }
        }
    }
}
