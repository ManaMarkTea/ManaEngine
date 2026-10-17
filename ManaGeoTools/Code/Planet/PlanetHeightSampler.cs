using Mana;
using Mana.Code;
using ManaCore.Graphics;
using ManaGeoTools.Code.GeoTiff.Terrarium;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>Bounds GPU height sampling, with a CPU fallback when admission or readback times out.</summary>
    public sealed class PlanetHeightSampler
    {
        public static PlanetHeightSampler Shared { get; } = new(8, TimeSpan.FromSeconds(5));
        private readonly SemaphoreSlim _slots;
        private readonly TimeSpan _timeout;

        public PlanetHeightSampler(int concurrency, TimeSpan timeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            _slots = new SemaphoreSlim(concurrency);
            _timeout = timeout;
        }

        public async Task<float[]?> ReadbackAsync(Func<Task<float[]>> dispatch)
        {
            if (!await _slots.WaitAsync(_timeout))
            {
                return null;
            }
            Task<float[]>? pending = null;
            var timedOut = false;
            try
            {
                pending = dispatch();
                return await pending.WaitAsync(_timeout);
            }
            catch (TimeoutException)
            {
                timedOut = true;
                Console.WriteLine($"[Terrarium] GPU height sampling timed out after {_timeout.TotalSeconds:0.#}s, falling back to CPU.");
                return null;
            }
            finally
            {
                if (pending == null || (pending.IsCompleted && !timedOut))
                {
                    _slots.Release();
                }
                else
                {
                    // A caller timeout does not cancel the native readback or free its dispatch slot.
                    _ = ReleaseWhenFinishedAsync(pending);
                }
            }
        }

        private async Task ReleaseWhenFinishedAsync(Task<float[]> pending)
        {
            try { await pending; }
            catch (Exception exception) { Console.WriteLine($"[Terrarium] Late GPU height sampling failed: {exception.Message}"); }
            finally { _slots.Release(); }
        }

        public async Task<float[,]?> TrySampleAsync(
            ManaContext context,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int meshResolution,
            IReadOnlyList<TerrariumHeightTile> fetchedTiles,
            int requestedTileCount)
        {
            if (context.GraphicsDevice is not IHeightSamplingComputeDevice computeDevice)
            {
                return null;
            }

            if (fetchedTiles.Count != requestedTileCount)
            {
                return null;
            }

            try
            {
                var lonLatGrid = PlanetPatchMesh.ComputeLonLatGrid(faceNormal, minU, maxU, minV, maxV, meshResolution);
                var resolution = lonLatGrid.GetLength(0);
                var points = new Vector2[resolution * resolution];
                for (var y = 0; y < resolution; y++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        points[(y * resolution) + x] = lonLatGrid[x, y];
                    }
                }

                var tileDescriptors = new HeightSampleTileDescriptor[fetchedTiles.Count];
                for (var i = 0; i < fetchedTiles.Count; i++)
                {
                    var tile = fetchedTiles[i];
                    tileDescriptors[i] = new HeightSampleTileDescriptor
                    {
                        LonMin = tile.Area.UpperLeft.Longitude,
                        LonMax = tile.Area.LowerRight.Longitude,
                        LatMin = tile.Area.LowerLeft.Latitude,
                        LatMax = tile.Area.UpperRight.Latitude,
                        Width = TerrariumHeightTile.TileSize,
                        Height = TerrariumHeightTile.TileSize,
                        HeightData = tile.HeightData,
                    };
                }

                var heights = await ReadbackAsync(() => computeDevice.SampleHeightsAsync(points, tileDescriptors));
                if (heights == null)
                {
                    return null;
                }

                var result = new float[resolution, resolution];
                for (var y = 0; y < resolution; y++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        result[x, y] = heights[(y * resolution) + x];
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Terrarium] GPU height sampling failed, falling back to CPU: {ex.Message}");
                return null;
            }
        }

    }
}
