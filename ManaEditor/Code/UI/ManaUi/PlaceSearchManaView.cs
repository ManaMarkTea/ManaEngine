using Mana;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Region;
using ManaUI;
using ManaEditor.Code.Search;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>Find an area, then fly to it or load it into the scene.</summary>
    public static class PlaceSearchManaView
    {
        public static void Show(UiRoot root, VoxelEditorViewModel viewModel, PlaceSearchModel model)
        {
            var theme = root.Theme;
            var popup = new UiPopup { StaysOpen = true };
            popup.BindPolled(p => p.Width, () => root.Size.X);
            popup.BindPolled(p => p.Height, () => root.Size.Y);
            var dialog = new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Padding(12.0f);
            dialog.Background = theme.Background;
            dialog.BindPolled(d => d.Width, () => MathF.Min(640.0f, root.Size.X));
            dialog.BindPolled(d => d.Height, () => MathF.Min(560.0f, root.Size.Y));
            dialog.Children.Add(new Grid().Columns("*, Auto").Rows("Auto").Children(
                new TextBlock().Text("Search Location").CenterV().GridColumn(0),
                new Button().Label("Close").OnClick(() => root.Popups.Close(popup)).GridColumn(1)));
            dialog.Children.Add(Build(theme, viewModel, model).GridRow(1));
            var overlay = new Grid().Rows("*").Columns("*").Children(dialog.CenterH().CenterV());
            overlay.Background = new Vector4(0, 0, 0, 0.55f);
            popup.Child = overlay;
            var contentEnabled = root.ContentLayer.IsEnabled;
            var floatingEnabled = root.FloatingLayer.IsEnabled;
            root.ContentLayer.IsEnabled = false;
            root.FloatingLayer.IsEnabled = false;
            popup.Closed += (_, _) =>
            {
                root.ContentLayer.IsEnabled = contentEnabled;
                root.FloatingLayer.IsEnabled = floatingEnabled;
            };
            root.Popups.ShowAt(popup, Vector2.Zero);
        }

        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel, PlaceSearchModel model)
        {
            var query = new TextBox { Placeholder = "City, landmark, address…", Text = model.Query };
            query.TextChanged += (_, text) => model.Query = text;

            // Enter searches. Typing a place name and pressing return is the whole interaction, and making
            // it reach for the button instead would be the one thing this panel got wrong.
            query.TextCommitted += (_, _) => model.Search();

            var search = new Button().Label("Search").Variant(ButtonVariant.Primary).OnClick(model.Search);
            search.BindPolled(b => b.IsEnabled, () => model.CanSearch);

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Wrap();

            // One binding drives the status text and the polling: it runs every frame the panel is visible,
            // which is exactly when a finished search should be picked up.
            status.BindTextPolled(() =>
            {
                model.Poll();

                if (model.IsSearching)
                {
                    return "Searching…";
                }

                return model.Error ?? string.Empty;
            });

            status.BindPolled(t => t.Foreground, () => model.Error != null ? theme.Danger : theme.TextMuted);
            status.BindPolled(
                t => t.Visibility,
                () => model.IsSearching || model.Error != null ? Visibility.Visible : Visibility.Collapsed);

            // Both the cost readout and the result list only mean anything once there are results, so the
            // whole lower half appears with them rather than sitting there empty.
            var results = new DynamicContent()
                .Showing<IReadOnlyList<GeocodeResult>>(
                    () => model.Results.Count > 0 ? model.Results : null,
                    _ => Results(theme, viewModel, model))
                .Empty(new TextBlock()
                    .Text("Search for a place to fly the camera there, or load it into the scene at 1 m scale.")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap());

            return new Grid().Rows("Auto, Auto, *", theme.Spacing).Columns("*").Padding(8.0f).Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").GridRow(0).Children(
                    query.GridColumn(0),
                    search.GridColumn(1)),

                status.GridRow(1),
                results.Scrollable().GridRow(2));
        }

        private static UiElement Results(UiTheme theme, VoxelEditorViewModel viewModel, PlaceSearchModel model)
        {
            var list = new StackPanel().Vertical(theme.Spacing).Children(
                Cost(theme, model),
                new Border().Fill(theme.Divider).Height(1.0f));

            foreach (var result in model.Results)
            {
                list.Children.Add(Result(theme, viewModel, model, result));
            }

            return list;
        }

        /// <summary>
        /// What loading a region would cost, and the settings that decide it. One picker for every result:
        /// the numbers come from the first, and Mercator tiles only tighten toward the poles.
        /// </summary>
        private static UiElement Cost(UiTheme theme, PlaceSearchModel model)
        {
            var size = new SliderBox
            {
                Minimum = PlaceSearchModel.MinRegionKm,
                Maximum = PlaceSearchModel.MaxRegionKm,
                DecimalPlaces = 1,
                Value = model.RegionKm,

                // The useful range spans a couple of orders of magnitude, and a linear slider bunches every
                // small region into the first few pixels of travel.
                IsLogarithmic = true
            };

            Bind(size, () => model.RegionKm, value => model.RegionKm = value);

            var zoom = new SliderBox
            {
                Minimum = RegionExtents.MinZoom,
                Maximum = RegionExtents.MaxZoom,
                IsInteger = true,
                Step = 1.0f,
                Value = model.Zoom
            };

            Bind(zoom, () => model.Zoom, value => model.Zoom = (int)MathF.Round(value));

            var hex = new SliderBox { Minimum = 0.5f, Maximum = 8.0f, DecimalPlaces = 2, Value = model.HexRadiusInSamples };
            Bind(hex, () => model.HexRadiusInSamples, value => model.HexRadiusInSamples = value);

            var step = new SliderBox { Minimum = 0.0f, Maximum = 1.0f, DecimalPlaces = 2, Value = model.StepStrength };
            Bind(step, () => model.StepStrength, value => model.StepStrength = value);

            var summary = new TextBlock().FontFamily(theme.FontFamilyMono).FontSize(theme.FontSizeSmall).Wrap();
            summary.BindTextPolled(() => model.CostSummary);

            // Over the limit the load fails rather than being slow, so the readout has to say so before the
            // button is pressed and not after.
            summary.BindPolled(t => t.Foreground, () => model.IsOverTileLimit ? theme.Danger : theme.TextMuted);

            var hexLabel = new TextBlock().Foreground(theme.TextMuted).Ellipsis().CenterV();
            hexLabel.BindTextPolled(() => $"Hexes (~{model.HexRadiusMeters:0.#} m)");

            return new StackPanel().Vertical(theme.Spacing).Children(
                Row(theme, "Size (km)", size,
                    "Cost grows with area: a box twice as wide needs four times the tiles."),

                Row(theme, "Source zoom", zoom,
                    "z13 is the finest Terrarium publishes everywhere. 14 and 15 exist only in places and "
                    + "fail the load where they do not. Each step up is four times the tiles."),

                new Grid().Columns("90, *", theme.Spacing).Rows("Auto").Children(
                    hexLabel.GridColumn(0),
                    hex.WithTooltip("Coarser hexes read as more stylised, and cost falls with the square of this.")
                        .GridColumn(1)),

                Row(theme, "Step", step,
                    "1 = flat plateaus with vertical walls between them, 0 = a continuous surface that is "
                    + "merely hex-faceted. Also adjustable after loading, from the Inspector."),

                summary);
        }

        private static UiElement Result(
            UiTheme theme,
            VoxelEditorViewModel viewModel,
            PlaceSearchModel model,
            GeocodeResult result)
        {
            var load = new Button()
                .Label("Load")
                .Variant(ButtonVariant.Subtle)
                .OnClick(() => viewModel.PendingRegionRequest = model.BuildRequest(result))
                .WithTooltip("Load this place into the scene at 1 m scale, off the globe.");

            // Disabled over the limit rather than left to fail: the load rejects it anyway, and finding that
            // out after the tiles start fetching is worse than not being offered it.
            load.BindPolled(b => b.IsEnabled, () => !model.IsOverTileLimit);

            var fly = new Button()
                .Label(result.DisplayName)
                .Variant(ButtonVariant.Subtle)
                .StretchH()
                .OnClick(() => PlaceSearchRequests.RequestFlyTo(result))
                .WithTooltip(result.DisplayName);

            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(
                load.GridColumn(0),
                fly.GridColumn(1));
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor, string? tooltip = null)
        {
            return new Grid().Columns("90, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().CenterV()
                    .WithTooltip(tooltip).GridColumn(0),
                editor.WithTooltip(tooltip).GridColumn(1));
        }

        private static void Bind(SliderBox slider, Func<float> read, Action<float> write)
        {
            slider.ValueChanged += (_, value) =>
            {
                if (!slider.IsApplyingBinding)
                {
                    write(value);
                }
            };

            slider.BindPolled(s => s.Value, read);
        }
    }

    /// <summary>
    /// Flying the camera somewhere, as a mailbox the game mode drains.
    /// <para>
    /// The panel knows a longitude and a latitude. Turning that into a camera move needs the planet, the
    /// camera and the scene to drop a marker into, none of which a docked panel should be holding.
    /// </para>
    /// </summary>
    public static class PlaceSearchRequests
    {
        private static GeocodeResult? _flyTo;

        public static void RequestFlyTo(GeocodeResult result) => _flyTo = result;

        /// <summary>Takes a pending request, if any. Reading it clears it -- a request is acted on once.</summary>
        public static GeocodeResult? TakeFlyTo()
        {
            var pending = _flyTo;
            _flyTo = null;
            return pending;
        }

        public static void Clear() => _flyTo = null;
    }
}
