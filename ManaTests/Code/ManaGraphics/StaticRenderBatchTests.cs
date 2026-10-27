using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics.Resources;
using ManaUI;
namespace ManaTests;
public static class StaticRenderBatchChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool ok) => check(name, ok, "");
        var data = new ManaMeshCpuData { Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], Indexes = [0, 1, 2],
            Colors = [], TextureCoordinates0 = [], TextureCoordinates1 = [] };
        var a = new ManaRenderMeshInstance { StaticBatchKey = "model", InstanceKey = "a", MeshKey = "triangle",
            Name = "A", MeshData = data, WorldTransform = Matrix4x4.Identity };
        var b = a with { InstanceKey = "b", WorldTransform = Matrix4x4.CreateTranslation(10, 2, 3) };
        var batcher = new ManaStaticMeshBatcher();
        var first = batcher.Build([a, b]).Single();
        Check("render batching preserves sources and transforms triangles", a.MeshData.Positions.Length == 3
            && first.MeshData.Indexes.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })
            && first.MeshData.Positions[3] == new Vector3(10, 2, 3));
        Check("unchanged frames reuse geometry", ReferenceEquals(first, batcher.Build([a, b]).Single()));
        var moved = batcher.Build([a, b with { WorldTransform = Matrix4x4.CreateTranslation(20, 0, 0) }]).Single();
        Check("moving a part refreshes geometry", moved.MeshData.Positions[3].X == 20 && !ReferenceEquals(first, moved));
        Check("material edits split batches", batcher.Build([a, b with { Roughness = 0.2f }]).Count == 2);
        Check("selection stays separately outlined", batcher.Build([a, b with { OutlineEnabled = true }]).Count == 2);
        Check("transparent surfaces stay sortable", batcher.Build([a, b with { IsTransparent = true }]).Count == 2);
        Check("separate model scopes stay separate", batcher.Build([a, b with { StaticBatchKey = "other" }]).Count == 2);
        Check("removal leaves no stale geometry", ReferenceEquals(batcher.Build([a]).Single(), a));
        Check("custom shaders retain their own coordinate space", batcher.Build([a, b with { ShaderName = "CustomShader" }]).Count == 2);
        Check("mesh revisions invalidate geometry", !ReferenceEquals(first, batcher.Build([a, b with { MeshVersion = 2 }]).Single()));
        Check("vertex limits split batches", new ManaStaticMeshBatcher { MaxVertices = 3 }.Build([a, b]).Count == 2);
    }
}
