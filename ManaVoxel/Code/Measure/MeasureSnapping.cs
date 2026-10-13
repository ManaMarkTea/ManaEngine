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
