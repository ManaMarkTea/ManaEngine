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

        // Offered region sizes, coarsest detail last. The zoom shown against each is not a free choice --
        // RegionExtents derives it from the extent (500m -> z13, and one zoom level down per doubling),
        // so this array only has to pick which points on that curve are worth exposing.
        private static readonly float[] RegionHalfExtentOptions = { 500f, 1000f, 2000f, 4000f };

        private string _query = string.Empty;
        private int _regionSizeIndex;
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
                DrawRegionSizePicker();
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
                            RegionHalfExtentOptions[_regionSizeIndex]);
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

        private void DrawRegionSizePicker()
        {
            var labels = new string[RegionHalfExtentOptions.Length];
            for (var i = 0; i < RegionHalfExtentOptions.Length; i++)
            {
                var halfExtent = RegionHalfExtentOptions[i];
                var sideMeters = halfExtent * 2f;
                var zoom = RegionExtents.ZoomForHalfExtent(halfExtent);
                labels[i] = sideMeters >= 1000f
                    ? $"{sideMeters / 1000f:0.##} km  (+/-{halfExtent:0} m, z{zoom})"
                    : $"{sideMeters:0} m  (+/-{halfExtent:0} m, z{zoom})";
            }

            ImGui.SetNextItemWidth(-1.0f);
            ImGui.Combo("##RegionSize", ref _regionSizeIndex, labels, labels.Length);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Region size. Source detail drops one zoom level per doubling, so every size costs\n" +
                    "about the same to fetch and build -- a bigger area trades resolution for coverage.");
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
