using ImGuiNET;
using Mana;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Planet;
using System.Numerics;

namespace ManaVoxel.Code.UI
{
    /// <summary>
    /// A summonable place-search box: queries OpenStreetMap's Nominatim geocoder (see NominatimGeocoder)
    /// for a typed place name and flies the main camera to the picked result. Mirrors
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

        private string _query = string.Empty;
        private bool _isSearching;
        private string? _errorMessage;
        private IReadOnlyList<GeocodeResult> _results = Array.Empty<GeocodeResult>();
        private Task<IReadOnlyList<GeocodeResult>>? _pendingSearch;

        public void Draw(VoxelEditorViewModel viewModel, PlanetNode? planet, CameraComponent? camera)
        {
            if (!viewModel.ShowSearchWindow)
            {
                return;
            }

            PollPendingSearch();

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
                        FlyTo(result, planet, camera);
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
            _pendingSearch = NominatimGeocoder.SearchAsync(query);
        }

        private void PollPendingSearch()
        {
            if (_pendingSearch == null || !_pendingSearch.IsCompleted)
            {
                return;
            }

            var task = _pendingSearch;
            _pendingSearch = null;
            _isSearching = false;

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

        private static void FlyTo(GeocodeResult result, PlanetNode planet, CameraComponent camera)
        {
            var latLong = new LatLong(result.Longitude, result.Latitude);
            var worldPoint = Vector3.Transform(latLong.ToSphere(planet.Radius, altitudeScale: 1.0f), planet.WorldTransform);

            var metersPerSceneUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;
            var distance = ArrivalAltitudeMeters / metersPerSceneUnit;

            camera.FlyToWorldPosition(worldPoint, distance);
        }
    }
}
