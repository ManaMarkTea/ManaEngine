using ManaGeoTools.Code.GeoTiff.Terrarium;
using ManaGeoTools.Code.Planet;
using ManaUI;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaVoxel.Code.Levels;
using System.Numerics;

namespace ManaTests;

public static class PlanetStreamingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check) => RunAsync(check).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var tile = new TerrariumHeightTile(7, 1, 2, new float[256 * 256]);
        var pending = new TaskCompletionSource<TerrariumHeightTile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var stream = new PlanetTileStream((_, _, _) => { Interlocked.Increment(ref requests); return pending.Task; }, 2);
        var key = new TerrariumTileKey(7, 1, 2);
        var first = stream.FetchAsync([key, key]);
        var overlapping = stream.FetchAsync([key]);
        Check("overlapping refinements share one in-flight tile fetch", requests == 1);
        pending.SetResult(tile);
        var results = await Task.WhenAll(first, overlapping);
        Check("each refinement receives the shared tile once", results.All(result => result.Count == 1 && result[0] == tile));

        var attempts = 0;
        var retry = new PlanetTileStream((_, _, _) => ++attempts == 1
            ? Task.FromException<TerrariumHeightTile?>(new IOException("test cache read failure"))
            : Task.FromResult<TerrariumHeightTile?>(tile), 1);
        Check("one unavailable tile does not fault a refinement batch", (await retry.FetchAsync([key])).Count == 0);
        Check("failed fetches are removed so a later request can retry", (await retry.FetchAsync([key])).Count == 1 && attempts == 2);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        var bounded = new PlanetTileStream(async (_, _, _) =>
        {
            var now = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(peak, now));
            await release.Task;
            Interlocked.Decrement(ref active);
            return tile;
        }, 2);
        var batch = bounded.FetchAsync([key, new(7, 2, 2), new(7, 3, 2)]);
        Check("tile admission respects its concurrency budget", active == 2);
        release.SetResult();
        await batch;
        Check("queued tile fetches finish without exceeding that budget", peak == 2 && active == 0);

        var scheduler = new PlanetRebuildScheduler();
        var order = new List<int>();
        await scheduler.RunAsync("test failed rebuild", () => Task.FromException(new InvalidDataException("test rebuild")),
            _ => scheduler.Enqueue(PlanetRebuildKind.Split, () => order.Add(1)));
        scheduler.Enqueue(PlanetRebuildKind.Split, () => order.Add(2));
        Check("failed rebuild cleanup waits for the main-thread handoff", order.Count == 0);
        scheduler.Drain(PlanetRebuildKind.Split, 1);
        Check("completion handoff respects the frame budget", order.SequenceEqual(new[] { 1 }));
        scheduler.Drain(PlanetRebuildKind.Merge, 1);
        Check("draining merges does not consume split completions", order.Count == 1);
        scheduler.Drain(PlanetRebuildKind.Split, 1);
        Check("remaining completions retain arrival order", order.SequenceEqual(new[] { 1, 2 }));

        var retiredScheduler = new PlanetRebuildScheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = 0;
        retiredScheduler.Enqueue(PlanetRebuildKind.Split, () => applied++);
        var running = retiredScheduler.RunAsync("retired rebuild", async () =>
        {
            started.SetResult();
            await finish.Task;
            retiredScheduler.Enqueue(PlanetRebuildKind.Split, () => applied++);
            throw new InvalidOperationException("late retired rebuild");
        }, _ => retiredScheduler.Enqueue(PlanetRebuildKind.Upres, () => applied++));
        await started.Task;
        retiredScheduler.Retire();
        retiredScheduler.Retire();
        finish.SetResult();
        await running;
        await retiredScheduler.RunAsync("new retired work", () => { applied++; return Task.CompletedTask; }, _ => applied++);
        foreach (var kind in Enum.GetValues<PlanetRebuildKind>())
        {
            retiredScheduler.Drain(kind, 10);
        }
        Check("retirement discards queued and late handoffs and rejects new work", applied == 0);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var container = new ManaSceneNode(context, Matrix4x4.Identity);
        var planet = new PlanetNode(context, 1, 10, 2, 1, 2);
        container.AddChild(planet);
        container.RemoveChild(planet);
        Check("ordinary reparenting does not retire planet work", !planet.IsRetired);
        container.AddChild(planet);
        ManaLevelLoader.RetirePlanetWork(container);
        planet.RequestDetailAt(0, 0, 7);
        planet.TrySplitPatch(PatchKey.Root(0, 0, 0));
        planet.ProcessPendingSplits();
        Check("permanent removal retires nested planets and ignores detail requests",
            planet.IsRetired && !planet.CanSplitPatch(PatchKey.Root(0, 0, 0)) && planet.PendingChangedPatchCount == 0);

        var sampler = new PlanetHeightSampler(1, TimeSpan.FromMilliseconds(30));
        var dispatched = false;
        var discarded = await sampler.ReadbackAsync(() => { dispatched = true; return Task.FromResult(new[] { 1f }); }, () => false);
        Check("retired sampling skips native dispatch and releases its admission slot", discarded == null && !dispatched
            && await sampler.ReadbackAsync(() => Task.FromResult(new[] { 4f })) is [4f]);
        var readback = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Check("a delayed GPU readback returns CPU fallback", await sampler.ReadbackAsync(() => readback.Task) == null);
        var extraDispatch = false;
        var saturated = await sampler.ReadbackAsync(() => { extraDispatch = true; return Task.FromResult(new[] { 2f }); });
        Check("timing out does not free an unfinished native dispatch slot", saturated == null && !extraDispatch);
        readback.SetException(new InvalidOperationException("test late readback failure"));
        var recovered = await sampler.ReadbackAsync(() => Task.FromResult(new[] { 3f }));
        Check("a late failure is observed and releases admission for later samples", recovered is [3f]);
    }
}
