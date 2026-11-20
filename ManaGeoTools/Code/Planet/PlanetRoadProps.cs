using Mana;
using Mana.Code;
using Mana.Code.Models;
using Mana.Code.Nodes;
using ManaGeoTools.Code.Osm;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Kenney's City Kit (Roads) planted along the roads near a low camera: guard rails down both edges of each motorway
    /// and trunk carriageway (road-straight-barrier stretched to the road's width, piece by piece along it) and street
    /// lights along every road. Copies are pooled and re-placed as the camera moves; the road itself is the ribbon
    /// underneath (<see cref="PlanetRoadMesh"/>), so curves stay smooth however the pieces fall.
    /// </summary>
    internal sealed class PlanetRoadProps
    {
        private const string Kit = "zip:kenney_city-kit-roads.zip/Models/GLB format/";
        private const string BarrierModel = Kit + "road-straight-barrier.glb";
        private const string LightModel = Kit + "light-curved.glb";

        // How far from the camera each is planted (metres), and the height above which none are.
        private const float BarrierRadius = 800.0f;
        private const float LightRadius = 1500.0f;
        private const float ShowBelowMetres = 1500.0f;

        // Rail pieces this long, overlapped a little so the half-metre float grid far from the origin leaves no gaps.
        private const float PieceMetres = 12.0f;
        private const float RailMetres = 0.8f;
        private const float LightMetres = 10.0f;
        private const int MaxBarriers = 4000;
        private const int MaxLights = 800;

        // Re-planted when the camera has moved this far, or the roads under it were laid again.
        private const float ReplantMetres = 50.0f;

        private readonly ManaContext _context;
        private readonly ManaSceneNode _parent;
        private readonly ManaModelLibrary _library;
        private readonly Dictionary<string, Pool> _pools = new();
        private readonly HashSet<string> _loading = [];
        private Vector3 _plantedFrom = new(float.MaxValue);
        private int _plantedRevision = -1;
        private bool _shown;

        public PlanetRoadProps(ManaContext context, ManaSceneNode parent)
        {
            _context = context;
            _parent = parent;
            _library = new ManaModelLibrary(context);
        }

        public int Planted { get; private set; }

        /// <param name="cameraLocal">The camera in the planet's local space.</param>
        /// <param name="roads">Each class's draped lines (planet local).</param>
        /// <param name="revision">Changes whenever the lines were built or laid again.</param>
        public void Update(Vector3 cameraLocal, float heightMetres, float metresPerUnit, IReadOnlyList<(RoadClass Class, Vector3[][] Lines)> roads,
            int revision, bool enabled)
        {
            if (!enabled || heightMetres > ShowBelowMetres)
            {
                if (_shown)
                {
                    HideAll();
                }
                return;
            }
            var moved = Vector3.Distance(cameraLocal, _plantedFrom) * metresPerUnit;
            if (_shown && moved < ReplantMetres && revision == _plantedRevision)
            {
                return;
            }
            _plantedFrom = cameraLocal;
            _plantedRevision = revision;
            _shown = true;

            var unitsPerMetre = 1.0f / metresPerUnit;
            var barriers = Begin(BarrierModel);
            var lights = Begin(LightModel);
            var barrierReach = BarrierRadius * unitsPerMetre;
            var lightReach = LightRadius * unitsPerMetre;
            foreach (var (roadClass, lines) in roads)
            {
                var (width, lightEvery, railed) = Look(roadClass);
                foreach (var line in lines)
                {
                    PlantAlong(line, cameraLocal, unitsPerMetre, width, lightEvery, railed, barrierReach, lightReach, barriers, lights);
                }
            }
            End(barriers);
            End(lights);
            Planted = (barriers?.Used ?? 0) + (lights?.Used ?? 0);
        }

        // Carriageway width (metres, as planet_road.wgsl draws it), the gap between lights, and whether it has rails.
        private static (float Width, float LightEvery, bool Railed) Look(RoadClass roadClass)
        {
            switch (roadClass)
            {
                case RoadClass.Motorway:
                    return (11.0f, 60.0f, true);
                case RoadClass.Trunk:
                    return (8.0f, 50.0f, true);
                default:
                    return (7.5f, 40.0f, false);
            }
        }

        private void PlantAlong(Vector3[] line, Vector3 camera, float unitsPerMetre, float width, float lightEvery, bool railed,
            float barrierReach, float lightReach, Pool? barriers, Pool? lights)
        {
            var piece = PieceMetres * unitsPerMetre;
            var lightGap = lightEvery * unitsPerMetre;
            var travelled = 0.0f;
            var nextLight = lightGap * 0.5f;
            for (var i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                var length = Vector3.Distance(a, b);
                if (length <= 0.0f)
                {
                    continue;
                }
                // A segment wholly out of reach is skipped, keeping the light spacing counted along it.
                var nearest = NearestOnSegment(camera, a, b);
                if (Vector3.Distance(nearest, camera) > lightReach + length)
                {
                    travelled += length;
                    while (nextLight < travelled)
                    {
                        nextLight += lightGap;
                    }
                    continue;
                }

                var along = (b - a) / length;
                var steps = Math.Max(1, (int)MathF.Ceiling(length / piece));
                for (var s = 0; s < steps; s++)
                {
                    var from = a + along * (length * s / steps);
                    var to = a + along * (length * (s + 1) / steps);
                    var centre = (from + to) * 0.5f;
                    if (railed && barriers != null && barriers.Used < MaxBarriers && Vector3.Distance(centre, camera) < barrierReach)
                    {
                        // Its rails run along X at z = +-0.5, 0.08 tall: stretched to the piece, the road's width and rail height.
                        Place(barriers, centre, along, Vector3.Distance(from, to) * 1.04f, width * unitsPerMetre, RailMetres / 0.08f * unitsPerMetre);
                    }
                }

                // Lights at the side, their arms reaching over the road (the model's arm is along -Z).
                while (nextLight < travelled + length)
                {
                    var at = a + along * (nextLight - travelled);
                    nextLight += lightGap;
                    if (lights == null || lights.Used >= MaxLights || Vector3.Distance(at, camera) > lightReach)
                    {
                        continue;
                    }
                    var up = Vector3.Normalize(at);
                    var across = Vector3.Normalize(Vector3.Cross(up, along));
                    var foot = at + across * ((width * 0.5f + 1.0f) * unitsPerMetre);
                    var size = LightMetres / 0.675f * unitsPerMetre;
                    PlaceTurned(lights, foot, along, up, across, size);
                }
                travelled += length;
            }
        }

        private static Vector3 NearestOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = Math.Clamp(Vector3.Dot(point - a, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0.0f, 1.0f);
            return a + ab * t;
        }

        // X along the road (stretched to `length`), Y up (`height`), Z across (`width`).
        private void Place(Pool pool, Vector3 centre, Vector3 along, float length, float width, float height)
        {
            var up = Vector3.Normalize(centre);
            along = Vector3.Normalize(along - up * Vector3.Dot(along, up));
            var across = Vector3.Cross(along, up);
            var placement = new Matrix4x4(
                along.X * length, along.Y * length, along.Z * length, 0.0f,
                up.X * height, up.Y * height, up.Z * height, 0.0f,
                across.X * width, across.Y * width, across.Z * width, 0.0f,
                centre.X, centre.Y, centre.Z, 1.0f);
            Use(pool, placement);
        }

        // Uniformly scaled, its Z turned to `outward` (so its -Z arm reaches back over the road).
        private void PlaceTurned(Pool pool, Vector3 foot, Vector3 along, Vector3 up, Vector3 outward, float size)
        {
            var x = Vector3.Normalize(Vector3.Cross(up, outward));
            var placement = new Matrix4x4(
                x.X * size, x.Y * size, x.Z * size, 0.0f,
                up.X * size, up.Y * size, up.Z * size, 0.0f,
                outward.X * size, outward.Y * size, outward.Z * size, 0.0f,
                foot.X, foot.Y, foot.Z, 1.0f);
            Use(pool, placement);
        }

        private Pool? Begin(string asset)
        {
            if (!_pools.TryGetValue(asset, out var pool))
            {
                // Loaded after the scene walk, which loading a model would disturb; planted from the next pass.
                if (_loading.Add(asset))
                {
                    _context.World.PostUpdateFutures.Add(() => _pools[asset] = new Pool(_library.Model(asset)));
                }
                return null;
            }
            if (pool.Model == null)
            {
                return null;
            }
            pool.Used = 0;
            return pool;
        }

        private void Use(Pool pool, Matrix4x4 placement)
        {
            if (pool.Used == pool.Copies.Count)
            {
                var copy = pool.Model!.Instantiate(_context, $"Road prop {pool.Copies.Count}", placement);
                copy.IncludeInEnvironmentCapture = false;
                var world = _context.World;
                world.PostUpdateFutures.Add(() =>
                {
                    lock (world.SceneLock)
                    {
                        _parent.AddChild(copy);
                    }
                });
                pool.Copies.Add(copy);
            }
            var placed = pool.Copies[pool.Used++];
            placed.LocalTransform = placement;
            Show(placed, true);
        }

        private static void End(Pool? pool)
        {
            if (pool == null)
            {
                return;
            }
            for (var i = pool.Used; i < pool.Copies.Count; i++)
            {
                Show(pool.Copies[i], false);
            }
        }

        private void HideAll()
        {
            _shown = false;
            _plantedRevision = -1;
            foreach (var pool in _pools.Values)
            {
                pool.Used = 0;
                End(pool);
            }
            Planted = 0;
        }

        private static void Show(ManaSceneNode node, bool shown)
        {
            foreach (var part in ManaModel.MeshNodes(node))
            {
                part.MeshRenderer.IsVisible = shown;
            }
        }

        private sealed class Pool(ManaModel? model)
        {
            public ManaModel? Model { get; } = model;
            public List<ManaSceneNode> Copies { get; } = [];
            public int Used { get; set; }
        }
    }
}
