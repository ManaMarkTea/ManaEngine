using ManaCore.Diagnostics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The profiler behind the hitch hunt. Checked headlessly because all of it is arithmetic over a ring
/// buffer -- no window, no GPU, and no need for a real frame to have happened.
/// <para>
/// The check that matters most is the peak surviving good frames afterwards: that property is the entire
/// reason this type exists instead of a live per-frame readout, and it is the one a refactor would quietly
/// break.
/// </para>
/// </summary>
public static class FrameProfilerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // Static state shared with whatever else has run, so every section starts from a known slate.
        ManaFrameProfiler.Reset();
        ManaFrameProfiler.HitchThresholdMs = 20.0;

        // ---------------------------------------------------------------- a scope records
        ManaFrameProfiler.Record("test.scope", 4.0);
        ManaFrameProfiler.EndFrame(frameMs: 8.0);

        var found = ManaFrameProfiler.TryGetSample("test.scope", out var sample);
        Check("a recorded scope reports its duration", found && Math.Abs(sample.LastMs - 4.0) < 0.001,
            $"{sample.LastMs:F2}ms");

        Check("and counts the frame", ManaFrameProfiler.FrameCount == 1, $"{ManaFrameProfiler.FrameCount}");

        // ---------------------------------------------------------------- calls in one frame sum
        ManaFrameProfiler.Record("test.scope", 1.0);
        ManaFrameProfiler.Record("test.scope", 2.0);
        ManaFrameProfiler.EndFrame(frameMs: 8.0);

        ManaFrameProfiler.TryGetSample("test.scope", out sample);
        Check("repeat calls in one frame sum", Math.Abs(sample.LastMs - 3.0) < 0.001, $"{sample.LastMs:F2}ms");
        Check("and the call count says how many", sample.LastCalls == 2, $"x{sample.LastCalls}");

        // ---------------------------------------------------------------- the whole point
        // A spike, then a run of good frames on top of it. A live readout would show 0.5ms and nothing
        // else; the peak has to still be reporting the spike, or a hitch can never be seen after the fact.
        ManaFrameProfiler.Record("test.spike", 45.0);
        ManaFrameProfiler.EndFrame(frameMs: 50.0);
        for (var i = 0; i < 30; i++)
        {
            ManaFrameProfiler.Record("test.spike", 0.5);
            ManaFrameProfiler.EndFrame(frameMs: 8.0);
        }

        ManaFrameProfiler.TryGetSample("test.spike", out var spike);
        Check("the peak survives 30 good frames after the spike",
            Math.Abs(spike.PeakMs - 45.0) < 0.001, $"peak {spike.PeakMs:F1}ms");
        Check("while the current value has moved on",
            Math.Abs(spike.LastMs - 0.5) < 0.001, $"now {spike.LastMs:F1}ms");
        Check("and the average sits between the two",
            spike.AverageMs > 0.5 && spike.AverageMs < 45.0, $"avg {spike.AverageMs:F2}ms");
        Check("spikiness flags it as a stutter source", spike.Spikiness > 10.0, $"{spike.Spikiness:F0}x");

        // ---------------------------------------------------------------- the peak eventually expires
        // Bounded history is deliberate: a peak from a minute ago would make the panel unreadable, so it
        // has to age out once the window has rolled entirely past it.
        for (var i = 0; i < ManaFrameProfiler.HistoryFrames + 1; i++)
        {
            ManaFrameProfiler.Record("test.spike", 0.5);
            ManaFrameProfiler.EndFrame(frameMs: 8.0);
        }

        ManaFrameProfiler.TryGetSample("test.spike", out spike);
        Check("but ages out once the window rolls past it",
            Math.Abs(spike.PeakMs - 0.5) < 0.001, $"peak {spike.PeakMs:F1}ms");

        // ---------------------------------------------------------------- hitch counting
        ManaFrameProfiler.Reset();
        ManaFrameProfiler.EndFrame(frameMs: 8.0);
        ManaFrameProfiler.EndFrame(frameMs: 16.0);
        Check("frames inside the budget are not hitches", ManaFrameProfiler.HitchCount == 0);

        ManaFrameProfiler.EndFrame(frameMs: 33.0);
        ManaFrameProfiler.EndFrame(frameMs: 21.0);
        Check("frames over the budget are", ManaFrameProfiler.HitchCount == 2, $"{ManaFrameProfiler.HitchCount}");
        Check("and the worst one is kept",
            Math.Abs(ManaFrameProfiler.WorstFrameMs - 33.0) < 0.001, $"{ManaFrameProfiler.WorstFrameMs:F1}ms");

        // ---------------------------------------------------------------- ranking
        ManaFrameProfiler.Reset();
        ManaFrameProfiler.Record("test.small", 1.0);
        ManaFrameProfiler.Record("test.big", 12.0);
        ManaFrameProfiler.Record("test.middle", 5.0);
        ManaFrameProfiler.EndFrame(frameMs: 20.0);

        var top = ManaFrameProfiler.TopByPeak(2);
        Check("the top list is ordered worst first",
            top.Count == 2 && top[0].Name == "test.big" && top[1].Name == "test.middle",
            string.Join(", ", top.Select(entry => entry.Name)));

        var names = ManaFrameProfiler.Names;
        Check("names come back sorted, for a panel that rebuilds on key identity",
            names.SequenceEqual(names.OrderBy(name => name, StringComparer.Ordinal)),
            string.Join(", ", names));

        Check("and the same array comes back while the set is unchanged",
            ReferenceEquals(names, ManaFrameProfiler.Names));

        // ---------------------------------------------------------------- disabled costs nothing
        ManaFrameProfiler.Reset();
        ManaFrameProfiler.Enabled = false;
        using (ManaFrameProfiler.Measure("test.disabled"))
        {
        }

        ManaFrameProfiler.EndFrame(frameMs: 999.0);
        Check("nothing records while disabled",
            !ManaFrameProfiler.TryGetSample("test.disabled", out _) && ManaFrameProfiler.FrameCount == 0);

        ManaFrameProfiler.Enabled = true;

        // ---------------------------------------------------------------- a real scope
        using (ManaFrameProfiler.Measure("test.measured"))
        {
            // Long enough that a wall clock can resolve it without making the suite slow.
            Thread.Sleep(12);
        }

        ManaFrameProfiler.EndFrame(frameMs: 14.0);
        ManaFrameProfiler.TryGetSample("test.measured", out var measured);
        Check("a using-scope times the block it wraps", measured.LastMs >= 8.0, $"{measured.LastMs:F1}ms");

        // Left clean for anything that runs after this, and for the app itself if this ran in-process.
        ManaFrameProfiler.Reset();
    }
}
