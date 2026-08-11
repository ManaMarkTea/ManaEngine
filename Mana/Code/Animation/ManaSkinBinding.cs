namespace Mana.Code.Animation
{
    // Per-mesh skinning payload: the untouched bind-pose geometry plus the fixed-width influence
    // table. Kept off ManaMeshCpuData deliberately -- skinning runs on the CPU, so neither graphics
    // backend ever needs to see joint indices or weights.
    public sealed class ManaSkinBinding
    {
        public const int InfluencesPerVertex = 4;

        public required ManaSkeleton Skeleton { get; init; }
        public required Vector3[] BindPositions { get; init; }
        public required Vector3[] BindNormals { get; init; }

        // Both are InfluencesPerVertex entries per vertex. A bone index of -1 marks an unused slot.
        public required int[] BoneIndices { get; init; }
        public required float[] BoneWeights { get; init; }

        public int VertexCount => BindPositions.Length;
    }
}
