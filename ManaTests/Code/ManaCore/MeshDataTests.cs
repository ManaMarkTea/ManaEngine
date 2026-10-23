using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// ManaMeshCpuData's bounds and geometry fingerprint: instanced drawing batches meshes by the fingerprint, so two
/// meshes built alike must match and any difference in any stream must not.
/// </summary>
public static class MeshDataChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        static ManaMeshCpuData Triangle(float lift = 0.0f, float red = 1.0f, uint last = 2) => new()
        {
            Positions = [new(0, lift, 0), new(1, 0, 0), new(0, 0, 1)],
            Normals = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
            Colors = [new(red, 1, 1, 1), Vector4.One, Vector4.One],
            TextureCoordinates0 = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
            TextureCoordinates1 = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
            Indexes = [0, 1, last]
        };

        var a = Triangle();
        check("two meshes built alike share a fingerprint", a.GeometryHash == Triangle().GeometryHash, "");
        check("a moved vertex changes it", a.GeometryHash != Triangle(lift: 0.01f).GeometryHash, "");
        check("a vertex colour changes it", a.GeometryHash != Triangle(red: 0.5f).GeometryHash, "");
        check("the indices change it", a.GeometryHash != Triangle(last: 1).GeometryHash, "");

        var lifted = Triangle(lift: 2.0f);
        check("bounds round the positions", lifted.Bounds == (new Vector3(0, 0, 0), new Vector3(1, 2, 1)), $"{lifted.Bounds}");
        var placed = lifted.WorldBounds(Matrix4x4.CreateScale(2.0f) * Matrix4x4.CreateTranslation(10, 0, 0));
        check("and placed in the world", placed == (new Vector3(10, 0, 0), new Vector3(12, 4, 2)), $"{placed}");
    }
}
