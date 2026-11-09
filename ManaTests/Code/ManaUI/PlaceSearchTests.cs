using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Region;
using ManaUI;
using ManaEditor.Code.Search;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The place search panel. What a region costs is decided by PlaceSearchModel and checked separately --
/// none of that needs a window. This is the half that does.
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


        // ---------------------------------------------------------------- the panel
        var root = new UiRoot(fonts);
        var viewModel = new ManaEditor.Code.UI.VoxelEditorViewModel();
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
