using System.Numerics;

namespace ManaVoxel.Code.Measure;

/// <summary>What a measuring point landed on, strongest first in the order snapping tries them.</summary>
public enum MeasureSnapKind
{
    Free,
    Grid,
    Face,
    Edge,
    Midpoint,
    Vertex,
}

/// <summary>The snap targets a person can switch on and off.</summary>
[Flags]
public enum MeasureSnapTargets
{
    None = 0,
    Vertex = 1,
    Midpoint = 2,
    Edge = 4,
    Face = 8,
    Grid = 16,
    All = Vertex | Midpoint | Edge | Face | Grid,
}

/// <summary>A point to measure from or to, the surface's normal there, and what it snapped to.</summary>
public readonly record struct MeasureSnap(Vector3 Position, Vector3 Normal, MeasureSnapKind Kind);

/// <summary>
/// Picks the point a click means from the triangle under the pointer: a corner or an edge's middle when the pointer
/// is within a few pixels of one on screen, a point on an edge when it is that close to the edge, else the surface.
/// Measured in screen pixels rather than metres, so snapping feels the same near and far.
/// </summary>
public static class MeasureSnapping
{
    /// <param name="edges">Which edges are real (bit 0 a-b, 1 b-c, 2 c-a); a diagonal across a flat face is not snapped to.</param>
    public static MeasureSnap FromTriangle(
        Vector3 hit, Vector3 normal, Vector3 a, Vector3 b, Vector3 c,
        Vector2 pointer, Func<Vector3, Vector2?> toScreen, float pixelRadius, MeasureSnapTargets targets, int edges = 7)
    {
        var ends = new[] { (a, b), (b, c), (c, a) }.Where((_, i) => (edges & (1 << i)) != 0).ToArray();
        if (targets.HasFlag(MeasureSnapTargets.Vertex)
            && Nearest(new[] { a, b, c }, pointer, toScreen, pixelRadius) is { } corner)
        {
            return new MeasureSnap(corner, normal, MeasureSnapKind.Vertex);
        }
        if (targets.HasFlag(MeasureSnapTargets.Midpoint)
            && Nearest(ends.Select(e => (e.Item1 + e.Item2) * 0.5f).ToArray(), pointer, toScreen, pixelRadius) is { } middle)
        {
            return new MeasureSnap(middle, normal, MeasureSnapKind.Midpoint);
        }
        if (targets.HasFlag(MeasureSnapTargets.Edge)
            && Nearest(ends.Select(e => ClosestOnSegment(hit, e.Item1, e.Item2)).ToArray(),
                pointer, toScreen, pixelRadius * 0.6f) is { } onEdge)
        {
            return new MeasureSnap(onEdge, normal, MeasureSnapKind.Edge);
        }
        return new MeasureSnap(hit, normal, targets.HasFlag(MeasureSnapTargets.Face) ? MeasureSnapKind.Face : MeasureSnapKind.Free);
    }

    /// <summary>Where the pointer meets the ground (y = 0) when it is over nothing, on the snap grid if one is given.</summary>
    public static MeasureSnap? OnGround(Vector3 rayOrigin, Vector3 rayDirection, float gridStep)
    {
        if (MathF.Abs(rayDirection.Y) < 1e-6f)
        {
            return null;
        }
        var travel = -rayOrigin.Y / rayDirection.Y;
        if (travel <= 0)
        {
            return null;
        }
        var point = rayOrigin + rayDirection * travel;
        if (gridStep <= 0)
        {
            return new MeasureSnap(point, Vector3.UnitY, MeasureSnapKind.Free);
        }
        return new MeasureSnap(
            new Vector3(MathF.Round(point.X / gridStep) * gridStep, 0, MathF.Round(point.Z / gridStep) * gridStep),
            Vector3.UnitY, MeasureSnapKind.Grid);
    }

    /// <summary>The foot of <paramref name="point"/> on a plane: a wall's corner dropped to the floor's height.</summary>
    public static Vector3 OntoPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        var n = Vector3.Normalize(planeNormal);
        return point - n * Vector3.Dot(point - planePoint, n);
    }

    /// <summary>
    /// Which edges of triangle <paramref name="triangle"/> (its first index) are real: bit 0 a-b, 1 b-c, 2 c-a. An
    /// edge shared with a neighbour in the same plane is only the diagonal a flat face was cut along. Neighbours are
    /// matched by position, since a textured mesh repeats its vertices along UV seams. Mana's own picker does the
    /// same (ManaScenePicker.CreaseEdges); this copy serves the phone apps, which have no engine.
    /// </summary>
    public static int CreaseEdges(IReadOnlyList<Vector3> positions, IReadOnlyList<uint> indices, int triangle)
    {
        var corner = new[] { positions[(int)indices[triangle]], positions[(int)indices[triangle + 1]], positions[(int)indices[triangle + 2]] };
        var normal = Vector3.Cross(corner[1] - corner[0], corner[2] - corner[0]);
        if (normal.LengthSquared() < 1e-20f)
        {
            return 7;
        }
        normal = Vector3.Normalize(normal);
        var creases = 7;
        for (var i = 0; i + 2 < indices.Count && creases != 0; i += 3)
        {
            if (i == triangle)
            {
                continue;
            }
            var p0 = positions[(int)indices[i]];
            var p1 = positions[(int)indices[i + 1]];
            var p2 = positions[(int)indices[i + 2]];
            var other = Vector3.Cross(p1 - p0, p2 - p0);
            if (other.LengthSquared() < 1e-20f || Vector3.Dot(Vector3.Normalize(other), normal) < 0.9995f)
            {
                continue;
            }
            for (var e = 0; e < 3; e++)
            {
                var u = corner[e];
                var v = corner[(e + 1) % 3];
                if ((p0 == u || p1 == u || p2 == u) && (p0 == v || p1 == v || p2 == v))
                {
                    creases &= ~(1 << e);
                }
            }
        }
        return creases;
    }

    public static Vector3 ClosestOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        var along = b - a;
        var lengthSquared = along.LengthSquared();
        if (lengthSquared < 1e-12f)
        {
            return a;
        }
        return a + along * Math.Clamp(Vector3.Dot(point - a, along) / lengthSquared, 0f, 1f);
    }

    private static Vector3? Nearest(Vector3[] candidates, Vector2 pointer, Func<Vector3, Vector2?> toScreen, float pixelRadius)
    {
        Vector3? best = null;
        var bestDistance = pixelRadius;
        foreach (var candidate in candidates)
        {
            if (toScreen(candidate) is not { } screen)
            {
                continue;
            }
            var distance = Vector2.Distance(screen, pointer);
            if (distance <= bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }
}
