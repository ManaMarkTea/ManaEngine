using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Region;
using ManaUI;
using ManaVoxel.Code.Search;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Place search. The interesting half is what a region costs to load -- a box twice as wide needs four times
/// the tiles, and past a limit the load fails outright -- which was computed inline halfway down an ImGui
/// draw call and could not be checked from anywhere.
/// <para>
/// The geocoder is injected, so none of this goes near the network.
/// </para>
/// </summary>
public static class PlaceSearchChecks
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

        // ---------------------------------------------------------------- the panel
        var root = new UiRoot(fonts);
        var viewModel = new ManaVoxel.Code.UI.VoxelEditorViewModel();
        var panelModel = new PlaceSearchModel(_ => Task.FromResult<IReadOnlyList<GeocodeResult>>(Found));

        PlaceSearchRequests.Clear();
        root.Content = PlaceSearchManaView.Build(root.Theme, viewModel, panelModel);
        root.UpdateLayout(new Vector2(340, 600));
        root.Render();

        var searchButton = FindAll<Button>(root.Content!)
            .FirstOrDefault(b => b.Text == "Search");

        Check("the panel has a search button", searchButton != null);
        Check("disabled with an empty query", searchButton is { IsEnabled: false });

        // Nothing found yet, so the panel shows what it is for rather than an empty frame.
        Check("and no results shown yet",
            !FindAll<Button>(root.Content!).Any(b => b.Text.Contains("Kyoto", StringComparison.Ordinal)),
            "empty state");

        panelModel.Query = "kyoto";
        root.UpdateLayout(new Vector2(340, 600));
        root.Render();
        Check("enabled once there is a query", searchButton is { IsEnabled: true });

        Press(root, searchButton!.Bounds.Center);

        // The search completes immediately here, but the panel only notices when its binding next runs --
        // which is the same path a real one takes a second later.
        root.UpdateLayout(new Vector2(340, 600));
        root.Render();
        root.UpdateLayout(new Vector2(340, 600));
        root.Render();

        var resultButtons = FindAll<Button>(root.Content!)
            .Where(b => b.Text.Contains("Kyoto", StringComparison.Ordinal))
            .ToList();

        Check("results appear in the panel", resultButtons.Count == 2, $"{resultButtons.Count} rows");

        var costSlider = FindAll<SliderBox>(root.Content!).FirstOrDefault();
        Check("with the cost picker", costSlider != null);

        // The useful sizes span two orders of magnitude, so the travel is spread by ratio -- half way along
        // a 1km..64km slider is 8km, not 32.
        Check("sized logarithmically", costSlider is { IsLogarithmic: true });

        var middle = costSlider!.Slider.ValueAt(0.5f);
        Check("so the midpoint is the geometric mean",
            MathF.Abs(middle - MathF.Sqrt(costSlider.Minimum * costSlider.Maximum)) < 0.1f,
            $"{middle:0.#} km");

        // Pressing a result asks to fly there; the panel has no idea where the planet is.
        PlaceSearchRequests.Clear();
        Press(root, resultButtons[0].Bounds.Center);

        var flyTo = PlaceSearchRequests.TakeFlyTo();
        Check("pressing a result asks to fly there",
            flyTo?.DisplayName == Found[0].DisplayName, flyTo?.DisplayName ?? "none");

        Check("and the request is taken once", PlaceSearchRequests.TakeFlyTo() == null, "cleared");

        // Loading is a different button on the same row, and goes to the view model the game mode reads.
        var loadButtons = FindAll<Button>(root.Content!).Where(b => b.Text == "Load").ToList();
        Check("each result can be loaded as a region", loadButtons.Count == 2, $"{loadButtons.Count}");

        viewModel.PendingRegionRequest = null;
        Press(root, loadButtons[0].Bounds.Center);

        Check("pressing Load queues the region",
            viewModel.PendingRegionRequest?.PlaceName == Found[0].DisplayName,
            viewModel.PendingRegionRequest?.PlaceName ?? "none");

        // Over the limit the load is refused anyway, so offering it is offering a failure.
        panelModel.Zoom = RegionExtents.MaxZoom;
        panelModel.RegionKm = PlaceSearchModel.MaxRegionKm;
        root.UpdateLayout(new Vector2(340, 600));
        root.Render();

        Check("and is disabled when it would be refused",
            loadButtons.All(b => !b.IsEnabled), "disabled");

        PlaceSearchRequests.Clear();
    }

    private static void Press(UiRoot root, Vector2 point)
    {
        root.Input.InjectPointerMoved(point);
        root.Input.InjectPointerPressed(point, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
    }

    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;

        static void Walk(UiElement element, List<T> found)
        {
            if (element is T match)
            {
                found.Add(match);
            }

            foreach (var child in element.VisualChildren)
            {
                Walk(child, found);
            }
        }
    }
}
