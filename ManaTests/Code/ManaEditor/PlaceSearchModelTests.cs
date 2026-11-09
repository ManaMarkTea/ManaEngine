using ManaGeoTools.Code.Geocoding;
using ManaUI;
using ManaGeoTools.Code.Region;
using ManaEditor.Code.Search;

namespace ManaTests;

/// <summary>
/// Place search, and what a region costs to load.
/// <para>
/// The cost is the part that matters and the part that has no UI in it: a box twice as wide needs four
/// times the tiles, and past a limit the load is refused rather than merely slow. All of it was computed
/// inline halfway down an ImGui draw call, which is why none of it could be checked from anywhere.
/// </para>
/// <para>
/// The geocoder is injected, so none of this goes near the network.
/// </para>
/// </summary>
public static class PlaceSearchModelChecks
{
    private static readonly GeocodeResult[] Found =
    {
        new("Kyoto, Japan", 135.768f, 35.011f),
        new("Kyoto Station", 135.759f, 34.985f)
    };

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static PlaceSearchModel Searched(GeocodeResult[]? results = null)
        {
            var model = new PlaceSearchModel(_ =>
                Task.FromResult<IReadOnlyList<GeocodeResult>>(results ?? Found))
            {
                Query = "kyoto"
            };

            model.Search();
            model.Poll();
            return model;
        }

        // ---------------------------------------------------------------- searching
        var idle = new PlaceSearchModel(_ => Task.FromResult<IReadOnlyList<GeocodeResult>>(Found));

        Check("an empty query cannot be searched", !idle.CanSearch, "blocked");

        idle.Query = "   ";
        Check("nor can whitespace", !idle.CanSearch, "blocked");

        idle.Query = "kyoto";
        Check("a real one can", idle.CanSearch);

        var model = Searched();
        Check("results arrive", model.Results.Count == 2, $"{model.Results.Count}");
        Check("and searching is over", !model.IsSearching);
        Check("with no error", model.Error == null, model.Error ?? "none");

        var empty = Searched(Array.Empty<GeocodeResult>());

        // An empty result list looks identical to a search that never ran, so it has to say so.
        Check("finding nothing says so", empty.Error != null, empty.Error ?? "silent");

        var failing = new PlaceSearchModel(_ =>
            Task.FromException<IReadOnlyList<GeocodeResult>>(new InvalidOperationException("offline")))
        {
            Query = "kyoto"
        };

        failing.Search();
        failing.Poll();

        Check("a failure is reported rather than thrown", failing.Error != null, failing.Error ?? "silent");
        Check("and does not leave it searching forever", !failing.IsSearching);

        // Polling with nothing outstanding is what the panel does every frame.
        Check("polling an idle model does nothing", !model.Poll());

        // ---------------------------------------------------------------- region cost
        model.Zoom = RegionExtents.DefaultZoom;

        // Measured from 16km rather than the 4km default: tile counts are ceilings over a grid, so a box
        // only a tile or two across is mostly boundary and the fourfold growth only shows once it is not.
        model.RegionKm = 16.0f;

        var baseline = model.TileCount;
        Check("a region has a tile cost", baseline > 0, $"{baseline} tiles");

        model.RegionKm = 32.0f;
        var doubled = model.TileCount;

        // Area, not width. This is the whole reason the readout exists.
        Check("doubling the width roughly quadruples it",
            doubled >= baseline * 3, $"{baseline} -> {doubled}");

        model.RegionKm = 16.0f;
        model.Zoom = RegionExtents.DefaultZoom + 1;
        var zoomed = model.TileCount;

        Check("so does a step of zoom", zoomed >= baseline * 3, $"{baseline} -> {zoomed}");

        // Finer source data means smaller samples.
        model.Zoom = RegionExtents.DefaultZoom;
        var coarse = model.MetersPerSample;
        model.Zoom = RegionExtents.DefaultZoom + 1;

        Check("and finer samples", model.MetersPerSample < coarse, $"{coarse:0.#} -> {model.MetersPerSample:0.#} m");

        // ---------------------------------------------------------------- clamping
        model.RegionKm = 1_000_000.0f;
        Check("the size cannot exceed what a region covers",
            model.RegionKm <= PlaceSearchModel.MaxRegionKm, $"{model.RegionKm:0.#} km");

        model.RegionKm = -5.0f;
        Check("nor go below the minimum",
            model.RegionKm >= PlaceSearchModel.MinRegionKm, $"{model.RegionKm:0.#} km");

        model.Zoom = 99;
        Check("zoom is held to what the source publishes",
            model.Zoom == RegionExtents.MaxZoom, $"z{model.Zoom}");

        model.StepStrength = 4.0f;
        Check("and step strength to 0..1", model.StepStrength == 1.0f, $"{model.StepStrength}");

        // ---------------------------------------------------------------- the limit
        model.Zoom = RegionExtents.MaxZoom;
        model.RegionKm = PlaceSearchModel.MaxRegionKm;

        Check("the largest region at the finest zoom is over the limit",
            model.IsOverTileLimit, $"{model.TileCount} of {RegionExtents.MaxTilesPerRegion}");

        Check("and the readout says which limit", model.CostSummary.Contains("limit"), model.CostSummary);

        model.Zoom = RegionExtents.DefaultZoom;
        model.RegionKm = 4.0f;
        Check("a sensible one is not", !model.IsOverTileLimit, model.CostSummary);

        // ---------------------------------------------------------------- the request
        var request = model.BuildRequest(Found[0]);

        Check("a request carries the place", request.PlaceName == Found[0].DisplayName, request.PlaceName);
        Check("and half the width, in metres",
            MathF.Abs(request.HalfExtentMeters - model.RegionKm * 500.0f) < 0.1f,
            $"{request.HalfExtentMeters:0} m");

        // Hexes are chosen in samples and sent in metres, so the conversion has to use the same spacing the
        // cost readout was computed from.
        Check("with the hex size converted through the sample spacing",
            MathF.Abs(request.HexRadiusMeters - model.HexRadiusInSamples * (float)model.MetersPerSample) < 0.01f,
            $"{request.HexRadiusMeters:0.#} m");
    }
}
