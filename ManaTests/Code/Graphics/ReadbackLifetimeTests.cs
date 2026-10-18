using ManaGraphics.Code.Graphics;
using ManaUI;
using Silk.NET.WebGPU;

namespace ManaTests;

public static class ReadbackLifetimeChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check) =>
        RunAsync(check).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var registry = new ManaWebGpuReadbackRegistry();
        nint id = 0;
        var completed = 0;
        var canceled = 0;
        registry.Register(token => id = token, status => completed += status == BufferMapAsyncStatus.Success ? 1 : 10,
            _ => canceled++);
        Check("admitted readbacks remain owned until callback or shutdown", id != 0 && registry.PendingCount == 1);
        ManaWebGpuReadbackRegistry.Complete(id, BufferMapAsyncStatus.Success);
        ManaWebGpuReadbackRegistry.Complete(id, BufferMapAsyncStatus.Success);
        registry.FaultAllPending();
        Check("duplicate delivery and later shutdown cannot retire a completed readback twice",
            completed == 1 && canceled == 0 && registry.PendingCount == 0);

        var retired = new ManaWebGpuReadbackRegistry();
        nint retiredId = 0;
        var cleanupThread = 0;
        retired.Register(token => retiredId = token, _ => completed++, failure =>
        {
            canceled++;
            cleanupThread = Environment.CurrentManagedThreadId;
            // Unmapping a canceled staging buffer may synchronously deliver its native callback.
            ManaWebGpuReadbackRegistry.Complete(retiredId, BufferMapAsyncStatus.Success);
        });
        var closingThread = Environment.CurrentManagedThreadId;
        retired.FaultAllPending();
        retired.FaultAllPending();
        ManaWebGpuReadbackRegistry.Complete(retiredId, BufferMapAsyncStatus.Success);
        Check("shutdown removes routing before cleanup can deliver a late callback",
            canceled == 1 && completed == 1 && retired.PendingCount == 0);
        Check("shutdown cleanup finishes on the closing thread before device release", cleanupThread == closingThread);
        var dispatched = false;
        retired.Register(_ => dispatched = true, _ => completed++, _ => canceled++);
        Check("closed admission cancels new work without dispatching", !dispatched && canceled == 2 && retired.PendingCount == 0);

        var fresh = new ManaWebGpuReadbackRegistry();
        nint freshId = 0;
        fresh.Register(token => freshId = token, _ => completed++, _ => canceled++);
        ManaWebGpuReadbackRegistry.Complete(retiredId, BufferMapAsyncStatus.Success);
        Check("retired IDs never address a fresh context's request", freshId != retiredId && fresh.PendingCount == 1 && completed == 1);
        ManaWebGpuReadbackRegistry.Complete(freshId, BufferMapAsyncStatus.Success);
        Check("a fresh context still completes normally after another context closes", fresh.PendingCount == 0 && completed == 2);
        fresh.FaultAllPending();

        var failed = new ManaWebGpuReadbackRegistry();
        var error = new InvalidOperationException("dispatch failed");
        Exception? received = null;
        nint failedId = 0;
        var failedCleanup = 0;
        failed.Register(token => { failedId = token; throw error; }, _ => completed++, failure =>
        {
            received = failure;
            failedCleanup++;
        });
        ManaWebGpuReadbackRegistry.Complete(failedId, BufferMapAsyncStatus.Success);
        failed.FaultAllPending();
        Check("dispatch failure rolls back ownership once and preserves its error",
            ReferenceEquals(received, error) && failedCleanup == 1 && failed.PendingCount == 0 && completed == 2);

        var synchronous = new ManaWebGpuReadbackRegistry();
        var synchronousComplete = false;
        synchronous.Register(token => ManaWebGpuReadbackRegistry.Complete(token, BufferMapAsyncStatus.Success),
            _ => synchronousComplete = true, _ => canceled++);
        Check("synchronous native delivery can retire during dispatch", synchronousComplete && synchronous.PendingCount == 0);
        synchronous.FaultAllPending();

        var racing = new ManaWebGpuReadbackRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFinished = false;
        var shutdownFinished = false;
        nint racingId = 0;
        racing.Register(token => racingId = token, _ =>
        {
            entered.SetResult();
            release.Wait(TimeSpan.FromSeconds(5));
            cleanupFinished = true;
        }, _ => canceled++);
        var delivery = Task.Run(() => ManaWebGpuReadbackRegistry.Complete(racingId, BufferMapAsyncStatus.Success));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shutdown = Task.Run(() =>
        {
            closing.SetResult();
            racing.FaultAllPending();
            shutdownFinished = true;
        });
        try
        {
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check("shutdown waits for a callback already cleaning native resources", !Volatile.Read(ref shutdownFinished));
        }
        finally
        {
            release.Set();
        }
        await Task.WhenAll(delivery, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
        Check("callback and shutdown leave no live request or deferred GPU cleanup",
            cleanupFinished && shutdownFinished && racing.PendingCount == 0 && canceled == 2);
    }
}
