using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Colours mesh vertices from the keyframes: each vertex is projected into every photo that could see it, photos
/// where the LiDAR depth says something stood in front are skipped, and the rest are blended, favouring
/// close, head-on views (which are both sharper and least smeared by tracking drift).
/// </summary>
public static class ScanColorizer
{
    /// <summary>Colour for a vertex no keyframe saw, by what ARKit thinks the surface is.</summary>
    public static Vector3 SurfaceColor(ScanSurface surface) => surface switch
    {
        ScanSurface.Wall => new Vector3(0.78f, 0.76f, 0.72f),
        ScanSurface.Floor => new Vector3(0.55f, 0.47f, 0.38f),
        ScanSurface.Ceiling => new Vector3(0.88f, 0.88f, 0.86f),
        ScanSurface.Table => new Vector3(0.62f, 0.45f, 0.30f),
        ScanSurface.Seat => new Vector3(0.40f, 0.48f, 0.62f),
        ScanSurface.Window => new Vector3(0.55f, 0.75f, 0.90f),
        ScanSurface.Door => new Vector3(0.60f, 0.42f, 0.28f),
        _ => new Vector3(0.65f, 0.65f, 0.65f),
    };

    /// <summary>
    /// One colour per vertex of <paramref name="piece"/>, 0..1. <paramref name="seen"/> (same length) is set for
    /// vertices some keyframe saw; the rest get their surface colour.
    /// </summary>
    public static Vector3[] Colorize(ScanMeshPiece piece, IReadOnlyList<ScanKeyframe> keyframes, bool[]? seen = null)
    {
        var colors = new Vector3[piece.Positions.Length];
        var surfaces = VertexSurfaces(piece);
        var (centre, radius) = Bounds(piece.Positions);
        var candidates = Candidates(centre, radius, keyframes);

        Parallel.For(0, colors.Length, i =>
        {
            if (TrySample(piece.Positions[i], piece.Normals[i], candidates, out colors[i]))
            {
                if (seen != null)
                {
                    seen[i] = true;
                }
            }
            else
            {
                colors[i] = SurfaceColor(surfaces[i]);
            }
        });
        return colors;
    }

    /// <summary>The colour of a surface point facing <paramref name="normal"/>, blended from the photos that saw it.</summary>
    public static bool TrySample(Vector3 point, Vector3 normal, IReadOnlyList<ScanKeyframe> candidates, out Vector3 color)
    {
        var sum = Vector3.Zero;
        var weightSum = 0f;
        foreach (var keyframe in candidates)
        {
            if (!Sees(keyframe, point, normal, out var pixel, out var distance, out var facing))
            {
                continue;
            }

            // Squared again so the best view dominates: an average of many slightly misaligned photos is a blur.
            var view = facing * facing * facing / (distance * distance + 0.05f);
            var sharpness = keyframe.Sharpness + 1f; // + 1: a photo of a blank wall is sharp but has no edges to show it
            var weight = view * view * sharpness * sharpness;
            sum += keyframe.Sample(pixel) * weight;
            weightSum += weight;
        }

        color = weightSum > 0 ? sum / weightSum : default;
        return weightSum > 0;
    }

    /// <summary>
    /// Whether a photo shows a surface point: in frame (clear of the lens's blurriest edge), facing the camera,
    /// and not hidden behind something the LiDAR saw nearer. The one rule colouring and coverage both use.
    /// </summary>
    public static bool Sees(ScanKeyframe keyframe, Vector3 point, Vector3 normal, out Vector2 pixel, out float distance, out float facing)
    {
        facing = 0;
        if (!keyframe.Project(point, out pixel, out distance))
        {
            return false;
        }
        const float margin = 2f;
        if (pixel.X < margin || pixel.Y < margin || pixel.X > keyframe.Width - margin || pixel.Y > keyframe.Height - margin)
        {
            return false;
        }
        var toCamera = keyframe.Position - point;
        facing = Vector3.Dot(normal, toCamera / MathF.Max(toCamera.Length(), 1e-4f));
        if (facing < 0.15f)
        {
            return false;
        }
        var depth = keyframe.DepthAt(pixel);
        // Something nearer the camera hid this point in that photo.
        return float.IsNaN(depth) || depth <= 0 || distance - depth <= 0.04f + 0.03f * distance;
    }

    /// <summary>Each vertex takes the surface of the first triangle that uses it.</summary>
    public static ScanSurface[] VertexSurfaces(ScanMeshPiece piece)
    {
        var surfaces = new ScanSurface[piece.Positions.Length];
        if (piece.FaceSurfaces is not { } faces)
        {
            return surfaces;
        }
        for (var f = 0; f < faces.Length; f++)
        {
            for (var k = 0; k < 3; k++)
            {
                ref var s = ref surfaces[piece.Indices[f * 3 + k]];
                if (s == ScanSurface.None)
                {
                    s = faces[f];
                }
            }
        }
        return surfaces;
    }

    public static (Vector3 Centre, float Radius) Bounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return min.X > max.X ? (Vector3.Zero, 0f) : ((min + max) * 0.5f, (max - min).Length() * 0.5f);
    }

    /// <summary>
    /// The keyframes whose view takes in some of a bounding sphere: a room scan has hundreds of photos, and any one
    /// part of it is in few of them.
    /// </summary>
    public static List<ScanKeyframe> Candidates(Vector3 centre, float radius, IReadOnlyList<ScanKeyframe> keyframes)
    {
        var result = new List<ScanKeyframe>();
        foreach (var keyframe in keyframes)
        {
            var c = Vector3.Transform(centre, keyframe.WorldToCamera);
            var distance = -c.Z;
            if (distance + radius <= 0.05f)
            {
                continue;
            }
            var d = MathF.Max(distance, 0.05f);
            var padX = radius * keyframe.Fx / d;
            var padY = radius * keyframe.Fy / d;
            var u = keyframe.Cx + keyframe.Fx * c.X / d;
            var v = keyframe.Cy - keyframe.Fy * c.Y / d;
            if (distance < radius
                || (u + padX >= 0 && u - padX <= keyframe.Width && v + padY >= 0 && v - padY <= keyframe.Height))
            {
                result.Add(keyframe);
            }
        }
        return result;
    }
}
