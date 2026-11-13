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
    }
}
