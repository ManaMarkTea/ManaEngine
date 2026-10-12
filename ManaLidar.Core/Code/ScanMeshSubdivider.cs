using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Splits a piece's triangles until no edge is longer than a limit. Colours live on vertices, so ARKit's 3-5 cm
/// mesh can never show detail finer than that; more vertices give the photos somewhere to put it.
/// Whether an edge splits depends only on its length, so the two triangles sharing it always agree and the mesh
/// stays free of cracks.
/// </summary>
public static class ScanMeshSubdivider
{
    public static ScanMeshPiece Subdivide(ScanMeshPiece piece, float maxEdge, int maxPasses = 6)
    {
        if (maxEdge <= 0)
        {
            return piece;
        }

        var positions = new List<Vector3>(piece.Positions);
        var normals = new List<Vector3>(piece.Normals);
        var indices = piece.Indices;
        var surfaces = piece.FaceSurfaces;
        var maxEdgeSquared = maxEdge * maxEdge;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var midpoints = new Dictionary<long, int>();
            var nextIndices = new List<int>(indices.Length * 2);
            var nextSurfaces = surfaces != null ? new List<ScanSurface>(surfaces.Length * 2) : null;
            var split = false;

            int Midpoint(int a, int b)
            {
                if (Vector3.DistanceSquared(positions[a], positions[b]) <= maxEdgeSquared)
                {
                    return -1;
                }
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!midpoints.TryGetValue(key, out var m))
                {
                    m = positions.Count;
                    positions.Add((positions[a] + positions[b]) * 0.5f);
                    var n = normals[a] + normals[b];
                    normals.Add(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : normals[a]);
                    midpoints[key] = m;
                }
                return m;
            }

            for (var f = 0; f < indices.Length / 3; f++)
            {
                var v = new[] { indices[f * 3], indices[f * 3 + 1], indices[f * 3 + 2] };
                // m[i] is the midpoint of edge v[i] -> v[i + 1], or -1 when that edge is short enough.
                var m = new[] { Midpoint(v[0], v[1]), Midpoint(v[1], v[2]), Midpoint(v[2], v[0]) };
                var count = (m[0] >= 0 ? 1 : 0) + (m[1] >= 0 ? 1 : 0) + (m[2] >= 0 ? 1 : 0);
                var before = nextIndices.Count;

                if (count == 0)
                {
                    nextIndices.AddRange(v);
                }
                else if (count == 3)
                {
                    nextIndices.AddRange(new[] { v[0], m[0], m[2], m[0], v[1], m[1], m[2], m[1], v[2], m[0], m[1], m[2] });
                }
                else
                {
                    // Turn the triangle so the split pattern starts at corner 0, keeping its winding.
                    var r = count == 1 ? Array.FindIndex(m, x => x >= 0) : Array.FindIndex(m, x => x < 0) switch { 0 => 1, 1 => 2, _ => 0 };
                    var a = v[r];
                    var b = v[(r + 1) % 3];
                    var c = v[(r + 2) % 3];
                    var ab = m[r];
                    if (count == 1)
                    {
                        nextIndices.AddRange(new[] { a, ab, c, ab, b, c });
                    }
                    else
                    {
                        var bc = m[(r + 1) % 3];
                        nextIndices.AddRange(new[] { ab, b, bc, a, ab, bc, a, bc, c });
                    }
                }

                if (count > 0)
                {
                    split = true;
                }
                if (nextSurfaces != null && surfaces != null)
                {
                    for (var t = 0; t < (nextIndices.Count - before) / 3; t++)
                    {
                        nextSurfaces.Add(surfaces[f]);
                    }
                }
            }

            indices = nextIndices.ToArray();
            surfaces = nextSurfaces?.ToArray();
            if (!split)
            {
                break;
            }
        }

        return new ScanMeshPiece(piece.Id, positions.ToArray(), normals.ToArray(), indices, surfaces);
    }
}
