using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Quadric edge collapse (Garland and Heckbert): merges triangles wherever that barely moves the surface, so a flat
/// floor ends up a handful of large triangles while a plant or a chair keeps its shape. Colour no longer needs the
/// vertices once it is baked into a texture, so the mesh only has to carry the shape.
/// Open edges (holes, the scan's outline) get extra planes so they keep their line rather than shrinking.
/// </summary>
public static class ScanMeshSimplifier
{
    /// <param name="maxError">How far (metres, RMS) the surface may move from the planes it was built from.</param>
    public static ScanMeshPiece Simplify(ScanMeshPiece piece, float maxError)
    {
        var mesh = new Collapser(piece);
        mesh.Run(maxError);
        return mesh.Result(piece.Id);
    }

    /// <summary>Joins vertices at exactly the same place, so pieces cut from one surface (fused blocks) simplify as one.</summary>
    public static ScanMeshPiece Weld(IReadOnlyList<ScanMeshPiece> pieces, Guid id)
    {
        var index = new Dictionary<Vector3, int>();
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var surfaces = new List<ScanSurface>();
        foreach (var piece in pieces)
        {
            var map = new int[piece.Positions.Length];
            for (var i = 0; i < map.Length; i++)
            {
                if (!index.TryGetValue(piece.Positions[i], out var at))
                {
                    at = positions.Count;
                    index[piece.Positions[i]] = at;
                    positions.Add(piece.Positions[i]);
                    normals.Add(piece.Normals[i]);
                }
                map[i] = at;
            }
            for (var t = 0; t < piece.TriangleCount; t++)
            {
                int a = map[piece.Indices[t * 3]], b = map[piece.Indices[t * 3 + 1]], c = map[piece.Indices[t * 3 + 2]];
                if (a == b || b == c || c == a)
                {
                    continue;
                }
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                surfaces.Add(piece.FaceSurfaces?[t] ?? ScanSurface.None);
            }
        }
        return new ScanMeshPiece(id, positions.ToArray(), normals.ToArray(), indices.ToArray(),
            surfaces.Any(s => s != ScanSurface.None) ? surfaces.ToArray() : null);
    }

    /// <summary>
    /// Drops every connected island smaller than <paramref name="minArea"/> (m2): the confetti that stray depth
    /// readings leave floating in the air. Real things in a room touch something larger and stay.
    /// </summary>
    public static ScanMeshPiece DropIslands(ScanMeshPiece piece, float minArea)
    {
        var parent = Enumerable.Range(0, piece.Positions.Length).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                x = parent[x] = parent[parent[x]];
            }
            return x;
        }
        for (var t = 0; t < piece.TriangleCount; t++)
        {
            var a = Find(piece.Indices[t * 3]);
            parent[Find(piece.Indices[t * 3 + 1])] = a;
            parent[Find(piece.Indices[t * 3 + 2])] = a;
        }

        var area = new Dictionary<int, float>();
        for (var t = 0; t < piece.TriangleCount; t++)
        {
            var root = Find(piece.Indices[t * 3]);
            var p = piece.Positions[piece.Indices[t * 3]];
            var cross = Vector3.Cross(piece.Positions[piece.Indices[t * 3 + 1]] - p, piece.Positions[piece.Indices[t * 3 + 2]] - p);
            area[root] = area.GetValueOrDefault(root) + cross.Length() * 0.5f;
        }

        var keep = new List<int>();
        for (var t = 0; t < piece.TriangleCount; t++)
        {
            if (area[Find(piece.Indices[t * 3])] >= minArea)
            {
                keep.Add(t);
            }
        }
        if (keep.Count == piece.TriangleCount)
        {
            return piece;
        }
        // Unused vertices are left in: the simplifier and the atlas only ever walk triangles.
        return new ScanMeshPiece(piece.Id, piece.Positions, piece.Normals,
            keep.SelectMany(t => new[] { piece.Indices[t * 3], piece.Indices[t * 3 + 1], piece.Indices[t * 3 + 2] }).ToArray(),
            piece.FaceSurfaces is { } surfaces ? keep.Select(t => surfaces[t]).ToArray() : null);
    }

    /// <summary>A symmetric 4x4 sum of squared distances to planes, with how many planes went in.</summary>
    private struct Quadric
    {
        public double A2, AB, AC, AD, B2, BC, BD, C2, CD, D2, Count;

        public static Quadric FromPlane(Vector3 n, float d, double weight)
        {
            return new Quadric
            {
                A2 = n.X * n.X * weight, AB = n.X * n.Y * weight, AC = n.X * n.Z * weight, AD = n.X * d * weight,
                B2 = n.Y * n.Y * weight, BC = n.Y * n.Z * weight, BD = n.Y * d * weight,
                C2 = n.Z * n.Z * weight, CD = n.Z * d * weight, D2 = (double)d * d * weight, Count = weight,
            };
        }

        public void Add(in Quadric q)
        {
            A2 += q.A2; AB += q.AB; AC += q.AC; AD += q.AD; B2 += q.B2; BC += q.BC; BD += q.BD;
            C2 += q.C2; CD += q.CD; D2 += q.D2; Count += q.Count;
        }

        /// <summary>Mean squared distance from p to the planes.</summary>
        public double Error(Vector3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;
            var sum = A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x
                + B2 * y * y + 2 * BC * y * z + 2 * BD * y + C2 * z * z + 2 * CD * z + D2;
            return Math.Max(0, sum) / Math.Max(Count, 1e-9);
        }
    }

    private sealed class Collapser
    {
        private readonly List<Vector3> _positions;
        private readonly int[] _faces;
        private readonly ScanSurface[]? _surfaces;
        private readonly bool[] _faceAlive;
        private readonly bool[] _vertexAlive;
        private readonly int[] _version;
        private readonly List<int>[] _vertexFaces;
        private readonly Quadric[] _quadrics;
        private readonly PriorityQueue<(int U, int V, int VersionU, int VersionV, Vector3 Target), double> _queue = new();

        public Collapser(ScanMeshPiece piece)
        {
            _positions = new List<Vector3>(piece.Positions);
            _faces = (int[])piece.Indices.Clone();
            _surfaces = piece.FaceSurfaces;
            var faceCount = _faces.Length / 3;
            _faceAlive = Enumerable.Repeat(true, faceCount).ToArray();
            _vertexAlive = Enumerable.Repeat(true, _positions.Count).ToArray();
            _version = new int[_positions.Count];
            _vertexFaces = new List<int>[_positions.Count];
            _quadrics = new Quadric[_positions.Count];
            for (var v = 0; v < _vertexFaces.Length; v++)
            {
                _vertexFaces[v] = new List<int>(6);
            }

            var edgeFaces = new Dictionary<(int, int), int>();
            for (var f = 0; f < faceCount; f++)
            {
                var (a, b, c) = (_faces[f * 3], _faces[f * 3 + 1], _faces[f * 3 + 2]);
                var cross = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                if (cross.LengthSquared() < 1e-20f)
                {
                    _faceAlive[f] = false; // degenerate: no plane to keep, and it would only block collapses
                    continue;
                }
                var n = Vector3.Normalize(cross);
                var plane = Quadric.FromPlane(n, -Vector3.Dot(n, _positions[a]), 1);
                foreach (var v in new[] { a, b, c })
                {
                    _quadrics[v].Add(plane);
                    _vertexFaces[v].Add(f);
                }
                foreach (var (u, w) in new[] { (a, b), (b, c), (c, a) })
                {
                    var key = u < w ? (u, w) : (w, u);
                    edgeFaces[key] = edgeFaces.GetValueOrDefault(key) + 1;
                }
            }

            // Open edges: a plane through the edge, upright to its face, holds the outline in place.
            for (var f = 0; f < faceCount; f++)
            {
                if (!_faceAlive[f])
                {
                    continue;
                }
                var (a, b, c) = (_faces[f * 3], _faces[f * 3 + 1], _faces[f * 3 + 2]);
                var faceNormal = Normal(a, b, c);
                foreach (var (u, w) in new[] { (a, b), (b, c), (c, a) })
                {
                    if (edgeFaces[u < w ? (u, w) : (w, u)] != 1)
                    {
                        continue;
                    }
                    var along = _positions[w] - _positions[u];
                    var side = Vector3.Cross(along, faceNormal);
                    if (side.LengthSquared() < 1e-20f)
                    {
                        continue;
                    }
                    side = Vector3.Normalize(side);
                    var plane = Quadric.FromPlane(side, -Vector3.Dot(side, _positions[u]), 4);
                    _quadrics[u].Add(plane);
                    _quadrics[w].Add(plane);
                }
            }

            foreach (var (u, w) in edgeFaces.Keys)
            {
                Push(u, w);
            }
        }

        public void Run(float maxError)
        {
            var limit = (double)maxError * maxError;
            while (_queue.TryDequeue(out var edge, out var cost))
            {
                if (cost > limit)
                {
                    break;
                }
                if (!_vertexAlive[edge.U] || !_vertexAlive[edge.V]
                    || _version[edge.U] != edge.VersionU || _version[edge.V] != edge.VersionV)
                {
                    continue; // stale: one end has moved since this was queued
                }
                TryCollapse(edge.U, edge.V, edge.Target);
            }
        }

        public ScanMeshPiece Result(Guid id)
        {
            var map = new int[_positions.Count];
            Array.Fill(map, -1);
            var positions = new List<Vector3>();
            var indices = new List<int>();
            var surfaces = new List<ScanSurface>();
            for (var f = 0; f < _faceAlive.Length; f++)
            {
                if (!_faceAlive[f])
                {
                    continue;
                }
                for (var k = 0; k < 3; k++)
                {
                    var v = _faces[f * 3 + k];
                    if (map[v] < 0)
                    {
                        map[v] = positions.Count;
                        positions.Add(_positions[v]);
                    }
                    indices.Add(map[v]);
                }
                surfaces.Add(_surfaces?[f] ?? ScanSurface.None);
            }

            // Area-weighted normals of what is left: the old ones described triangles that are gone.
            var normals = new Vector3[positions.Count];
            for (var t = 0; t < indices.Count; t += 3)
            {
                var cross = Vector3.Cross(positions[indices[t + 1]] - positions[indices[t]], positions[indices[t + 2]] - positions[indices[t]]);
                normals[indices[t]] += cross;
                normals[indices[t + 1]] += cross;
                normals[indices[t + 2]] += cross;
            }
            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].LengthSquared() > 1e-20f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
            }
            return new ScanMeshPiece(id, positions.ToArray(), normals, indices.ToArray(), _surfaces != null ? surfaces.ToArray() : null);
        }

        private void Push(int u, int v)
        {
            var q = _quadrics[u];
            q.Add(_quadrics[v]);
            // The best of the two ends and their middle: nearly as good as solving for the optimum, and never
            // wanders off past the edge on a flat patch where the optimum is undefined.
            var pu = _positions[u];
            var pv = _positions[v];
            var mid = (pu + pv) * 0.5f;
            var (target, cost) = (mid, q.Error(mid));
            var eu = q.Error(pu);
            if (eu < cost)
            {
                (target, cost) = (pu, eu);
            }
            var ev = q.Error(pv);
            if (ev < cost)
            {
                (target, cost) = (pv, ev);
            }
            _queue.Enqueue((u, v, _version[u], _version[v], target), cost);
        }

        private void TryCollapse(int u, int v, Vector3 target)
        {
            // Link condition: u and v may share only the neighbours across their shared faces, or the
            // collapse pinches the surface into a non-manifold fin.
            var shared = 0;
            foreach (var f in _vertexFaces[u])
            {
                if (_faceAlive[f] && HasVertex(f, v))
                {
                    shared++;
                }
            }
            var neighboursU = Neighbours(u);
            var neighboursV = Neighbours(v);
            neighboursU.IntersectWith(neighboursV);
            if (neighboursU.Count > shared)
            {
                return;
            }

            // No triangle may turn over (or go to a sliver) by the move.
            foreach (var vertex in new[] { u, v })
            {
                foreach (var f in _vertexFaces[vertex])
                {
                    if (!_faceAlive[f] || (HasVertex(f, u) && HasVertex(f, v)))
                    {
                        continue;
                    }
                    var (a, b, c) = (_faces[f * 3], _faces[f * 3 + 1], _faces[f * 3 + 2]);
                    var before = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                    Vector3 P(int i) => i == u || i == v ? target : _positions[i];
                    var after = Vector3.Cross(P(b) - P(a), P(c) - P(a));
                    if (after.LengthSquared() < 1e-20f
                        || Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after)) < 0.5f)
                    {
                        return;
                    }
                }
            }

            _positions[v] = target;
            _quadrics[v].Add(_quadrics[u]);
            foreach (var f in _vertexFaces[u])
            {
                if (!_faceAlive[f])
                {
                    continue;
                }
                if (HasVertex(f, v))
                {
                    _faceAlive[f] = false;
                    continue;
                }
                for (var k = 0; k < 3; k++)
                {
                    if (_faces[f * 3 + k] == u)
                    {
                        _faces[f * 3 + k] = v;
                    }
                }
                _vertexFaces[v].Add(f);
            }
            _vertexFaces[v].RemoveAll(f => !_faceAlive[f]);
            _vertexFaces[u].Clear();
            _vertexAlive[u] = false;
            _version[v]++;

            foreach (var n in Neighbours(v))
            {
                Push(v, n);
            }
        }

        private HashSet<int> Neighbours(int vertex)
        {
            var set = new HashSet<int>();
            foreach (var f in _vertexFaces[vertex])
            {
                if (!_faceAlive[f])
                {
                    continue;
                }
                for (var k = 0; k < 3; k++)
                {
                    var n = _faces[f * 3 + k];
                    if (n != vertex)
                    {
                        set.Add(n);
                    }
                }
            }
            return set;
        }

        private bool HasVertex(int f, int vertex) =>
            _faces[f * 3] == vertex || _faces[f * 3 + 1] == vertex || _faces[f * 3 + 2] == vertex;

        private Vector3 Normal(int a, int b, int c) =>
            Vector3.Normalize(Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]));
    }
}
