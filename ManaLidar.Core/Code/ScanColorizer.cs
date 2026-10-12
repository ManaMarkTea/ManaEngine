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
        var candidates = KeyframesThatCouldSee(piece, keyframes);

        Parallel.For(0, colors.Length, i =>
        {
            var p = piece.Positions[i];
            var n = piece.Normals[i];
            var sum = Vector3.Zero;
            var weightSum = 0f;
            foreach (var keyframe in candidates)
            {
                if (!keyframe.Project(p, out var pixel, out var distance))
                {
                    continue;
                }
                // A margin keeps clear of the lens's blurriest, most distorted edge.
                const float margin = 2f;
                if (pixel.X < margin || pixel.Y < margin || pixel.X > keyframe.Width - margin || pixel.Y > keyframe.Height - margin)
                {
                    continue;
                }
                var toCamera = keyframe.Position - p;
                var facing = Vector3.Dot(n, toCamera / MathF.Max(toCamera.Length(), 1e-4f));
                if (facing < 0.15f)
                {
                    continue;
                }
                var depth = keyframe.DepthAt(pixel);
                if (!float.IsNaN(depth) && depth > 0 && distance - depth > 0.04f + 0.03f * distance)
                {
                    continue; // something nearer the camera hid this point in that photo
                }

                // Squared again so the best view dominates: an average of many slightly misaligned photos is a blur.
                var view = facing * facing * facing / (distance * distance + 0.05f);
                var sharpness = keyframe.Sharpness + 1f; // + 1: a photo of a blank wall is sharp but has no edges to show it
                var weight = view * view * sharpness * sharpness;
                sum += keyframe.Sample(pixel) * weight;
                weightSum += weight;
            }

            if (weightSum > 0)
            {
                colors[i] = sum / weightSum;
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

    // A bounding-sphere check first: a room scan has hundreds of photos, and any one piece is in few of them.
    private static List<ScanKeyframe> KeyframesThatCouldSee(ScanMeshPiece piece, IReadOnlyList<ScanKeyframe> keyframes)
    {
        var result = new List<ScanKeyframe>();
        if (piece.Positions.Length == 0)
        {
            return result;
        }
        var min = piece.Positions[0];
        var max = min;
        foreach (var p in piece.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var centre = (min + max) * 0.5f;
        var radius = (max - min).Length() * 0.5f;

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
