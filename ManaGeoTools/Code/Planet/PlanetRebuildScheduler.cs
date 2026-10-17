using System.Collections.Concurrent;

namespace ManaGeoTools.Code.Planet;

public enum PlanetRebuildKind { Upres, Split, Merge }

/// <summary>Runs background work and budgets the caller's main-thread completion handoff.</summary>
public sealed class PlanetRebuildScheduler
{
    private readonly ConcurrentQueue<Action>[] _ready = [new(), new(), new()];

    public Task RunAsync(string name, Func<Task> work, Action<Exception> failed) => Task.Run(async () =>
    {
        if (string.IsNullOrEmpty(Thread.CurrentThread.Name))
        {
            Thread.CurrentThread.Name = name;
        }
        try
        {
            await work();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Planet] {name} failed: {exception.Message}");
            failed(exception);
        }
    });

    public void Enqueue(PlanetRebuildKind kind, Action completion) => _ready[(int)kind].Enqueue(completion);

    // The caller supplies its scene lock and calls this only on the main thread.
    public void Drain(PlanetRebuildKind kind, int budget)
    {
        for (var i = 0; i < budget && _ready[(int)kind].TryDequeue(out var completion); i++)
        {
            completion();
        }
    }
}
