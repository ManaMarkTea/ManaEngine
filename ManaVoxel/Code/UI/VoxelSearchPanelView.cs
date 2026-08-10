using System.Diagnostics;
using ImGuiNET;
using Mana;
using Mana.Code.Nodes;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Region;
using System.Numerics;

namespace ManaVoxel.Code.UI
{
    /// <summary>
    /// A summonable place-search box: queries OpenStreetMap's Nominatim geocoder (see NominatimGeocoder)
    /// for a typed place name and flies the main camera to the picked result, drops a small marker there,
    /// and requests full (z13) Terrarium detail for that immediate area. Mirrors
    /// VoxelEditorAboutPanelView's own "ShowX flag on the ViewModel, early-return if false,
    /// ImGui.Begin(title, ref isOpen)" floating-window pattern, minus that view's resize/docking
    /// machinery -- this window is small and doesn't need it.
    /// </summary>
    public sealed class VoxelSearchPanelView
    {
        // "Just arrived in this city" altitude -- close enough to read as a specific place, not the
        // whole globe or a street-level zoom. Converted from real-world meters to scene units via the
        // same EarthRadiusMeters/Radius ratio PlanetNode's own ComputeTargetMetersPerPixel uses.
        private const float ArrivalAltitudeMeters = 50_000f;

        // Deliberately small in real-world terms -- at the ~50km arrival altitude above, a marker sized
        // in scene units the way a nearby debug shape would be reads as a giant sphere covering half the
        // view. Sizing it in meters and converting through the same ratio as the arrival altitude keeps
        // it a small, sensible "pin" regardless of the planet's own scene-unit radius.
        private const float MarkerRadiusMeters = 300f;
        private static readonly Vector4 MarkerColor = Colors.Orange;

        // Region size is free-form (in km across the box) rather than a fixed menu, bounded by
        // RegionExtents' own zoom ladder: the smallest box the max-zoom end supports, up to the largest
        // the local-tangent-plane model stays trustworthy over. Whatever is picked, the source zoom is
        // derived rather than chosen -- see RegionExtents.
        private const float DefaultRegionBoxKm = 4.0f;

        // Hex size is picked as a MULTIPLE of the source sample spacing rather than in meters, because
        // that's the number that actually controls the look: at 1 a hex is about the size of one source
        // sample and the tiling resolves everything the data has, and doubling it makes the hexes four
        // times as coarse regardless of which zoom level the chosen region size happens to land on.
        private const float DefaultHexRadiusInSamples = RegionTerrainMesh.DefaultHexRadiusInSamples;
        private const float DefaultStepStrength = 1.0f;

        private string _query = string.Empty;
        private float _regionBoxKm = DefaultRegionBoxKm;
        private float _hexRadiusInSamples = DefaultHexRadiusInSamples;
        private float _stepStrength = DefaultStepStrength;
        private float _previewHexRadiusMeters = 1.0f;
        private bool _isSearching;
        private string? _errorMessage;
        private IReadOnlyList<GeocodeResult> _results = Array.Empty<GeocodeResult>();
        private Task<IReadOnlyList<GeocodeResult>>? _pendingSearch;
        private Stopwatch? _searchStopwatch;
        private ManaSceneNode? _markerNode;

        public void Draw(VoxelEditorViewModel viewModel, ManaContext context, PlanetNode? planet, CameraComponent? camera)
        {
            if (!viewModel.ShowSearchWindow)
            {
                return;
            }

            PollPendingSearch(context);

            ImGui.SetNextWindowSize(new Vector2(420.0f, 0.0f), ImGuiCond.FirstUseEver);
            var isOpen = viewModel.ShowSearchWindow;
            if (!ImGui.Begin("Search Location", ref isOpen))
            {
                viewModel.ShowSearchWindow = isOpen;
                ImGui.End();
                return;
            }

            viewModel.ShowSearchWindow = isOpen;

            ImGui.SetNextItemWidth(-80.0f);
            var submitted = ImGui.InputText("##SearchQuery", ref _query, 256, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            ImGui.BeginDisabled(_isSearching || string.IsNullOrWhiteSpace(_query));
            var clickedSearch = ImGui.Button("Search");
            ImGui.EndDisabled();

            if ((submitted || clickedSearch) && !_isSearching && !string.IsNullOrWhiteSpace(_query))
            {
                StartSearch(_query);
            }

            if (_isSearching)
            {
                ImGui.TextDisabled("Searching...");
            }
            else if (_errorMessage != null)
            {
                ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1.0f), _errorMessage);
            }

            if (planet == null || camera == null)
            {
                ImGui.TextDisabled("No planet/camera available to fly to.");
            }
            else if (_results.Count > 0)
            {
                ImGui.Separator();
                DrawRegionSizePicker(_results[0].Latitude);
                ImGui.Separator();

                for (var i = 0; i < _results.Count; i++)
                {
                    var result = _results[i];
                    ImGui.PushID(i);

                    // "Region" loads the place into the scene at true 1m scale, off the globe entirely
                    // (see RegionNode); clicking the name itself keeps the original behaviour of flying
                    // the planet camera there.
                    if (ImGui.SmallButton("Region"))
                    {
                        viewModel.PendingRegionRequest = new RegionLoadRequest(
                            result.DisplayName,
                            result.Longitude,
                            result.Latitude,
                            _regionBoxKm * 1000f * 0.5f,
                            _previewHexRadiusMeters,
                            _stepStrength);
                    }

                    ImGui.SameLine();
                    if (ImGui.Selectable(result.DisplayName))
                    {
                        FlyTo(result, context, planet, camera);
                    }

                    ImGui.PopID();
                }
            }

            ImGui.End();
        }

        // previewLatitude only affects the reported meters-per-sample (Mercator samples tighten toward the
        // poles); the chosen zoom itself is latitude-independent. Taken from the first result purely so
        // the readout is representative of what's about to be loaded.
        private void DrawRegionSizePicker(float previewLatitude)
        {
            var minKm = RegionExtents.MinHalfExtentMeters * 2f / 1000f;
            var maxKm = RegionExtents.MaxHalfExtentMeters * 2f / 1000f;
            _regionBoxKm = Math.Clamp(_regionBoxKm, minKm, maxKm);

            ImGui.SetNextItemWidth(-1.0f);
            // Logarithmic: the useful range spans a couple of orders of magnitude, and a linear slider
            // would bunch every small region into the first few pixels of travel.
            ImGui.SliderFloat(
                "##RegionSize",
                ref _regionBoxKm,
                minKm,
                maxKm,
                "Region: %.1f km across",
                ImGuiSliderFlags.Logarithmic);

            // Checked immediately after the slider, while it's still the "current item" -- the readout
            // below becomes the current item the moment it's emitted.
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Source detail steps down one zoom level per doubling of size, so every region costs\n" +
                    "about the same to fetch and build -- a bigger area trades resolution for coverage\n" +
                    "rather than taking proportionally longer.");
            }

            var halfExtent = _regionBoxKm * 1000f * 0.5f;
            var zoom = RegionExtents.ZoomForHalfExtent(halfExtent);
            var metersPerSample = RegionExtents.MetersPerSample(zoom, previewLatitude);
            ImGui.TextDisabled($"z{zoom} source, ~{metersPerSample:0.#} m/sample");

            // Held in a field rather than recomputed at click time because the "Region" buttons below are
            // drawn per result and all send the same style; this is the one place the sample spacing that
            // converts it into meters is already to hand.
            _previewHexRadiusMeters = _hexRadiusInSamples * (float)metersPerSample;

            ImGui.SetNextItemWidth(-1.0f);
            ImGui.SliderFloat("##HexSize", ref _hexRadiusInSamples, 0.5f, 8.0f, $"Hexes: %.2f samples (~{_previewHexRadiusMeters:0.#} m)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Terrain is tiled with hexagons rather than a quad grid. Coarser hexes are more\n" +
                    "obviously stylised and much cheaper -- cost falls with the square of this.");
            }

            ImGui.SetNextItemWidth(-1.0f);
            ImGui.SliderFloat("##HexStep", ref _stepStrength, 0.0f, 1.0f, "Step: %.2f");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "1 = every hex is a flat plateau with vertical walls between it and its neighbours.\n" +
                    "0 = a continuous surface that is merely hex-faceted. Adjustable after loading, from\n" +
                    "the Inspector on the region node.");
            }
        }

        private void StartSearch(string query)
        {
            _isSearching = true;
            _errorMessage = null;
            _results = Array.Empty<GeocodeResult>();
            _searchStopwatch = Stopwatch.StartNew();
            _pendingSearch = NominatimGeocoder.SearchAsync(query);
        }

        private void PollPendingSearch(ManaContext context)
        {
            if (_pendingSearch == null || !_pendingSearch.IsCompleted)
            {
                return;
            }

            var task = _pendingSearch;
            _pendingSearch = null;
            _isSearching = false;

            if (_searchStopwatch != null)
            {
                context.World.RecordFrameStat("Search: Nominatim Geocode", _searchStopwatch.Elapsed);
                _searchStopwatch = null;
            }

            if (task.IsFaulted)
            {
                _errorMessage = "Search failed -- see console for details.";
                return;
            }

            _results = task.Result;
            if (_results.Count == 0)
            {
                _errorMessage = "No results found.";
            }
        }

        private void FlyTo(GeocodeResult result, ManaContext context, PlanetNode planet, CameraComponent camera)
        {
            var latLong = new LatLong(result.Longitude, result.Latitude);
            var worldPoint = Vector3.Transform(latLong.ToSphere(planet.Radius, altitudeScale: 1.0f), planet.WorldTransform);
            var metersPerSceneUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;

            camera.FlyToWorldPosition(worldPoint, ArrivalAltitudeMeters / metersPerSceneUnit);
            PlaceMarker(context, worldPoint, MarkerRadiusMeters / metersPerSceneUnit);
            planet.RequestDetailAt(result.Longitude, result.Latitude);
        }

        private void PlaceMarker(ManaContext context, Vector3 worldPosition, float radius)
        {
            if (_markerNode == null)
            {
                _markerNode = new ManaSceneNode(context, Matrix4x4.CreateTranslation(worldPosition)) { Name = "Search Marker" };
                _markerNode.Components.Add(new MeshRendererComponent(_markerNode, new ManaSphere(context, MarkerColor, radius)));
                context.World.DefaultScene.Children.Add(_markerNode);
                return;
            }

            _markerNode.Position = worldPosition;
            _markerNode.UpdateTransform();
        }
    }
}
