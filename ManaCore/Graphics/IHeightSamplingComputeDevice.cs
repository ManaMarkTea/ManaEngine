namespace ManaCore.Graphics
{
    // Backend-agnostic description of one height-source tile for GPU sampling. ManaCore must not depend
    // on ManaGeoTools' GeoTiff/TerrariumHeightTile types (wrong dependency direction -- ManaGeoTools
    // references Mana, which references ManaCore, not the other way around), so callers convert their
    // own tile objects into this before calling SampleHeightsAsync.
    public readonly struct HeightSampleTileDescriptor
    {
        public required float LonMin { get; init; }
        public required float LonMax { get; init; }
        public required float LatMin { get; init; }
        public required float LatMax { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required ReadOnlyMemory<float> HeightData { get; init; }   // row-major, Width*Height floats
    }

    // Optional capability, implemented by backends that can sample a height source on the GPU instead of
    // per-vertex on the CPU, checked via `Context.GraphicsDevice is IHeightSamplingComputeDevice`.
    public interface IHeightSamplingComputeDevice : IManaGraphicsDevice
    {
        // tilesNewestFirst: the caller's own "which source wins where multiple overlap" ordering (see
        // GeoTiffLibrary.FindCurrentTile) -- the implementation takes the first bounding-box match per
        // point, same newest-wins semantics, just without needing the CPU spatial-grid bucketing since a
        // single dispatch's tile list is already small and bounded.
        Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<System.Numerics.Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst);
    }
}
