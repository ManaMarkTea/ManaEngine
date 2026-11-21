using ManaGeoTools.Code.Osm;
using ManaUI;

namespace ManaTests;

public static class PlanetRoadChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        // Transportation tile with a buffered ramp bridge, tunnel and ordinary motorway.
        var tile = Convert.FromHexString(
            "1A82010A0E7472616E73706F72746174696F6E12121206000001010202180222060913080A2800"
            + "12101204000002031802220609130E0A2800120E12020000180222060913140A28001A05636C617373"
            + "1A0472616D701A076272756E6E656C220A0A086D6F746F727761792202200122080A0662726964676522080A0674756E6E656C2810");
        var lines = OsmRoadTiles.Parse(tile, 14, 1234, 5678);
        var bounds = OsmRoadTiles.Bounds(14, 1234, 5678);
        check("the decoder preserves a numeric ramp and bridge tag and omits the tunnel",
            lines is [{ Ramp: true, Bridge: true }, { Ramp: false, Bridge: false }], $"{lines.Count} roads");
        check("buffered road endpoints are clipped at the tile seam",
            lines.All(line => line.Points[0].Longitude == bounds.West && line.Points[^1].Longitude < bounds.East), "");
        check("only the west or north tile edge owns a road running along a seam",
            !OsmRoadTiles.ClipRoad([(16, 0), (16, 15)], 16).Any()
            && OsmRoadTiles.ClipRoad([(0, 0), (0, 15)], 16).Count() == 1, "");

        var west = new RoadLine(RoadClass.Trunk, [(0, 0), (1, 0)]);
        var east = new RoadLine(RoadClass.Trunk, [(2, 0), (1, 0)]);
        var duplicate = west with { Points = [(1, 0), (0, 0)] };
        var straight = OsmRoadCleanup.Join([west, east, duplicate]);
        check("reversed duplicates are dropped and unbranched pieces are joined",
            straight.Count == 1 && straight[0].Points.SequenceEqual(new (double, double)[] { (0, 0), (1, 0), (2, 0) }), "");

        var ramp = new RoadLine(RoadClass.Trunk, [(1, 0), (2, 1)], Ramp: true);
        var parallel = new RoadLine(RoadClass.Trunk, [(0, 0.01), (2, 0.01)]);
        check("a junction, changed structure or parallel carriageway stays independent",
            OsmRoadCleanup.Join([west, east, ramp, parallel]).Count == 4
            && OsmRoadCleanup.Join([west, east with { Bridge = true }]).Count == 2, "");
    }
}
