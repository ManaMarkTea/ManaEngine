using Mana;
using Mana.Code;
using Mana.Code.Models;
using Mana.Code.Nodes;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>The model an aircraft is drawn as close up.</summary>
    public enum AircraftKind
    {
        Airliner,
        Light
    }

    /// <summary>
    /// The aircraft nearest the camera drawn as models instead of markers: an airliner, or the An-2 for light aircraft,
    /// gliders and helicopters (Content/Prefabs/Aircraft, nose along -Z). Copies are pooled per kind and moved each
    /// redraw; a copy is never smaller on screen than the marker it replaces, nor smaller than the aircraft itself.
    /// </summary>
    public sealed class FlightTrafficModels
    {
        public const string AirlinerPrefab = "Prefabs/Aircraft/Airliner.manaprefab";
        public const string LightPrefab = "Prefabs/Aircraft/Light Aircraft.manaprefab";

        // Without a reported category, slower and lower than this is a light aircraft: airliners approach at 65-80 m/s.
        private const float LightSpeedMetersPerSecond = 60.0f;
        private const float LightAltitudeMeters = 3000.0f;

        private readonly ManaContext _context;
        private readonly ManaSceneNode _parent;
        private readonly ManaModelLibrary _library;
        private readonly Dictionary<AircraftKind, Pool> _pools = new();
        private readonly HashSet<AircraftKind> _loading = [];

        public FlightTrafficModels(ManaContext context, ManaSceneNode parent)
        {
            _context = context;
            _parent = parent;
            _library = new ManaModelLibrary(context);
        }

        /// <summary>OpenSky's emitter category (extended states) when it has one; otherwise judged by speed and height.</summary>
        public static AircraftKind KindOf(in FlightState state)
        {
            switch (state.Category)
            {
                case 2: // light
                case 8: // rotorcraft
                case 9: // glider
                case 10: // lighter than air
                case 12: // ultralight
                    return AircraftKind.Light;
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                    return AircraftKind.Airliner;
            }

            if (state.SpeedMetersPerSecond > 0.0f && state.SpeedMetersPerSecond < LightSpeedMetersPerSecond && state.AltitudeMeters < LightAltitudeMeters)
            {
                return AircraftKind.Light;
            }
            return AircraftKind.Airliner;
        }

        /// <summary>Starts a redraw: every copy is free again until placed.</summary>
        public void Begin()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Used = 0;
            }
        }

        /// <summary>
        /// Places the next copy of <paramref name="kind"/> at <paramref name="center"/>, nose along <paramref name="forward"/>
        /// and top along <paramref name="up"/> (both unit, at right angles), at least <paramref name="markerSize"/> twice long.
        /// False when the model cannot be loaded (no app loader -- tests, the browser): draw the marker instead.
        /// </summary>
        public bool Place(AircraftKind kind, Vector3 center, Vector3 forward, Vector3 up, float markerSize)
        {
            if (!_pools.TryGetValue(kind, out var pool))
            {
                // Loaded after the scene walk (loading builds a scene of its own, which the walk is iterating);
                // until then the aircraft keeps its marker.
                if (_loading.Add(kind))
                {
                    _context.World.PostUpdateFutures.Add(() => _pools[kind] = new Pool(_library.Model(PrefabFor(kind))));
                }
                return false;
            }
            if (pool.Model == null)
            {
                return false;
            }

            var length = MathF.Max(1.0f, pool.Model.BoundsMax.Z - pool.Model.BoundsMin.Z);
            var scale = MathF.Max(1.0f, 2.0f * markerSize / length);
            var right = Vector3.Cross(forward, up);
            var placement = Matrix4x4.CreateScale(scale) * new Matrix4x4(
                right.X, right.Y, right.Z, 0.0f,
                up.X, up.Y, up.Z, 0.0f,
                -forward.X, -forward.Y, -forward.Z, 0.0f,
                center.X, center.Y, center.Z, 1.0f);

            if (pool.Used == pool.Copies.Count)
            {
                var copy = pool.Model.Instantiate(_context, $"{kind} {pool.Copies.Count}", placement);
                copy.IncludeInEnvironmentCapture = false;
                // After the scene walk: this runs inside it, and adding a child there breaks the walk's iteration.
                var world = _context.World;
                world.PostUpdateFutures.Add(() =>
                {
                    lock (world.SceneLock)
                    {
                        _parent.AddChild(copy);
                    }
                });
                pool.Copies.Add(new Copy(copy));
            }

            var placed = pool.Copies[pool.Used++];
            placed.Node.LocalTransform = placement;
            placed.Show(true);
            return true;
        }

        /// <summary>Ends a redraw: copies not placed this time are hidden.</summary>
        public void End()
        {
            foreach (var pool in _pools.Values)
            {
                for (var i = pool.Used; i < pool.Copies.Count; i++)
                {
                    pool.Copies[i].Show(false);
                }
            }
        }

        private static string PrefabFor(AircraftKind kind)
        {
            if (kind == AircraftKind.Light)
            {
                return LightPrefab;
            }
            return AirlinerPrefab;
        }

        private sealed class Pool(ManaModel? model)
        {
            public ManaModel? Model { get; } = model;
            public List<Copy> Copies { get; } = [];
            public int Used { get; set; }
        }

        private sealed class Copy(ManaSceneNode node)
        {
            private bool _shown = true;

            public ManaSceneNode Node { get; } = node;

            public void Show(bool shown)
            {
                if (_shown == shown)
                {
                    return;
                }
                _shown = shown;
                foreach (var part in ManaModel.MeshNodes(Node))
                {
                    part.MeshRenderer.IsVisible = shown;
                }
            }
        }
    }
}
