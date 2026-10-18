using System.Collections.Concurrent;

namespace ManaGraphics.Code.Graphics;

// Browser promises can settle after a context closes or a newer one starts initializing.
internal static class ManaWebGpuInitializationRequests
{
    private static long _nextId;
    private static readonly ConcurrentDictionary<int, Func<int, bool>> Pending = new();

    internal static int Register(Func<int, bool> accept)
    {
        var id = Interlocked.Increment(ref _nextId);
        if (id <= 0 || id > int.MaxValue)
        {
            throw new InvalidOperationException("WebGPU initialization request IDs exhausted.");
        }
        Pending[(int)id] = accept;
        return (int)id;
    }

    internal static bool Complete(int id, int handle) =>
        Pending.TryRemove(id, out var accept) && accept(handle);

    internal static void Cancel(int id) => Pending.TryRemove(id, out _);
}
