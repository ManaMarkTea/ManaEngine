using ManaGeoTools.Code.Geometry;
using ManaUI;

namespace ManaTests;

/// <summary>Inland water from the vector tiles: polygons with islands cut into triangles that cover exactly the water.</summary>
public static class PlanetWaterChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        // A 10 x 10 square with a 4 x 4 island in it: the triangles should cover 100 - 16.
        double[] square = [0, 0, 10, 0, 10, 10, 0, 10, 3, 3, 3, 7, 7, 7, 7, 3];
        var triangles = Earcut.Triangulate(square, [4]);
        check("a square with an island cuts into triangles", triangles.Count > 0 && triangles.Count % 3 == 0, $"{triangles.Count}");
        check("which cover the water and not the island", Math.Abs(Area(square, triangles) - 84.0) < 1e-9, $"{Area(square, triangles)}");

        // A concave L, wound the other way round: still covered exactly.
        double[] shape = [0, 0, 0, 4, 2, 4, 2, 2, 4, 2, 4, 0];
        var l = Earcut.Triangulate(shape, []);
        check("a concave shape either way round is covered exactly", Math.Abs(Area(shape, l) - 12.0) < 1e-9, $"{Area(shape, l)}");
    }

    private static double Area(double[] xy, List<int> triangles)
    {
        var total = 0.0;
        for (var t = 0; t < triangles.Count; t += 3)
        {
            var (a, b, c) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            total += Math.Abs((xy[b * 2] - xy[a * 2]) * (xy[c * 2 + 1] - xy[a * 2 + 1]) - (xy[b * 2 + 1] - xy[a * 2 + 1]) * (xy[c * 2] - xy[a * 2])) * 0.5;
        }
        return total;
    }
}
