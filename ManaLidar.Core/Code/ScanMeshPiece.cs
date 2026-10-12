using System.Numerics;

namespace ManaLidar;

/// <summary>What ARKit says a face is (ARMeshClassification, same numbers).</summary>
public enum ScanSurface : byte
{
    None = 0,
    Wall = 1,
    Floor = 2,
    Ceiling = 3,
    Table = 4,
    Seat = 5,
    Window = 6,
    Door = 7,
}

/// <summary>
/// One ARMeshAnchor's mesh, already in world space (metres, Y up -- the same frame glTF uses).
/// ARKit replaces an anchor's whole mesh on each update, so a piece is swapped rather than edited.
/// </summary>
public sealed class ScanMeshPiece
{
    public ScanMeshPiece(Guid id, Vector3[] positions, Vector3[] normals, int[] indices, ScanSurface[]? faceSurfaces = null)
    {
        if (normals.Length != positions.Length)
        {
            throw new ArgumentException("One normal per position.", nameof(normals));
        }
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("Indices must be whole triangles.", nameof(indices));
        }
        if (faceSurfaces != null && faceSurfaces.Length != indices.Length / 3)
        {
            throw new ArgumentException("One surface per triangle.", nameof(faceSurfaces));
        }

        Id = id;
        Positions = positions;
        Normals = normals;
        Indices = indices;
        FaceSurfaces = faceSurfaces;
    }

    public Guid Id { get; }
    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public int[] Indices { get; }
    public ScanSurface[]? FaceSurfaces { get; }

    public int TriangleCount => Indices.Length / 3;

    /// <summary>Builds a piece from anchor-local data and the anchor's local-to-world matrix.</summary>
    public static ScanMeshPiece FromLocal(Guid id, Vector3[] localPositions, Vector3[] localNormals, int[] indices,
        Matrix4x4 anchorToWorld, ScanSurface[]? faceSurfaces = null)
    {
        var positions = new Vector3[localPositions.Length];
        var normals = new Vector3[localNormals.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = Vector3.Transform(localPositions[i], anchorToWorld);
        }
        for (var i = 0; i < normals.Length; i++)
        {
            // Anchors are rigid (no scale), so the rotation part carries normals as is.
            var n = Vector3.TransformNormal(localNormals[i], anchorToWorld);
            normals[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
        }
        return new ScanMeshPiece(id, positions, normals, indices, faceSurfaces);
    }
}
