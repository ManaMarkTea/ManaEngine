using System.Diagnostics;
using ImGuiNET;
using Mana;
using Mana.Code.Nodes;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Planet;
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

        private string _query = string.Empty;
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
                foreach (var result in _results)
                {
                    if (ImGui.Selectable(result.DisplayName))
                    {
                        FlyTo(result, context, planet, camera);
                    }
                }
            }

            ImGui.End();
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
