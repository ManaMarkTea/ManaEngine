using ManaGeoTools.Code.GeoTiff.Terrarium;

namespace ManaGeoTools.Code.Planet;

public readonly record struct TerrariumTileKey(int Zoom, int X, int Y);

/// <summary>Shares in-flight tile fetches across refinements without retaining a second decoded tile cache.</summary>
public sealed class PlanetTileStream
{
    public static PlanetTileStream Shared { get; } = new(TerrariumTileFetcher.FetchTileAsync, 24);
    private readonly Func<int, int, int, Task<TerrariumHeightTile?>> _fetch;
    private readonly SemaphoreSlim _slots;
    private readonly object _gate = new();
    private readonly Dictionary<TerrariumTileKey, Lazy<Task<TerrariumHeightTile?>>> _pending = new();
    public int MaxConcurrentFetches { get; }
    public int ActiveFetches => MaxConcurrentFetches - _slots.CurrentCount;

    public PlanetTileStream(Func<int, int, int, Task<TerrariumHeightTile?>> fetch, int maxConcurrentFetches)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentFetches);
        _fetch = fetch;
        MaxConcurrentFetches = maxConcurrentFetches;
        _slots = new SemaphoreSlim(maxConcurrentFetches);
    }

    public async Task<List<TerrariumHeightTile>> FetchAsync(IReadOnlyList<TerrariumTileKey> keys)
    {
        var tasks = keys.Distinct().Select(FetchOne).ToArray();
        return (await Task.WhenAll(tasks)).OfType<TerrariumHeightTile>().ToList();
    }

    private Task<TerrariumHeightTile?> FetchOne(TerrariumTileKey key)
    {
        Lazy<Task<TerrariumHeightTile?>> pending;
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var existing))
            {
                pending = new Lazy<Task<TerrariumHeightTile?>>(() => FetchCoreAsync(key));
                _pending.Add(key, pending);
            }
            else
            {
                pending = existing;
            }
        }
        return pending.Value;
    }

    private async Task<TerrariumHeightTile?> FetchCoreAsync(TerrariumTileKey key)
    {
        await _slots.WaitAsync();
        try
        {
            return await _fetch(key.Zoom, key.X, key.Y);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Terrarium] Tile {key.Zoom}/{key.X}/{key.Y} failed: {exception.Message}");
            return null;
        }
        finally
        {
            _slots.Release();
            lock (_gate)
            {
                _pending.Remove(key);
            }
        }
    }
}
