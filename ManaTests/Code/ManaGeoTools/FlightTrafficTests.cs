using System.Text;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Flights;
using ManaGeoTools.Code.Planet;
using ManaUI;

namespace ManaTests;

/// <summary>Live flight traffic: OpenSky's reply read right, and aircraft flown on the right way between replies.</summary>
public static class FlightTrafficChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        const string reply = """
            {"time":1794613785,"states":[
              ["408127","MUK6977 ","United Kingdom",1794613784,1794613784,4.6139,52.0937,8343.9,false,237.2,130.43,-6.18,null,8321.04,"5203",false,0],
              ["aa8c39","UAL300  ","United States",1794613784,1794613784,-122.3738,37.6165,null,true,9.26,118.12,null,null,null,null,true,0],
              ["c822af","","New Zealand",null,1794613778,null,null,1000,false,100,0,0,null,null,null,false,0],
              ["e8027e","LPE2339 ","Chile",1794613501,1794613501,-77.1367,-12.0007,106.68,false,67.88,154.41,-3.58,null,null,null,false,0]
            ]}
            """;
        var snapshot = OpenSkyStates.Parse(Encoding.UTF8.GetBytes(reply));
        check("a row without a position is skipped", snapshot.States.Length == 3, $"{snapshot.States.Length}");
        var first = snapshot.States[0];
        check("callsigns lose their padding", first.Callsign == "MUK6977", first.Callsign);
        check("GPS altitude wins over barometric", MathF.Abs(first.AltitudeMeters - 8321.04f) < 0.01f, $"{first.AltitudeMeters}");
        check("barometric is used when there is no GPS height", MathF.Abs(snapshot.States[2].AltitudeMeters - 106.68f) < 0.01f, $"{snapshot.States[2].AltitudeMeters}");
        check("grounded aircraft read as grounded, with no altitude", snapshot.States[1].OnGround && snapshot.States[1].AltitudeMeters == 0, "");
        check("a missing states list is no aircraft, not a failure", OpenSkyStates.Parse("{\"time\":1,\"states\":null}"u8).States.Length == 0, "");

        // Extended rows carry the emitter category after position_source; it picks the model drawn close up.
        const string extended = """
            {"time":1,"states":[
              ["a1","N123AB","US",1,1,-120.0,37.0,900,false,45,90,0,null,910,null,false,0,2],
              ["a2","UAL1","US",1,1,-120.0,37.0,11000,false,240,90,0,null,11000,null,false,0,6],
              ["a3","HELI1","US",1,1,-120.0,37.0,300,false,50,90,0,null,300,null,false,0,8]
            ]}
            """;
        var kinds = OpenSkyStates.Parse(Encoding.UTF8.GetBytes(extended)).States;
        check("the emitter category is read from extended rows", kinds.Length == 3 && kinds[0].Category == 2 && kinds[1].Category == 6, string.Join(", ", kinds.Select(k => k.Category)));
        check("light aircraft and helicopters are drawn as the light model, heavies as the airliner",
            FlightTrafficModels.KindOf(kinds[0]) == AircraftKind.Light && FlightTrafficModels.KindOf(kinds[2]) == AircraftKind.Light
            && FlightTrafficModels.KindOf(kinds[1]) == AircraftKind.Airliner, "");
        check("with no category, slow and low is light and an airliner on approach stays an airliner",
            FlightTrafficModels.KindOf(first with { SpeedMetersPerSecond = 45, AltitudeMeters = 600 }) == AircraftKind.Light
            && FlightTrafficModels.KindOf(first with { SpeedMetersPerSecond = 72, AltitudeMeters = 600 }) == AircraftKind.Airliner, "");

        // Due east along the equator at 250 m/s for an hour: 900 km, about 8.09 degrees of longitude.
        var east = new FlightState("x", "", 10, 0, 10000, 250, 90, 0, 0, false);
        var (direction, heading) = FlightDeadReckoning.Advance(east, 3600);
        var reached = LatLong.FromPointOnSphere(direction, 1, 0);
        var expected = 10 + 900_000 / PlanetLoader.EarthRadiusMeters * 180 / MathF.PI;
        check("flying east moves east by its distance", MathF.Abs(reached.Longitude - expected) < 0.01f && MathF.Abs(reached.Latitude) < 0.01f, $"{reached}");
        check("and the heading stays a level tangent", MathF.Abs(Vector3.Dot(heading, direction)) < 1e-4f && MathF.Abs(heading.Length() - 1) < 1e-4f, $"{heading}");

        var north = east with { TrackDegrees = 0 };
        var northward = LatLong.FromPointOnSphere(FlightDeadReckoning.Advance(north, 3600).Direction, 1, 0);
        check("flying north moves north", northward.Latitude > 8 && MathF.Abs(northward.Longitude - 10) < 0.01f, $"{northward}");

        var climbing = east with { VerticalRateMetersPerSecond = 10 };
        check("a climb runs for a while, then levels off", FlightDeadReckoning.Altitude(climbing, 600) == 10000 + 10 * FlightDeadReckoning.VerticalRateSeconds, "");
        var descending = east with { AltitudeMeters = 100, VerticalRateMetersPerSecond = -10 };
        check("a descent never goes below the ground", FlightDeadReckoning.Altitude(descending, 60) == 0, "");

        // Two fields 30 km apart: an arrival counts for the nearer one only; cruisers overhead and taxiing aircraft not at all.
        Airport[] airports = [new("AAA", "A", 40.0f, -100.0f), new("BBB", "B", 40.0f, -99.65f), new("FAR", "F", 0.0f, 0.0f)];
        FlightState Near(float longitude, float altitude, bool onGround = false) =>
            new("x", "", longitude, 40.0f, altitude, 0, 0, 0, 100, onGround);
        var traffic = new FlightSnapshot(100, [Near(-100.05f, 1500), Near(-99.95f, 2000), Near(-99.6f, 800), Near(-100.0f, 11000), Near(-100.0f, 0, onGround: true)]);
        var busy = AirportTraffic.Count(airports, traffic, 100, 100, 4500);
        check("aircraft coming and going count for their nearest airport, busiest first",
            busy.Length == 2 && busy[0].Airport.Code == "AAA" && busy[0].Count == 2 && busy[1].Airport.Code == "BBB" && busy[1].Count == 1,
            string.Join(", ", busy.Select(b => $"{b.Airport.Code} {b.Count}")));

        // A border painted across a small picture lands where its longitude says, and nowhere else.
        var picture = new ManaCore.Imaging.ManaImage(360, 180);
        PlanetBorderPainter.Paint(picture, [new List<(double, double)> { (0, -60), (0, 60) }], 333.0f, new Vector4(1, 1, 1, 1));
        byte Red(int x, int y) => picture.Pixels[(y * 360 + x) * 4];
        check("a meridian border is painted along its longitude", Red(180, 90) > 200 && Red(179, 90) > 200, $"{Red(180, 90)} {Red(179, 90)}");
        check("and leaves the rest of the picture alone", Red(185, 90) == 0 && Red(180, 10) == 0, $"{Red(185, 90)} {Red(180, 10)}");

        check("a patch across the date line keeps its longitudes on one side of it",
            MathF.Abs(PlanetPatchMesh.UnwrapU(0.02f, 0.99f) - 1.02f) < 1e-6f && MathF.Abs(PlanetPatchMesh.UnwrapU(0.98f, 0.01f) + 0.02f) < 1e-6f
            && PlanetPatchMesh.UnwrapU(0.4f, 0.5f) == 0.4f, "");

        // A tile's picture covers 0..20 E, 10 S..10 N: a border at 10 E lands down its middle, not where the whole planet's would.
        var tile = new ManaCore.Imaging.ManaImage(200, 200);
        PlanetBorderPainter.Paint(tile, [new List<(double, double)> { (10, -5), (10, 5) }], 30.0f, new Vector4(1, 1, 1, 1), new GeoArea(0, 20, -10, 10));
        byte TileRed(int x, int y) => tile.Pixels[(y * 200 + x) * 4];
        check("a border in a tile lands at its longitude within the tile", TileRed(100, 100) > 200 && TileRed(150, 100) == 0 && TileRed(100, 10) == 0,
            $"{TileRed(100, 100)} {TileRed(150, 100)} {TileRed(100, 10)}");

        var tokyo = new LatLong(139.7f, 35.7f);
        var uv = PlanetPatchMesh.GlobalUv(tokyo.ToUnitSphere() * 6371000);
        check("a point's picture coordinate matches LatLong's", Vector2.Distance(uv, tokyo.ToUv()) < 1e-5f, $"{uv} {tokyo.ToUv()}");

        // A tile's window maps its own corners to the picture's: west edge u 0, north edge v 1 (pictures upload bottom row first).
        var window = new PlanetImageryDetail("k", "p", 0.25f, 0.5f, 0.2f, 0.4f);
        check("a tile maps its north-west corner to (0, 1) and its south-east to (1, 0)",
            Vector2.Distance(window.Map(0.25f, 0.2f), new Vector2(0, 1)) < 1e-6f && Vector2.Distance(window.Map(0.5f, 0.4f), new Vector2(1, 0)) < 1e-6f, "");
        check("tiles on Earth are a root patch quartered (about 420 km)",
            MathF.Abs(PlanetDetailImagery.TileCubeSize(6, PlanetLoader.EarthRadiusMeters) - 1.0f / 12.0f) < 1e-6f, "");

        // Over 0 N 0 E (planet -Z), facing +Y is north and -X east.
        var over = new Vector3(0, 0, -1);
        check("heading is 0 facing north and 90 facing east",
            MathF.Abs(ManaEditor.Code.UI.ManaUi.PlanetReadoutManaView.HeadingOf(over, Vector3.UnitY) ?? -1) < 1e-3f
            && MathF.Abs((ManaEditor.Code.UI.ManaUi.PlanetReadoutManaView.HeadingOf(over, -Vector3.UnitX) ?? -1) - 90) < 1e-3f, "");
        check("looking straight down has no heading", ManaEditor.Code.UI.ManaUi.PlanetReadoutManaView.HeadingOf(over, -over) == null, "");

        foreach (var (from, to) in new[] { (Vector3.UnitY, Vector3.Normalize(new Vector3(1, 1, 0))), (Vector3.UnitY, -Vector3.UnitY), (Vector3.UnitX, Vector3.UnitX) })
        {
            var turned = Vector3.Transform(from, Mana.CameraComponent.RotationBetween(from, to));
            check($"the camera's up turns from {from} onto {to}", Vector3.Distance(turned, to) < 1e-5f, $"{turned}");
        }

        // A callsign's route on record is believed only when the aircraft is flying it (the two cards that showed otherwise).
        var sfo = new RouteAirport("SFO", "San Francisco", "", 37.619f, -122.375f);
        var clt = new RouteAirport("CLT", "Charlotte", "", 35.214f, -80.943f);
        Airport[] fields = [new("PHX", "Phoenix", 33.434f, -112.012f), new("LAS", "Las Vegas", 36.08f, -115.152f), new("CLT", "Charlotte", 35.214f, -80.943f)];
        FlightDetails Usual(RouteAirport from, RouteAirport to, params TrackPoint[] track) =>
            new("American Airlines", from, to, null, null, track, null);
        var eastbound = new FlightState("a", "AAL2460", -100.0f, 36.5f, 11000, 230, 92, 0, 0, false);
        var flying = FlightRouteJudge.Judge(Usual(sfo, clt, new TrackPoint(0, 37.62f, -122.37f, 0)), eastbound, fields);
        check("a route the aircraft is flying is shown as scheduled", flying is { Scheduled: true, From.Code: "SFO", To.Code: "CLT" }, $"{flying}");

        // AAL2460 near Phoenix, coming down south-west from Charlotte: the record says SFO -> CLT, the other way.
        var intoPhoenix = new FlightState("b", "AAL2460", -111.5f, 33.9f, 6980, 191, 227, -11, 0, false);
        var seen = FlightRouteJudge.Judge(Usual(sfo, clt, new TrackPoint(0, 35.22f, -80.95f, 300)), intoPhoenix, fields);
        check("flying away from the destination on record, the card shows its own trip instead",
            seen is { Scheduled: false, From.Code: "CLT", To.Code: "PHX", ToGuessed: true, Usual: "SFO → CLT" }, $"{seen}");

        // AAL2978 over Mexico at cruise, the record says PHL -> PIT: nothing known, and the record named as usual.
        var phl = new RouteAirport("PHL", "Philadelphia", "", 39.872f, -75.241f);
        var pit = new RouteAirport("PIT", "Pittsburgh", "", 40.492f, -80.233f);
        var overMexico = new FlightState("c", "AAL2978", -113.74f, 29.62f, 11476, 231, 332, 0, 0, false);
        var unknown = FlightRouteJudge.Judge(Usual(phl, pit, new TrackPoint(0, 29.0f, -113.0f, 11400)), overMexico, fields);
        check("far off the route on record at cruise, the route is unknown", unknown is { HasEnds: false, Usual: "PHL → PIT" }, $"{unknown}");

        // DAL1936 50 km south-west of Minneapolis, coming down heading east-south-east: being turned in, MSP 40 degrees off its course.
        Airport[] minneapolis = [new("MSP", "Minneapolis", 44.882f, -93.222f)];
        var turningIn = new FlightState("d", "DAL1936", -93.75f, 44.62f, 4915, 210, 112, -11, 0, false);
        check("being turned in near an airport, it is still the likely arrival",
            FlightRouteJudge.ArrivingAt(turningIn, new LatLong(-93.75f, 44.62f).ToUnitSphere(), minneapolis)?.Code == "MSP", "");

        var westbound = eastbound with { TrackDegrees = 272 };
        check("on the line but flying the other way is not the route", FlightRouteJudge.Judge(Usual(sfo, clt), westbound, fields) is { Scheduled: false }, "");
        var equator = new LatLong(0, 0).ToUnitSphere();
        check("a bearing due east is 90 degrees", MathF.Abs(FlightRouteJudge.Bearing(equator, new LatLong(10, 0).ToUnitSphere()) - 90) < 0.01f, "");

        // Flying 700 km a few metres a frame: every tiny turn of the up must count, or the horizon tilts.
        var frame = Quaternion.Identity;
        var start = Vector3.Normalize(new Vector3(0.3f, 1.0f, 0.2f));
        var stepAngle = 5.0f / PlanetLoader.EarthRadiusMeters;
        frame = Mana.CameraComponent.RotationBetween(Vector3.UnitY, start);
        for (var step = 1; step <= 140_000; step++)
        {
            var desired = Vector3.Transform(start, Quaternion.CreateFromAxisAngle(Vector3.UnitX, step * stepAngle));
            var current = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, frame));
            frame = Quaternion.Normalize(Mana.CameraComponent.RotationBetween(current, desired) * frame);
        }
        var finalUp = Vector3.Transform(start, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 140_000 * stepAngle));
        var frameUp = Vector3.Transform(Vector3.UnitY, frame);
        check("the camera's up keeps up with 700 km of 5 m steps", Vector3.Dot(frameUp, finalUp) > 0.99999f, $"{frameUp} vs {finalUp}");
    }
}
