using System.Numerics;

namespace ManaLidar;

/// <summary>
/// How well the photos so far cover a mesh, per vertex: what a scanner can paint over the live mesh so you can
/// see where to point next. The mesh can be there with no good photo of it, which bakes grey or blurry.
/// </summary>
public static class ScanCoverage
{
    /// <summary>At or under this distance a head-on photo counts as fully good; further away counts for less.</summary>
    public const float GoodDistance = 1.5f;

    /// <summary>0 when no photo saw the point; up to 1 for a close, head-on view. The best photo counts.</summary>
    public static float Quality(ScanKeyframe keyframe, Vector3 point, Vector3 normal)
    {
        if (!ScanColorizer.Sees(keyframe, point, normal, out _, out var distance, out var facing))
        {
            return 0f;
        }
        return facing * MathF.Min(1f, GoodDistance / MathF.Max(distance, 0.1f));
    }

    /// <summary>The best quality any of <paramref name="keyframes"/> gives each vertex of a piece.</summary>
    public static float[] Score(ScanMeshPiece piece, IReadOnlyList<ScanKeyframe> keyframes)
    {
        var scores = new float[piece.Positions.Length];
        Improve(piece, scores, keyframes);
        return scores;
    }

    /// <summary>
    /// Raises <paramref name="scores"/> where any of <paramref name="keyframes"/> sees a vertex better; true if
    /// anything changed. A new photo only ever improves coverage, so it can be folded in without a recount.
    /// </summary>
    public static bool Improve(ScanMeshPiece piece, float[] scores, IReadOnlyList<ScanKeyframe> keyframes)
    {
        var (centre, radius) = ScanColorizer.Bounds(piece.Positions);
        // Scoring runs live on the phone, so only the photos most likely to show the piece well are checked, and a
        // vertex already well covered is not worth another look. Ranked by how squarely each photo looks at the
        // piece, not by distance alone: past a couple of hundred photos the nearest ones are often taken close by
        // but pointing elsewhere, and choosing those turned well covered areas red again as ARKit refined them.
        var facing = Vector3.Zero;
        foreach (var n in piece.Normals)
        {
            facing += n;
        }
        facing = facing.LengthSquared() > 1e-6f ? Vector3.Normalize(facing) : Vector3.Zero;
        var candidates = ScanColorizer.Candidates(centre, radius, keyframes)
            .OrderByDescending(k => Promise(k, centre, facing))
            .Take(MaxPhotosPerPiece)
            .ToArray();
        var changed = false;
        for (var i = 0; i < scores.Length; i++)
        {
            foreach (var keyframe in candidates)
            {
                if (scores[i] >= WellCovered)
                {
                    break;
                }
                var quality = Quality(keyframe, piece.Positions[i], piece.Normals[i]);
                if (quality > scores[i])
                {
                    scores[i] = quality;
                    changed = true;
                }
            }
        }
        return changed;
    }

    // Roughly how good a view of a piece a photo is: aimed at it, facing it (when the piece faces one way), close.
    private static float Promise(ScanKeyframe keyframe, Vector3 centre, Vector3 facing)
    {
        var toPiece = centre - keyframe.Position;
        var distance = MathF.Max(toPiece.Length(), 0.1f);
        var aimed = MathF.Max(0.05f, Vector3.Dot(keyframe.Forward, toPiece / distance));
        var faced = facing == Vector3.Zero ? 1f : MathF.Max(0.2f, -Vector3.Dot(facing, toPiece / distance));
        return aimed * faced * MathF.Min(1f, GoodDistance / distance);
    }

    /// <summary>A score this high is as green as it gets; checking more photos cannot change what is shown.</summary>
    public const float WellCovered = 0.7f;
    public const int MaxPhotosPerPiece = 48;

    /// <summary>Red where nothing has been photographed, amber where only from afar or askew, green when well covered.</summary>
    public static Vector4 Colour(float score)
    {
        var red = new Vector4(1.0f, 0.25f, 0.2f, 1);
        var amber = new Vector4(1.0f, 0.75f, 0.2f, 1);
        var green = new Vector4(0.3f, 0.95f, 0.5f, 1);
        return score < 0.4f
            ? Vector4.Lerp(red, amber, score / 0.4f)
            : Vector4.Lerp(amber, green, MathF.Min(1, (score - 0.4f) / 0.3f));
    }
}
