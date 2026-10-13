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
        // Scoring runs live on the phone: the nearest photos decide it, and a vertex already well covered
        // is not worth another look.
        var candidates = ScanColorizer.Candidates(centre, radius, keyframes)
            .OrderBy(k => Vector3.DistanceSquared(k.Position, centre))
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
