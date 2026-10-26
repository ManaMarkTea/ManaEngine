using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>
    /// A box of baked light probes (ManaProbeVolumeComponent): inside it, a surface's ambient light is what reaches its
    /// spot -- sky that is not walled off, sunlight bounced off the street, a lamp's glow off the wall -- instead of
    /// the open sky's. Probes stand <see cref="ProbeSpacing"/> apart; the scene is voxelized at <see cref="VoxelSize"/>
    /// for the rays they trace.
    /// </summary>
    public sealed record ManaProbeVolumeSettings(Vector3 Min, Vector3 Size, float ProbeSpacing, float VoxelSize, int Bounces)
    {
        /// <summary>Probes along each axis, one at the middle of each spacing-sized cell.</summary>
        public (int X, int Y, int Z) ProbeCounts => (Count(Size.X, ProbeSpacing), Count(Size.Y, ProbeSpacing), Count(Size.Z, ProbeSpacing));

        /// <summary>Voxels along each axis.</summary>
        public (int X, int Y, int Z) VoxelCounts => (Count(Size.X, VoxelSize), Count(Size.Y, VoxelSize), Count(Size.Z, VoxelSize));

        private static int Count(float length, float step) => Math.Clamp((int)MathF.Ceiling(length / MathF.Max(step, 0.05f)), 1, 512);
    }
}
