using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Phones without LiDAR (ARCore) give a depth image per frame instead of a mesh: this turns one keyframe's depth
/// into a world-space piece, a grid of two triangles a cell with the cells that straddle a depth jump left out.
/// </summary>
public static class ScanDepthMesher
{
    /// <summary>A cell is dropped when its depths differ by more than this share of the nearest one (a silhouette edge).</summary>
    public const float MaxDepthJump = 0.08f;

    /// <summary>Every <paramref name="step"/>th depth sample becomes a vertex: the depth image is finer than a room scan needs.</summary>
    public static ScanMeshPiece? Build(ScanKeyframe keyframe, int step = 1, float minDepth = 0.2f, float maxDepth = 5f)
    {
        if (keyframe.Depth is not { } depth || keyframe.DepthWidth < 2 || keyframe.DepthHeight < 2)
        {
            return null;
        }

        step = Math.Max(1, step);
        int w = keyframe.DepthWidth / step;
        int h = keyframe.DepthHeight / step;
        int full = keyframe.DepthWidth;
        var vertexOf = new int[w * h];
        var positions = new List<Vector3>();
        var scaleX = keyframe.Width / (float)w;
        var scaleY = keyframe.Height / (float)h;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var z = depth[y * step * full + x * step];
                if (!(z >= minDepth && z <= maxDepth))
                {
                    vertexOf[y * w + x] = -1;
                    continue;
                }
                // Camera space: X right, Y up, looking down -Z; image rows go down.
                var u = (x + 0.5f) * scaleX;
                var v = (y + 0.5f) * scaleY;
                var local = new Vector3((u - keyframe.Cx) / keyframe.Fx * z, -(v - keyframe.Cy) / keyframe.Fy * z, -z);
                vertexOf[y * w + x] = positions.Count;
                positions.Add(Vector3.Transform(local, keyframe.CameraToWorld));
            }
        }

        float Z(int cell) => depth[(cell / w) * step * full + (cell % w) * step];
        var indices = new List<int>();
        for (var y = 0; y < h - 1; y++)
        {
            for (var x = 0; x < w - 1; x++)
            {
                var a = y * w + x;
                var b = a + 1;
                var c = a + w;
                var d = c + 1;
                if (vertexOf[a] < 0 || vertexOf[b] < 0 || vertexOf[c] < 0 || vertexOf[d] < 0 || Jumps(Z(a), Z(b), Z(c), Z(d)))
                {
                    continue;
                }
                // Counter-clockwise seen from the camera, so the face normals point back at it.
                indices.Add(vertexOf[a]); indices.Add(vertexOf[c]); indices.Add(vertexOf[b]);
                indices.Add(vertexOf[b]); indices.Add(vertexOf[c]); indices.Add(vertexOf[d]);
            }
        }
        if (indices.Count == 0)
        {
            return null;
        }

        var pos = positions.ToArray();
        var idx = indices.ToArray();
        return new ScanMeshPiece(Guid.NewGuid(), pos, Normals(pos, idx), idx);
    }

    private static bool Jumps(float a, float b, float c, float d)
    {
        var near = MathF.Min(MathF.Min(a, b), MathF.Min(c, d));
        var far = MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
        return far - near > near * MaxDepthJump;
    }

    private static Vector3[] Normals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var n = Vector3.Cross(positions[indices[i + 1]] - positions[indices[i]], positions[indices[i + 2]] - positions[indices[i]]);
            normals[indices[i]] += n;
            normals[indices[i + 1]] += n;
            normals[indices[i + 2]] += n;
        }
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 1e-18f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
        }
        return normals;
    }
}
