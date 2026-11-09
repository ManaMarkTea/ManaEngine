using System.Diagnostics;
using Mana;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Geocoding;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Region;
using ManaEditor.Code.UI;

namespace ManaEditor.Code.Search
{
    /// <summary>
    /// A place search and the settings a result gets loaded with.
    /// <para>
    /// Split out of the ImGui panel because none of it is drawing. What a region costs to load is the part
    /// that matters -- a box twice as wide needs four times the tiles, and the limit is enforced during the
    /// load whether or not a panel warned first -- and it was previously computed inline, halfway down a
    /// draw call, with no way to check it.
    /// </para>
    /// </summary>
    public sealed class PlaceSearchModel
    {
        /// <summary>
        /// "Just arrived in this city" altitude: close enough to read as a specific place rather than the
        /// whole globe or a street corner.
        /// </summary>
        public const float ArrivalAltitudeMeters = 50_000.0f;

        /// <summary>
        /// Sized in metres rather than scene units on purpose. At the arrival altitude a marker sized the way
        /// a nearby debug shape would be covers half the view.
        /// </summary>
        public const float MarkerRadiusMeters = 300.0f;

        private const float DefaultRegionBoxKm = 4.0f;

        /// <summary>
        /// Hex size is chosen in multiples of the source sample spacing rather than in metres, so "as fine as
        /// the data goes" stays at 1.0 whatever zoom is picked.
        /// </summary>
        private const float DefaultHexRadiusInSamples = RegionTerrainMesh.DefaultHexRadiusInSamples;

        private readonly Func<string, Task<IReadOnlyList<GeocodeResult>>> _geocoder;

        private Task<IReadOnlyList<GeocodeResult>>? _pending;
        private Stopwatch? _timer;

        private float _regionKm = DefaultRegionBoxKm;
        private int _zoom = RegionExtents.DefaultZoom;
        private float _hexRadiusInSamples = DefaultHexRadiusInSamples;
        private float _stepStrength = 1.0f;

        public PlaceSearchModel(Func<string, Task<IReadOnlyList<GeocodeResult>>>? geocoder = null)
        {
            // Defaults to the real geocoder; tests supply their own rather than going to the network.
            _geocoder = geocoder ?? (query => NominatimGeocoder.SearchAsync(query));
        }

        /// <summary>Raised when results, the error or the searching state change.</summary>
        public event EventHandler? Changed;

        public string Query { get; set; } = string.Empty;

        public bool IsSearching { get; private set; }

        public string? Error { get; private set; }

        public IReadOnlyList<GeocodeResult> Results { get; private set; } = Array.Empty<GeocodeResult>();

        public bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(Query);

        public static float MinRegionKm => RegionExtents.MinHalfExtentMeters * 2.0f / 1000.0f;

        public static float MaxRegionKm => RegionExtents.MaxHalfExtentMeters * 2.0f / 1000.0f;

        /// <summary>Width of the loaded box, in kilometres. Clamped to what a region can actually cover.</summary>
        public float RegionKm
        {
            get => _regionKm;
            set => _regionKm = Math.Clamp(value, MinRegionKm, MaxRegionKm);
        }

        /// <summary>
        /// Source tile zoom. z13 is the finest Terrarium publishes everywhere; 14 and 15 exist only in places
        /// and fail the load where they do not.
        /// </summary>
        public int Zoom
        {
            get => _zoom;
            set => _zoom = Math.Clamp(value, RegionExtents.MinZoom, RegionExtents.MaxZoom);
        }

        public float HexRadiusInSamples
        {
            get => _hexRadiusInSamples;
            set => _hexRadiusInSamples = Math.Clamp(value, 0.5f, 8.0f);
        }

        /// <summary>1 = flat plateaus with vertical walls, 0 = a continuous surface that is merely faceted.</summary>
        public float StepStrength
        {
            get => _stepStrength;
            set => _stepStrength = Math.Clamp(value, 0.0f, 1.0f);
        }

        public float HalfExtentMeters => RegionKm * 1000.0f * 0.5f;

        /// <summary>
        /// The result the cost readouts describe. Mercator tiles tighten toward the poles, so the numbers are
        /// only representative -- but of the first result, which is the one anyone reads them against.
        /// </summary>
        public GeocodeResult? Preview => Results.Count > 0 ? Results[0] : null;

        public double MetersPerSample =>
            Preview is { } result ? RegionExtents.MetersPerSample(Zoom, result.Latitude) : 0.0;

        /// <summary>Hex radius in metres, derived from the sample spacing at the previewed place.</summary>
        public float HexRadiusMeters => HexRadiusInSamples * (float)MetersPerSample;

        /// <summary>Tiles the load would fetch. The same call the load itself is checked against.</summary>
        public long TileCount =>
            Preview is { } result
                ? RegionExtents.EstimateTileCount(
                    new LatLong(result.Longitude, result.Latitude),
                    HalfExtentMeters,
                    Zoom)
                : 0L;

        public bool IsOverTileLimit => TileCount > RegionExtents.MaxTilesPerRegion;

        public string CostSummary
        {
            get
            {
                if (Preview == null)
                {
                    return string.Empty;
                }

                var summary = $"~{MetersPerSample:0.#} m/sample, {TileCount} tiles";
                return IsOverTileLimit ? $"{summary} — over the {RegionExtents.MaxTilesPerRegion} tile limit" : summary;
            }
        }

        public RegionLoadRequest BuildRequest(GeocodeResult result) =>
            new(result.DisplayName,
                result.Longitude,
                result.Latitude,
                HalfExtentMeters,
                Zoom,
                HexRadiusMeters,
                StepStrength);

        // ----- searching ----------------------------------------------------------------------------------

        public void Search()
        {
            if (!CanSearch)
            {
                return;
            }

            IsSearching = true;
            Error = null;
            Results = Array.Empty<GeocodeResult>();
            _timer = Stopwatch.StartNew();
            _pending = _geocoder(Query);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Takes a finished search. Cheap to call every frame -- it does nothing until one completes, which
        /// is what lets the panel drive this from a binding rather than the render loop.
        /// </summary>
        public bool Poll(ManaContext? context = null)
        {
            if (_pending is not { IsCompleted: true } task)
            {
                return false;
            }

            _pending = null;
            IsSearching = false;

            if (_timer != null && context != null)
            {
                context.World.RecordFrameStat("Search: Nominatim Geocode", _timer.Elapsed);
            }

            _timer = null;

            if (task.IsFaulted)
            {
                // The exception goes to the console; the panel gets something a person can read.
                Error = "Search failed — see console for details.";
            }
            else
            {
                Results = task.Result;
                Error = Results.Count == 0 ? "No results found." : null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }
}
