namespace ManaCore.Graphics
{
    public sealed class ManaRenderScene
    {
        public required ManaCameraData Camera { get; set; }
        public required IReadOnlyList<ManaRenderMeshInstance> Meshes { get; init; }
    }
}
