using System.Numerics;
using ManaCore.Graphics;
using ManaUI;

namespace ManaTests;

/// <summary>The probe volume's voxels: surfaces land where they are, walls block and wires do not, normals face out.</summary>
public static class ProbeVoxelizerChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var volume = new ManaProbeVolumeSettings(Vector3.Zero, new Vector3(8, 4, 8), 2.0f, 1.0f, 2);
        check("probe and voxel counts follow the spacing", volume.ProbeCounts == (4, 2, 4) && volume.VoxelCounts == (8, 4, 8),
            $"{volume.ProbeCounts} {volume.VoxelCounts}");

        // A floor across the box at y 0.5, a red wall at x 4.5, and a wire 2 cm thick running along z at y 3.5.
        var meshes = new List<ManaRenderMeshInstance>
        {
            Quad("Floor", new Vector3(0, 0.5f, 0), Vector3.UnitX * 8, Vector3.UnitZ * 8, Vector4.One),
            Quad("Wall", new Vector3(4.5f, 0, 0), Vector3.UnitY * 4, Vector3.UnitZ * 8, new Vector4(1, 0, 0, 1)),
            Quad("Wire", new Vector3(1.5f, 3.5f, 0), Vector3.UnitX * 0.02f, Vector3.UnitZ * 8, Vector4.One)
        };
        var voxels = ManaProbeVoxelizer.Build(volume, meshes);

        bool Solid(int x, int y, int z) => voxels.Albedo[voxels.Index(x, y, z) * 4 + 3] >= 128;
        check("the floor fills the layer it crosses", Enumerable.Range(0, 8).All(x => Solid(x, 0, 3)), "");
        check("and the air above it stays empty", !Solid(2, 2, 3) && !Solid(6, 2, 3), "");
        check("a wall blocks the probes' rays", Enumerable.Range(0, 4).All(y => Solid(4, y, 5)), "");
        check("a wire through a voxel does not", !Solid(1, 3, 4), $"alpha {voxels.Albedo[voxels.Index(1, 3, 4) * 4 + 3]}");

        var red = voxels.Albedo.AsSpan(voxels.Index(4, 2, 5) * 4, 3).ToArray();
        check("a voxel takes its surface's colour (linear)", red[0] > 200 && red[1] < 30 && red[2] < 30, string.Join(",", red));

        var up = voxels.Normal.AsSpan(voxels.Index(6, 0, 6) * 4, 3).ToArray();
        check("and its normal, out of the surface", up[1] > 240 && Math.Abs(up[0] - 128) < 8 && Math.Abs(up[2] - 128) < 8, string.Join(",", up));

        // A cut-out surface whose picture is all hole leaves nothing behind.
        var hole = Quad("Hole", new Vector3(0, 2.5f, 0), Vector3.UnitX * 8, Vector3.UnitZ * 8, new Vector4(1, 1, 1, 0), ManaOpacityMode.Cutout);
        var holed = ManaProbeVoxelizer.Build(volume, [hole]);
        check("a cut-out's holes are not solid", holed.Occupied == 0, $"{holed.Occupied} solid");
    }

    private static ManaRenderMeshInstance Quad(string name, Vector3 corner, Vector3 u, Vector3 v, Vector4 colour, ManaOpacityMode mode = ManaOpacityMode.Opaque)
    {
        var normal = Vector3.Normalize(Vector3.Cross(v, u));
        return new ManaRenderMeshInstance
        {
            Name = name,
            WorldTransform = Matrix4x4.Identity,
            OpacityMode = mode,
            MeshData = new ManaMeshCpuData
            {
                Indexes = [0, 1, 2, 0, 2, 3],
                Positions = [corner, corner + u, corner + u + v, corner + v],
                Normals = [normal, normal, normal, normal],
                Colors = [colour, colour, colour, colour],
                TextureCoordinates0 = [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY],
                TextureCoordinates1 = [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero]
            }
        };
    }
}
