using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Fuses depth images into one surface, for phones that give depth per frame rather than a mesh (ARCore).
/// Meshing each frame on its own stacks a new sheet wherever views overlap; here every frame instead votes on a
/// signed distance to the surface in each voxel near it (a TSDF), noise averages out, and one surface is read
/// back. Voxels live in sparse 8x8x8 blocks, so only the space near surfaces costs memory; each block is read
/// back as its own piece with a fixed id, so a recording's SetPiece replaces it as the scan refines.
/// </summary>
public sealed class ScanTsdfVolume
{
    public const int BlockSize = 8;
    private const int BlockVoxels = BlockSize * BlockSize * BlockSize;

    private sealed class Block
    {
        public readonly float[] Sdf = new float[BlockVoxels];
        public readonly float[] Weight = new float[BlockVoxels];

        public Block()
        {
            Array.Fill(Sdf, 1f);
        }
    }

    private readonly object _lock = new();
    private readonly Dictionary<(int X, int Y, int Z), Block> _blocks = new();
    private readonly HashSet<(int X, int Y, int Z)> _dirty = new();

    public ScanTsdfVolume(float voxelSize = 0.02f, float truncationVoxels = 4f)
    {
        VoxelSize = voxelSize;
        Truncation = voxelSize * truncationVoxels;
    }

    /// <summary>Edge of one voxel (metres): the finest shape the fused mesh can hold.</summary>
    public float VoxelSize { get; }
    /// <summary>How far either side of a measured surface a frame updates voxels.</summary>
    public float Truncation { get; }
    /// <summary>A voxel needs this many frames' say before it can carry surface, so a one-off bad depth leaves nothing.</summary>
    public float MinWeight { get; set; } = 2f;
    /// <summary>Caps the history, so something moved during the scan is overwritten in a few dozen frames.</summary>
    public float MaxWeight { get; set; } = 48f;
    public float MinDepth { get; set; } = 0.15f;
    public float MaxDepth { get; set; } = 5f;

    public int BlockCount
    {
        get { lock (_lock) { return _blocks.Count; } }
    }

    /// <summary>The voxels' memory: two floats each, in whole blocks.</summary>
    public long EstimatedBytes => BlockCount * (long)BlockVoxels * 8;

    /// <summary>Adds a keyframe's depth to the volume and returns the pieces that changed (empty ones included).</summary>
    public IReadOnlyList<ScanMeshPiece> Integrate(ScanKeyframe keyframe)
    {
        lock (_lock)
        {
            IntegrateLocked(keyframe);
            return ExtractDirtyLocked();
        }
    }

    /// <summary>Every block's surface, e.g. to export.</summary>
    public IReadOnlyList<ScanMeshPiece> ExtractAll()
    {
        lock (_lock)
        {
            return _blocks.Keys.Select(ExtractBlock).Where(p => p.TriangleCount > 0).ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _blocks.Clear();
            _dirty.Clear();
        }
    }

    private void IntegrateLocked(ScanKeyframe keyframe)
    {
        if (keyframe.Depth is not { } depth)
        {
            return;
        }

        // Find the blocks the frame reaches: along each depth pixel's ray, the stretch within the truncation band.
        var touched = new HashSet<(int, int, int)>();
        var blockSpan = VoxelSize * BlockSize;
        var steps = Math.Max(1, (int)MathF.Ceiling(2 * Truncation / (blockSpan * 0.5f)));
        for (var dy = 0; dy < keyframe.DepthHeight; dy++)
        {
            for (var dx = 0; dx < keyframe.DepthWidth; dx++)
            {
                var d = depth[dy * keyframe.DepthWidth + dx];
                if (!(d >= MinDepth && d <= MaxDepth))
                {
                    continue;
                }
                var u = (dx + 0.5f) * keyframe.Width / keyframe.DepthWidth;
                var v = (dy + 0.5f) * keyframe.Height / keyframe.DepthHeight;
                var ray = new Vector3((u - keyframe.Cx) / keyframe.Fx, -(v - keyframe.Cy) / keyframe.Fy, -1f);
                for (var s = 0; s <= steps; s++)
                {
                    var along = d - Truncation + 2 * Truncation * s / steps;
                    var world = Vector3.Transform(ray * along, keyframe.CameraToWorld);
                    touched.Add(BlockOf(world));
                }
            }
        }

        foreach (var key in touched)
        {
            if (!_blocks.ContainsKey(key))
            {
                _blocks[key] = new Block();
            }
        }

        var keys = touched.ToArray();
        var changed = new bool[keys.Length];
        Parallel.For(0, keys.Length, i => changed[i] = IntegrateBlock(keys[i], _blocks[keys[i]], keyframe));

        for (var i = 0; i < keys.Length; i++)
        {
            if (!changed[i])
            {
                continue;
            }
            // A block's cells read one voxel into the blocks above it, so the ones below and behind change too.
            var (x, y, z) = keys[i];
            for (var ox = -1; ox <= 0; ox++)
            {
                for (var oy = -1; oy <= 0; oy++)
                {
                    for (var oz = -1; oz <= 0; oz++)
                    {
                        _dirty.Add((x + ox, y + oy, z + oz));
                    }
                }
            }
        }
    }

    private bool IntegrateBlock((int X, int Y, int Z) key, Block block, ScanKeyframe keyframe)
    {
        var changed = false;
        for (var i = 0; i < BlockVoxels; i++)
        {
            var world = VoxelCentre(key.X * BlockSize + i % BlockSize, key.Y * BlockSize + i / BlockSize % BlockSize,
                key.Z * BlockSize + i / (BlockSize * BlockSize));
            if (!keyframe.Project(world, out var pixel, out var distance)
                || pixel.X < 0 || pixel.Y < 0 || pixel.X >= keyframe.Width || pixel.Y >= keyframe.Height)
            {
                continue;
            }
            var measured = keyframe.DepthAt(pixel);
            if (!(measured >= MinDepth && measured <= MaxDepth))
            {
                continue;
            }
            var sdf = measured - distance;
            if (sdf < -Truncation)
            {
                continue; // well behind the surface: this frame cannot know what is there
            }
            var tsdf = MathF.Min(1f, sdf / Truncation);
            var w = block.Weight[i];
            block.Sdf[i] = (block.Sdf[i] * w + tsdf) / (w + 1);
            block.Weight[i] = MathF.Min(w + 1, MaxWeight);
            changed = true;
        }
        return changed;
    }

    private IReadOnlyList<ScanMeshPiece> ExtractDirtyLocked()
    {
        var pieces = _dirty.Where(_blocks.ContainsKey).Select(ExtractBlock).ToArray();
        _dirty.Clear();
        return pieces;
    }

    // Surface nets: one vertex inside every cell the surface passes through, at the mean of its edge crossings, and
    // a quad across every voxel edge with a sign change. Simpler than marching cubes, no tables, and smoother.
    private ScanMeshPiece ExtractBlock((int X, int Y, int Z) key)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var cellVertex = new Dictionary<(int, int, int), int>();

        int Vertex(int cx, int cy, int cz)
        {
            if (cellVertex.TryGetValue((cx, cy, cz), out var existing))
            {
                return existing;
            }
            var index = -1;
            if (CellSurface(cx, cy, cz, out var position, out var normal))
            {
                index = positions.Count;
                positions.Add(position);
                normals.Add(normal);
            }
            cellVertex[(cx, cy, cz)] = index;
            return index;
        }

        var x0 = key.X * BlockSize;
        var y0 = key.Y * BlockSize;
        var z0 = key.Z * BlockSize;
        for (var z = z0; z < z0 + BlockSize; z++)
        {
            for (var y = y0; y < y0 + BlockSize; y++)
            {
                for (var x = x0; x < x0 + BlockSize; x++)
                {
                    var (s, w) = Sample(x, y, z);
                    if (w < MinWeight)
                    {
                        continue;
                    }
                    for (var axis = 0; axis < 3; axis++)
                    {
                        var (nx, ny, nz) = axis switch { 0 => (x + 1, y, z), 1 => (x, y + 1, z), _ => (x, y, z + 1) };
                        var (s1, w1) = Sample(nx, ny, nz);
                        if (w1 < MinWeight || (s < 0) == (s1 < 0))
                        {
                            continue;
                        }
                        // The four cells around this edge, ordered so their quad faces +axis (eb x ec = ea).
                        var (b, c) = axis switch { 0 => (1, 2), 1 => (2, 0), _ => (0, 1) };
                        var c00 = Offset(x, y, z, b, -1, c, -1);
                        var c10 = Offset(x, y, z, c, -1);
                        var c11 = (x, y, z);
                        var c01 = Offset(x, y, z, b, -1);
                        var q = new[] { Vertex(c00.Item1, c00.Item2, c00.Item3), Vertex(c10.Item1, c10.Item2, c10.Item3),
                            Vertex(c11.Item1, c11.Item2, c11.Item3), Vertex(c01.Item1, c01.Item2, c01.Item3) };
                        if (q.Any(i => i < 0))
                        {
                            continue;
                        }
                        // Positive is free space (toward the camera), so the surface faces +axis when the far end is free.
                        if (s1 > 0)
                        {
                            indices.AddRange(new[] { q[0], q[1], q[2], q[0], q[2], q[3] });
                        }
                        else
                        {
                            indices.AddRange(new[] { q[0], q[2], q[1], q[0], q[3], q[2] });
                        }
                    }
                }
            }
        }

        return new ScanMeshPiece(BlockId(key), positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    private bool CellSurface(int cx, int cy, int cz, out Vector3 position, out Vector3 normal)
    {
        position = default;
        normal = default;
        Span<float> s = stackalloc float[8];
        for (var i = 0; i < 8; i++)
        {
            var (v, w) = Sample(cx + (i & 1), cy + (i >> 1 & 1), cz + (i >> 2 & 1));
            if (w < MinWeight)
            {
                return false;
            }
            s[i] = v;
        }

        var sum = Vector3.Zero;
        var crossings = 0;
        for (var i = 0; i < 8; i++)
        {
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                var j = i | bit;
                if (j == i || (s[i] < 0) == (s[j] < 0))
                {
                    continue;
                }
                var t = s[i] / (s[i] - s[j]);
                var a = new Vector3(i & 1, i >> 1 & 1, i >> 2 & 1);
                var b = new Vector3(j & 1, j >> 1 & 1, j >> 2 & 1);
                sum += Vector3.Lerp(a, b, t);
                crossings++;
            }
        }
        if (crossings == 0)
        {
            return false;
        }

        position = (new Vector3(cx, cy, cz) + sum / crossings + new Vector3(0.5f)) * VoxelSize;
        // The distance rises toward free space, so its gradient is the outward normal.
        var g = new Vector3(
            s[1] - s[0] + s[3] - s[2] + s[5] - s[4] + s[7] - s[6],
            s[2] - s[0] + s[3] - s[1] + s[6] - s[4] + s[7] - s[5],
            s[4] - s[0] + s[5] - s[1] + s[6] - s[2] + s[7] - s[3]);
        normal = g.LengthSquared() > 1e-12f ? Vector3.Normalize(g) : Vector3.UnitY;
        return true;
    }

    private (float Sdf, float Weight) Sample(int x, int y, int z)
    {
        var key = (FloorDiv(x), FloorDiv(y), FloorDiv(z));
        if (!_blocks.TryGetValue(key, out var block))
        {
            return (1f, 0f);
        }
        var i = (x - key.Item1 * BlockSize) + (y - key.Item2 * BlockSize) * BlockSize
            + (z - key.Item3 * BlockSize) * BlockSize * BlockSize;
        return (block.Sdf[i], block.Weight[i]);
    }

    private Vector3 VoxelCentre(int x, int y, int z) => (new Vector3(x, y, z) + new Vector3(0.5f)) * VoxelSize;

    private (int, int, int) BlockOf(Vector3 world) => (
        FloorDiv((int)MathF.Floor(world.X / VoxelSize)),
        FloorDiv((int)MathF.Floor(world.Y / VoxelSize)),
        FloorDiv((int)MathF.Floor(world.Z / VoxelSize)));

    private static int FloorDiv(int v) => v >= 0 ? v / BlockSize : (v - BlockSize + 1) / BlockSize;

    private static (int, int, int) Offset(int x, int y, int z, int axis, int by, int axis2 = -1, int by2 = 0)
    {
        Span<int> p = stackalloc int[] { x, y, z };
        p[axis] += by;
        if (axis2 >= 0)
        {
            p[axis2] += by2;
        }
        return (p[0], p[1], p[2]);
    }

    private static Guid BlockId((int X, int Y, int Z) key)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, key.X);
        BitConverter.TryWriteBytes(bytes[4..], key.Y);
        BitConverter.TryWriteBytes(bytes[8..], key.Z);
        BitConverter.TryWriteBytes(bytes[12..], 0x46445354); // "TSDF": apart from ARKit's anchor ids
        return new Guid(bytes);
    }
}
