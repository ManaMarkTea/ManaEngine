using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;
using ManaVoxel.Code.GridMaps;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>
    /// Shows a <see cref="TywGame"/>: hex tiles, scenery, track, buildings and trains. Rebuilds only what a hex's
    /// revision says changed, and moves the trains every frame. Owns every node it adds, so a new match can
    /// clear the board without touching anything else in the world.
    /// </summary>
    internal sealed class TywBoardView
    {
        public static readonly Vector4[] PlayerColors =
        {
            new(0.90f, 0.22f, 0.20f, 1.0f),
            new(0.22f, 0.45f, 0.95f, 1.0f),
            new(0.25f, 0.78f, 0.30f, 1.0f),
            new(0.98f, 0.82f, 0.18f, 1.0f),
            new(0.62f, 0.32f, 0.88f, 1.0f),
            new(0.98f, 0.55f, 0.15f, 1.0f),
            new(0.15f, 0.78f, 0.78f, 1.0f),
            new(0.95f, 0.45f, 0.70f, 1.0f)
        };

        public static readonly Vector4[] ResourceColors =
        {
            new(0.55f, 0.34f, 0.17f, 1.0f),
            new(0.66f, 0.66f, 0.70f, 1.0f),
            new(0.96f, 0.80f, 0.28f, 1.0f),
            new(0.13f, 0.13f, 0.15f, 1.0f)
        };

        private const string Forest = "zip:kenney_mini-forest_1.0.zip/Models/GLB format/";
        private const string Factory = "zip:kenney_factory-kit_3.0.zip/Models/GLB format/";
        private const string Trains = "zip:kenney_train-kit.zip/Models/GLB format/";
        private const string Kit = "zip:kenney_hexagon-kit.zip/Models/GLB format/";

        // The hexagon kit's tiles are 1 across the flats; ours are HexRadius corner to centre.
        private static readonly float KitScale = TywGeometry.HexRadius / (1.0f / TywHex.Sqrt3);

        private static readonly string[] CargoWagons =
        {
            Trains + "train-carriage-lumber.glb",
            Trains + "train-carriage-dirt.glb",
            Trains + "train-carriage-box.glb",
            Trains + "train-carriage-coal.glb"
        };

        private const string EmptyWagon = Trains + "train-carriage-flatbed.glb";
        private const string Locomotive = Trains + "train-locomotive-a.glb";
        private const float TrainLength = 1.3f;
        private const float WagonGap = 1.28f;
        // The kit's locomotive faces +Z (its front wheels are there), which Place turns to +X.
        private static readonly Vector3 ChimneyOffset = new(0.45f, 0.92f, 0.0f);

        private readonly ManaContext _context;
        private readonly ManaScene _scene;
        private readonly Dictionary<string, GridMapPrototype> _models = new();
        private readonly Dictionary<(string, int), ManaMaterial> _tinted = new();
        private readonly List<ManaSceneNode> _owned = new();
        private readonly Dictionary<TywHex, HexNodes> _hexes = new();
        private readonly Dictionary<int, TrainNodes> _trains = new();
        private readonly TywMesh?[] _demandMeshes = new TywMesh?[4];

        private TywGame? _game;
        private ManaMeshSceneNode? _hover;
        // Per seat: each player's route preview and cursor (the mouse player is seat 0, pads take the others).
        private readonly Dictionary<int, (ManaSceneNode? Node, string Key)> _previews = new();
        private readonly Dictionary<int, (ManaMeshSceneNode Ring, ManaMeshSceneNode Pin)> _cursors = new();
        private float _clock;

        public TywBoardView(ManaContext context, ManaScene scene)
        {
            _context = context;
            _scene = scene;
        }

        private sealed class HexNodes
        {
            public int Revision = -1;
            public bool Cleared;
            public ManaMeshSceneNode? Track;
            public int TrackLinks;
            public float TrackBornAt = float.NegativeInfinity;
            public ManaSceneNode? Fire;
            public ManaSceneNode? Torch;
            public float BuildingBornAt = float.NegativeInfinity;
            public Vector3 BuildingAt;
            public ManaSceneNode? Building;
            public TywBuilding BuildingKind;
            public ManaMeshSceneNode? Stock;
            public int StockShown = -1;
            public ManaMeshSceneNode? Demand;
            public TywResource DemandShown = (TywResource)(-1);
        }

        private sealed class TrainNodes
        {
            public ManaSceneNode Locomotive = null!;
            public ManaSceneNode? Wagon;
            public int WagonCargo = -2;
            public ManaParticleEmitterComponent? Smoke;
            public PoseBlend LocomotivePose = new();
            public PoseBlend WagonPose = new();
        }

        /// <summary>
        /// Where a car was drawn last, so a jump (a train turning round at a stop puts its wagon on the other side)
        /// is eased over a moment instead of snapping.
        /// </summary>
        private sealed class PoseBlend
        {
            private const float Seconds = 0.45f;

            public bool Has;
            public Vector3 Position;
            public float Yaw;
            private Vector3 _fromPosition;
            private float _fromYaw;
            private float _startedAt = float.NegativeInfinity;

            public (Vector3 Position, float Yaw) Step(Vector3 position, float yaw, float now)
            {
                if (Has && (Vector3.Distance(position, Position) > 0.9f || MathF.Abs(AngleBetween(Yaw, yaw)) > 1.0f) && now - _startedAt > Seconds)
                {
                    _fromPosition = Position;
                    _fromYaw = Yaw;
                    _startedAt = now;
                }

                var t = Math.Clamp((now - _startedAt) / Seconds, 0.0f, 1.0f);
                if (Has && t < 1.0f)
                {
                    var ease = t * t * (3.0f - 2.0f * t);
                    position = Vector3.Lerp(_fromPosition, position, ease);
                    yaw = _fromYaw + AngleBetween(_fromYaw, yaw) * ease;
                }

                Has = true;
                Position = position;
                Yaw = yaw;
                return (position, yaw);
            }

            private static float AngleBetween(float from, float to)
            {
                var delta = (to - from) % MathF.Tau;
                return delta > MathF.PI ? delta - MathF.Tau : delta < -MathF.PI ? delta + MathF.Tau : delta;
            }
        }

        /// <summary>Loads every model the board uses, off the main thread during the loading screen.</summary>
        public void Preload()
        {
            var assets = new List<string>
            {
                Forest + "rocks-low.glb", Forest + "stones.glb", Forest + "rocks-high.glb",
                Factory + "hopper-round.glb", Factory + "hopper-high-square.glb", Locomotive, EmptyWagon
            };
            assets.AddRange(CargoWagons);
            assets.AddRange(new[]
            {
                "water", "water-rocks", "sand", "sand-rocks", "sand-desert", "grass", "grass-forest", "stone",
                "stone-hill", "stone-rocks", "stone-mountain", "grass-hill", "building-port", "unit-tree", "unit-house", "unit-mansion",
                "unit-mill", "unit-tower"
            }.Select(name => Kit + name + ".glb"));
            foreach (var asset in assets)
            {
                Model(asset);
            }
        }

        /// <summary>The middle of the board and how far it reaches, in world units on the ground.</summary>
        public static (Vector3 Center, Vector2 HalfSize) Extent(TywBoard board)
        {
            var points = board.Tiles.Select(t => TywGeometry.Center(t.Hex)).ToList();
            var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Z));
            var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Z));
            var middle = (min + max) * 0.5f;
            return (new Vector3(middle.X, 0.0f, middle.Y), (max - min) * 0.5f);
        }

        /// <summary>Clears whatever was shown and lays out a fresh board for <paramref name="game"/>.</summary>
        public void Show(TywGame game)
        {
            lock (_context.World.SceneLock)
            {
                foreach (var node in _owned)
                {
                    _scene.Children.Remove(node);
                }

                _owned.Clear();
                _hexes.Clear();
                _trains.Clear();
                _chunks.Clear();
                _game = game;

                // Open sea past the map's edge, a little above the kit's own (shallow) water tiles.
                var (center, half) = Extent(game.Board);
                var sea = new ManaWaterNode(_context, new Vector2(half.X * 2.0f + 160.0f, half.Y * 2.0f + 160.0f), 128) { Name = "TYW Sea" };
                sea.Position = center + new Vector3(0.0f, 0.13f * KitScale, 0.0f);
                sea.UpdateTransform();
                Add(sea);

                foreach (var tile in game.Board.Tiles)
                {
                    _hexes[tile.Hex] = new HexNodes { Cleared = IsCleared(game, tile) };
                    var key = ChunkOf(tile.Hex);
                    if (!_chunks.TryGetValue(key, out var chunk))
                    {
                        chunk = new Chunk();
                        _chunks[key] = chunk;
                    }

                    chunk.Tiles.Add(tile);
                    AddEffects(tile);
                }

                foreach (var chunk in _chunks.Values)
                {
                    BuildChunk(chunk);
                }

                _hover = new ManaMeshSceneNode(_context, new TywMesh(_context,
                    b => b.HexRing(Vector3.Zero, TywGeometry.HexRadius - 0.08f, 0.14f, 0.0f, new Vector4(1.0f, 1.0f, 1.0f, 1.0f)), unlit: true))
                {
                    Name = "TYW Hover"
                };
                _hover.MeshRenderer.IsVisible = false;
                Add(_hover);
                _previews.Clear();
                _cursors.Clear();
            }
        }

        private void Add(ManaSceneNode node)
        {
            _scene.Children.Add(node);
            _owned.Add(node);
        }

        private void Remove(ManaSceneNode? node)
        {
            if (node != null)
            {
                _scene.Children.Remove(node);
                _owned.Remove(node);
            }
        }

        // ---- The ground ---------------------------------------------------------------------------------------

        private const int ChunkSize = 8;

        /// <summary>A block of hexes whose ground is drawn as one batch, rebuilt when any of them changes.</summary>
        private sealed class Chunk
        {
            public readonly List<TywTile> Tiles = new();
            public readonly List<ManaSceneNode> Nodes = new();
            public bool Dirty;
        }

        private readonly Dictionary<(int, int), Chunk> _chunks = new();

        private static (int, int) ChunkOf(TywHex hex)
        {
            var column = hex.Q + (hex.R - (hex.R & 1)) / 2;
            return (column / ChunkSize, hex.R / ChunkSize);
        }

        private void BuildChunk(Chunk chunk)
        {
            foreach (var node in chunk.Nodes)
            {
                Remove(node);
            }

            chunk.Nodes.Clear();
            var batch = new TywGroundBatch();
            foreach (var tile in chunk.Tiles)
            {
                AppendGround(batch, tile, _hexes[tile.Hex].Cleared);
            }

            foreach (var node in batch.Build(_context, "TYW Ground"))
            {
                chunk.Nodes.Add(node);
                Add(node);
            }

            chunk.Dirty = false;
        }

        // Track through a forest, hill or mountain clears it: trains do not drive through trees.
        private static bool IsCleared(TywGame game, TywTile tile) =>
            tile.Owner >= 0 || tile.Building is TywBuilding.Extractor or TywBuilding.Factory or TywBuilding.Station || game.AllLinks(tile.Hex).Any();

        /// <summary>
        /// One hex of ground: the kit's tile for its terrain, turned a random sixth for variety, with whatever stands
        /// in its corners -- trees left by a clearing, houses round a town, a depot's shed. Corners 4, 5 and 0 (the
        /// back of the hex) are where track never runs.
        /// </summary>
        private void AppendGround(TywGroundBatch batch, TywTile tile, bool cleared)
        {
            var random = new Random(tile.Hex.Q * 7919 + tile.Hex.R * 104729 + 17);
            var turn = random.Next(6);
            var center = TywGeometry.Center(tile.Hex) with { Y = 0.0f };

            var name = tile.Terrain switch
            {
                TywTerrain.Water => random.Next(12) == 0 ? "water-rocks" : "water",
                TywTerrain.Sand => random.Next(5) == 0 ? "sand-rocks" : "sand",
                TywTerrain.Desert => cleared ? "sand" : "sand-desert",
                // Not the kit's dirt tile: every vertex shares one UV, which this renderer shades flat grey.
                TywTerrain.Fields => "grass",
                TywTerrain.Forest => cleared ? "grass" : "grass-forest",
                TywTerrain.Hills => cleared ? "stone" : random.Next(3) switch { 0 => "stone-hill", 1 => "stone-rocks", _ => "grass-hill" },
                TywTerrain.Mountain => cleared ? "stone" : "stone-mountain",
                TywTerrain.Port => "building-port",
                _ => "grass"
            };

            if (tile.Terrain == TywTerrain.Port)
            {
                // The dock faces the land it serves: turn the tile so its quay is toward the most land.
                turn = Enumerable.Range(0, 6).MaxBy(t => PortFacesLand(tile, t));
            }

            AddKit(batch, name, center, turn * MathF.PI / 3.0f);
            var top = TywGeometry.TopY;
            Vector3 Corner(int corner, float reach = 0.6f) => TywMeshBuilder.Corner(center, TywGeometry.HexRadius * reach, corner, top);

            switch (tile.Terrain)
            {
                case TywTerrain.Forest when cleared:
                    AddKit(batch, "unit-tree", Corner(4), random.NextSingle() * MathF.Tau);
                    AddKit(batch, "unit-tree", Corner(0, 0.55f), random.NextSingle() * MathF.Tau);
                    break;
                case TywTerrain.Hills when cleared:
                    AddProp(batch, Forest + "rocks-low.glb", Corner(5), 0.45f, random);
                    break;
                case TywTerrain.Mountain when cleared:
                    AddProp(batch, Forest + "rocks-high.glb", Corner(4, 0.62f), 1.2f, random);
                    AddProp(batch, Forest + "rocks-high.glb", Corner(0, 0.62f), 0.9f, random);
                    break;
                case TywTerrain.Town:
                    AddKit(batch, "unit-house", Corner(4), random.Next(6) * MathF.PI / 3.0f);
                    AddKit(batch, "unit-mansion", Corner(5), random.Next(6) * MathF.PI / 3.0f);
                    AddKit(batch, "unit-house", Corner(0), random.Next(6) * MathF.PI / 3.0f);
                    AddKit(batch, "unit-tower", Corner(1, 0.62f), 0.0f);
                    break;
            }

            // Crop rows, yield marks and the depot's shed are drawn rather than loaded.
            var extras = new TywMesh(_context, b =>
            {
                if (tile.Terrain == TywTerrain.Fields)
                {
                    // A patch of tilled earth with golden rows in each back corner.
                    foreach (var corner in new[] { 4, 5, 0 })
                    {
                        var at = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.58f, corner, top + 0.02f);
                        b.Box(at, Vector3.UnitX, new Vector3(0.8f, 0.04f, 0.62f), new Vector4(0.62f, 0.42f, 0.25f, 1.0f));
                        for (var row = -1; row <= 1; row++)
                        {
                            b.Box(at + new Vector3(0.0f, 0.06f, row * 0.18f), Vector3.UnitX, new Vector3(0.72f, 0.1f, 0.1f), new Vector4(0.98f, 0.82f, 0.3f, 1.0f));
                        }
                    }
                }

                // Yield: one to three small marks in the resource's colour at the front-left corner.
                if (tile.Richness > 0)
                {
                    var mark = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.7f, 3, top + 0.04f);
                    var color = Vector4.Lerp(ResourceColors[(int)tile.Produces], Vector4.One, 0.25f);
                    for (var i = 0; i < tile.Richness; i++)
                    {
                        b.Box(mark + new Vector3((i - (tile.Richness - 1) * 0.5f) * 0.22f, 0.0f, 0.0f), Vector3.UnitX, new Vector3(0.15f, 0.08f, 0.15f), color);
                    }
                }

                if (tile.Terrain == TywTerrain.Depot)
                {
                    var owner = PlayerColors[tile.BuildingOwner];
                    b.HexRing(Vector3.Zero, TywGeometry.HexRadius - 0.06f, 0.22f, top + 0.02f, owner);
                    var shed = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.58f, 5, top);
                    b.Box(shed + new Vector3(0, 0.3f, 0), Vector3.UnitX, new Vector3(1.0f, 0.6f, 0.55f), new Vector4(0.9f, 0.87f, 0.8f, 1.0f));
                    b.Box(shed + new Vector3(0, 0.66f, 0), Vector3.UnitX, new Vector3(1.1f, 0.12f, 0.65f), owner);
                    var pole = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.6f, 1, top);
                    b.Box(pole + new Vector3(0, 0.6f, 0), Vector3.UnitX, new Vector3(0.05f, 1.2f, 0.05f), new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                    b.Box(pole + new Vector3(0.2f, 1.05f, 0), Vector3.UnitX, new Vector3(0.38f, 0.24f, 0.03f), owner);
                }
            });

            if (!extras.IsEmpty)
            {
                batch.Add(extras, extras.Materials[0], Matrix4x4.CreateTranslation(center));
            }
        }

        // How much land lies on the side a port tile's quay faces when the tile is turned t sixths. The kit's port
        // has its land half toward local +Z.
        private int PortFacesLand(TywTile tile, int t)
        {
            var facing = Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateRotationY(t * MathF.PI / 3.0f));
            return Enumerable.Range(0, TywHex.DirectionCount).Count(d =>
            {
                var neighbour = _game?.Board[tile.Hex.Neighbor(d)];
                if (neighbour == null || neighbour.Terrain is TywTerrain.Water or TywTerrain.Port)
                {
                    return false;
                }

                var toward = TywGeometry.Center(neighbour.Hex) - TywGeometry.Center(tile.Hex);
                return Vector3.Dot(Vector3.Normalize(toward with { Y = 0 }), facing) > 0.3f;
            });
        }

        private void AddKit(TywGroundBatch batch, string name, Vector3 at, float yaw)
        {
            if (Model(Kit + name + ".glb") is { } prototype)
            {
                batch.Add(prototype, Matrix4x4.CreateScale(KitScale) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(at));
            }
        }

        private void AddProp(TywGroundBatch batch, string asset, Vector3 at, float height, Random random)
        {
            if (Model(asset) is { } prototype)
            {
                batch.Add(prototype, Fit(prototype, height, lengthwise: false, footprint: 1.0f) *
                                     Matrix4x4.CreateRotationY(random.NextSingle() * MathF.Tau) * Matrix4x4.CreateTranslation(at));
            }
        }

        /// <summary>
        /// Life on the landmarks: a town's chimney smokes and a lamp burns over it; a port has a harbour light. Lamps are
        /// few on purpose -- the renderer has 64 slots for placed lights, and the torches players build need most of them.
        /// </summary>
        private void AddEffects(TywTile tile)
        {
            if (tile.Terrain is not (TywTerrain.Town or TywTerrain.Port))
            {
                return;
            }

            var town = tile.Terrain == TywTerrain.Town;
            var center = TywGeometry.Center(tile.Hex);
            var at = town ? TywMeshBuilder.Corner(center, TywGeometry.HexRadius * 0.6f, 5, center.Y) + new Vector3(0, 1.1f, 0) : center + new Vector3(0, 1.5f, 0);
            var lamp = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(at)) { Name = "TYW Lamp" };
            lamp.Components.Add(new ManaLightSourceComponent(lamp)
            {
                LightType = ManaLightType.Point,
                Color = town ? new Vector4(1.0f, 0.72f, 0.4f, 1.0f) : new Vector4(0.75f, 0.88f, 1.0f, 1.0f),
                Intensity = town ? 1.6f : 0.8f,
                Range = town ? 6.0f : 4.0f,
                FlickerAmount = town ? 0.12f : 0.0f,
                FlickerSpeed = 3.0f
            });
            Add(lamp);

            if (town)
            {
                var chimney = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(at)) { Name = "TYW Chimney" };
                chimney.Components.Add(new ManaParticleEmitterComponent(chimney)
                {
                    MaxParticles = 30,
                    Rate = 4.0f,
                    Speed = 0.4f,
                    SpreadDegrees = 10.0f,
                    Lifetime = 2.4f,
                    Gravity = new Vector3(0, 0.35f, 0),
                    Drag = 0.5f,
                    Turbulence = 0.6f,
                    StartSize = 0.1f,
                    EndSize = 0.5f,
                    StartColor = new Vector4(0.85f, 0.85f, 0.85f, 0.6f),
                    EndColor = new Vector4(0.75f, 0.75f, 0.78f, 0.0f)
                });
                Add(chimney);
            }
        }

        /// <summary>Marks a hex's block for rebuilding once track or a building clears it.</summary>
        private void SyncGround(TywGame game, TywTile tile, HexNodes nodes)
        {
            var cleared = IsCleared(game, tile);
            if (cleared != nodes.Cleared)
            {
                nodes.Cleared = cleared;
                _chunks[ChunkOf(tile.Hex)].Dirty = true;
            }
        }

        // ---- Models -----------------------------------------------------------------------------------------

        private GridMapPrototype? Model(string asset)
        {
            if (_models.TryGetValue(asset, out var cached))
            {
                return cached.HasGeometry ? cached : null;
            }

            var prototype = GridMapPrototype.Load(asset, _context);
            _models[asset] = prototype;
            if (!prototype.HasGeometry)
            {
                ManaLog.WriteDiagnostic($"Train Yard Wars: model '{asset}' did not load: {prototype.Error}");
                return null;
            }

            return prototype;
        }

        /// <summary>
        /// A model scaled to <paramref name="size"/> -- its height, or with <paramref name="lengthwise"/> its long
        /// side, laid along local +X -- standing on the origin; tinted toward a player's colour when one is given.
        /// </summary>
        private ManaSceneNode? Place(string asset, float size, int player, bool lengthwise = false, float footprint = float.MaxValue)
        {
            var prototype = Model(asset);
            if (prototype == null)
            {
                return null;
            }

            var node = prototype.Instantiate(_context, Path.GetFileNameWithoutExtension(asset), Fit(prototype, size, lengthwise, footprint));
            if (player >= 0)
            {
                foreach (var child in node.Children.OfType<ManaMeshSceneNode>())
                {
                    child.MeshRenderer.Material = Tinted(asset, child.MeshRenderer.Material, player);
                }
            }

            return node;
        }

        /// <summary>
        /// Sizes a model to <paramref name="size"/> -- its height, or with <paramref name="lengthwise"/> its long side,
        /// laid along local +X -- standing on the origin.
        /// </summary>
        private static Matrix4x4 Fit(GridMapPrototype prototype, float size, bool lengthwise, float footprint)
        {
            var extent = prototype.BoundsMax - prototype.BoundsMin;
            var middle = (prototype.BoundsMax + prototype.BoundsMin) * 0.5f;
            var turn = Matrix4x4.Identity;
            float scale;
            if (lengthwise)
            {
                // Long along Z: turn it so the length runs along +X like everything else here.
                var alongZ = extent.Z > extent.X;
                turn = alongZ ? Matrix4x4.CreateRotationY(MathF.PI * 0.5f) : Matrix4x4.Identity;
                scale = size / MathF.Max(0.001f, MathF.Max(extent.X, extent.Z));
            }
            else
            {
                // Never wider than the footprint, so a broad rock stays in its corner, off the track.
                scale = MathF.Min(size / MathF.Max(0.001f, extent.Y), footprint / MathF.Max(0.001f, MathF.Max(extent.X, extent.Z)));
            }

            return Matrix4x4.CreateTranslation(-middle.X, -prototype.BoundsMin.Y, -middle.Z) * Matrix4x4.CreateScale(scale) * turn;
        }

        private ManaMaterial Tinted(string asset, ManaMaterial source, int player)
        {
            var key = ($"{asset}#{source.Name}", player);
            if (_tinted.TryGetValue(key, out var material))
            {
                return material;
            }

            material = (ManaMaterial)source.Clone();
            material.Albedo.Value = Vector4.Lerp(Vector4.One, PlayerColors[player], 0.55f);
            _tinted[key] = material;
            return material;
        }

        // ---- Every frame ------------------------------------------------------------------------------------

        public void Sync(float deltaSeconds)
        {
            _clock += deltaSeconds;
            var game = _game;
            if (game == null)
            {
                return;
            }

            lock (_context.World.SceneLock)
            {
                foreach (var tile in game.Board.Tiles)
                {
                    var nodes = _hexes[tile.Hex];
                    if (nodes.Revision != tile.Revision)
                    {
                        nodes.Revision = tile.Revision;
                        SyncGround(game, tile, nodes);
                        RebuildTrack(game, tile, nodes);
                        SyncTorch(tile, nodes);
                        RebuildBuilding(tile, nodes);
                    }

                    SyncStock(tile, nodes);
                    SyncDemand(tile, nodes);
                    Animate(nodes);
                }

                foreach (var chunk in _chunks.Values.Where(c => c.Dirty))
                {
                    BuildChunk(chunk);
                }

                foreach (var train in game.Trains)
                {
                    SyncTrain(game, train);
                }
            }
        }

        private void RebuildTrack(TywGame game, TywTile tile, HexNodes nodes)
        {
            var links = game.AllLinks(tile.Hex).ToList();
            if (links.Count == 0)
            {
                return;
            }

            var damaged = tile.IsDamaged(game.Time);
            SyncFire(tile, nodes, damaged);
            if (links.Count > nodes.TrackLinks)
            {
                nodes.TrackBornAt = _clock;
            }

            nodes.TrackLinks = links.Count;
            var mesh = new TywMesh(_context, b =>
            {
                foreach (var owner in links.Select(l => l.Owner).Distinct())
                {
                    var directions = links.Where(l => l.Owner == owner).Select(l => l.Direction).ToList();
                    foreach (var piece in TywGeometry.TrackPieces(tile.Hex, directions, tile.IsTerminal))
                    {
                        AddRails(b, piece, PlayerColors[owner], damaged);
                    }
                }
            });

            if (nodes.Track == null)
            {
                nodes.Track = new ManaMeshSceneNode(_context, mesh) { Name = $"TYW Track {tile.Hex}" };
                Add(nodes.Track);
            }
            else
            {
                nodes.Track.Mesh = mesh;
                nodes.Track.MeshRenderer.Mesh = mesh;
                nodes.Track.MeshRenderer.Material = mesh.Materials[0];
            }
        }

        private static void AddRails(TywMeshBuilder b, Vector3[] points, Vector4 owner, bool damaged)
        {
            var ballast = damaged ? new Vector4(0.15f, 0.12f, 0.1f, 1.0f) : Vector4.Lerp(new Vector4(0.35f, 0.33f, 0.3f, 1.0f), owner, 0.7f);
            var tie = damaged ? new Vector4(0.12f, 0.1f, 0.09f, 1.0f) : new Vector4(0.42f, 0.28f, 0.17f, 1.0f);
            var rail = damaged ? new Vector4(0.6f, 0.2f, 0.12f, 1.0f) : new Vector4(0.82f, 0.84f, 0.88f, 1.0f);
            const float Gauge = 0.34f;
            var y = TywGeometry.TopY;

            var travelled = 0.0f;
            var nextTie = 0.12f;
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var a = points[i];
                var c = points[i + 1];
                var along = c - a;
                var length = along.Length();
                if (length < 1e-4f)
                {
                    continue;
                }

                along /= length;
                var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, along));
                var mid = (a + c) * 0.5f;
                b.Box(mid with { Y = y + 0.015f }, along, new Vector3(length + 0.04f, 0.03f, 0.72f), ballast);
                var gap = damaged && i == points.Length / 2 - 1;
                if (!gap)
                {
                    b.Box(mid + side * Gauge * 0.5f + new Vector3(0, 0.085f - mid.Y + y, 0), along, new Vector3(length + 0.01f, 0.05f, 0.05f), rail);
                    b.Box(mid - side * Gauge * 0.5f + new Vector3(0, 0.085f - mid.Y + y, 0), along, new Vector3(length + 0.01f, 0.05f, 0.05f), rail);
                }

                while (nextTie <= travelled + length)
                {
                    var t = (nextTie - travelled) / length;
                    var at = Vector3.Lerp(a, c, t);
                    b.Box(at with { Y = y + 0.045f }, along, new Vector3(0.11f, 0.04f, 0.58f), tie);
                    nextTie += 0.24f;
                }

                travelled += length;
            }
        }

        private void RebuildBuilding(TywTile tile, HexNodes nodes)
        {
            var kind = tile.Building is TywBuilding.Extractor or TywBuilding.Factory or TywBuilding.Station ? tile.Building : TywBuilding.None;
            if (kind == nodes.BuildingKind)
            {
                return;
            }

            if (nodes.Building != null)
            {
                _scene.Children.Remove(nodes.Building);
                _owned.Remove(nodes.Building);
                nodes.Building = null;
            }

            nodes.BuildingKind = kind;
            if (kind == TywBuilding.None)
            {
                return;
            }

            var at = TywMeshBuilder.Corner(TywGeometry.Center(tile.Hex), TywGeometry.HexRadius * 0.6f, 1, TywGeometry.TopY);
            ManaSceneNode? node;
            if (kind == TywBuilding.Station)
            {
                var owner = PlayerColors[tile.BuildingOwner];
                node = new ManaMeshSceneNode(_context, new TywMesh(_context, b =>
                {
                    b.Box(new Vector3(0, 0.08f, 0), Vector3.UnitX, new Vector3(0.9f, 0.16f, 0.5f), new Vector4(0.8f, 0.78f, 0.72f, 1.0f));
                    b.Box(new Vector3(-0.35f, 0.4f, 0), Vector3.UnitX, new Vector3(0.06f, 0.5f, 0.06f), new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                    b.Box(new Vector3(0.35f, 0.4f, 0), Vector3.UnitX, new Vector3(0.06f, 0.5f, 0.06f), new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                    b.Box(new Vector3(0, 0.68f, 0), Vector3.UnitX, new Vector3(1.0f, 0.08f, 0.6f), owner);
                }));
            }
            else
            {
                node = Place(kind == TywBuilding.Factory ? Factory + "hopper-high-square.glb" : Factory + "hopper-round.glb",
                    kind == TywBuilding.Factory ? 1.1f : 0.8f, tile.BuildingOwner);
            }

            if (node == null)
            {
                return;
            }

            nodes.BuildingAt = at;
            nodes.BuildingBornAt = _clock;
            node.LocalTransform = Matrix4x4.CreateScale(0.01f) * Matrix4x4.CreateTranslation(at);
            nodes.Building = node;
            Add(node);
        }

        // New track drops onto the board; a new building springs up out of it.
        private void Animate(HexNodes nodes)
        {
            const float DropSeconds = 0.35f;
            var drop = (_clock - nodes.TrackBornAt) / DropSeconds;
            if (nodes.Track != null && drop < 3.0f)
            {
                var t = Math.Clamp(drop, 0.0f, 1.0f);
                nodes.Track.LocalTransform = Matrix4x4.CreateTranslation(0.0f, 0.9f * (1.0f - t) * (1.0f - t), 0.0f);
            }

            const float PopSeconds = 0.4f;
            var pop = (_clock - nodes.BuildingBornAt) / PopSeconds;
            if (nodes.Building != null && pop < 3.0f)
            {
                // Back-out easing: overshoots a little, then settles.
                var t = Math.Clamp(pop, 0.0f, 1.0f) - 1.0f;
                var scale = MathF.Max(0.01f, 1.0f + 2.7f * t * t * t + 1.7f * t * t);
                nodes.Building.LocalTransform = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(nodes.BuildingAt);
            }
        }

        /// <summary>
        /// A torch: a post with an iron bowl, a band in its owner's colour, a flame and a real flickering light -- the
        /// light is the point. It stands in the front-left corner, beside the yield marks, clear of the track.
        /// </summary>
        private void SyncTorch(TywTile tile, HexNodes nodes)
        {
            if (tile.HasTorch == (nodes.Torch != null))
            {
                return;
            }

            if (!tile.HasTorch)
            {
                Remove(nodes.Torch);
                nodes.Torch = null;
                return;
            }

            var owner = PlayerColors[tile.TorchOwner];
            var at = TywMeshBuilder.Corner(TywGeometry.Center(tile.Hex), TywGeometry.HexRadius * 0.5f, 3, TywGeometry.TopY);
            var torch = new ManaMeshSceneNode(_context, new TywMesh(_context, b =>
            {
                b.Box(new Vector3(0, 0.45f, 0), Vector3.UnitX, new Vector3(0.08f, 0.9f, 0.08f), new Vector4(0.42f, 0.27f, 0.15f, 1.0f));
                b.Box(new Vector3(0, 0.66f, 0), Vector3.UnitX, new Vector3(0.11f, 0.08f, 0.11f), owner);
                b.Box(new Vector3(0, 0.94f, 0), Vector3.UnitX, new Vector3(0.24f, 0.1f, 0.24f), new Vector4(0.18f, 0.17f, 0.16f, 1.0f));
            }))
            {
                Name = $"TYW Torch {tile.Hex}"
            };
            torch.LocalTransform = Matrix4x4.CreateTranslation(at);

            var flame = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(0, 1.0f, 0)) { Name = "Flame" };
            flame.Components.Add(new ManaParticleEmitterComponent(flame)
            {
                Shape = ManaParticleShape.Flames,
                BlendMode = ManaParticleBlend.Additive,
                MaxParticles = 40,
                Rate = 24.0f,
                Radius = 0.06f,
                Speed = 0.4f,
                SpreadDegrees = 10.0f,
                Lifetime = 0.5f,
                Gravity = new Vector3(0, 1.4f, 0),
                StartSize = 0.17f,
                EndSize = 0.03f,
                StartColor = new Vector4(1.0f, 0.85f, 0.45f, 0.95f),
                EndColor = new Vector4(1.0f, 0.25f, 0.05f, 0.0f)
            });
            torch.AddChild(flame);

            var glow = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(0, 1.2f, 0)) { Name = "Light" };
            glow.Components.Add(new ManaLightSourceComponent(glow)
            {
                LightType = ManaLightType.Point,
                Color = new Vector4(1.0f, 0.62f, 0.28f, 1.0f),
                Intensity = 2.4f,
                Range = 5.5f,
                FlickerAmount = 0.22f,
                FlickerSpeed = 9.0f
            });
            torch.AddChild(glow);
            nodes.Torch = torch;
            Add(torch);
        }

        // Smoke and a few flames over a sabotaged hex, for as long as it stays broken.
        private void SyncFire(TywTile tile, HexNodes nodes, bool damaged)
        {
            if (damaged == (nodes.Fire != null))
            {
                return;
            }

            if (!damaged)
            {
                _scene.Children.Remove(nodes.Fire!);
                _owned.Remove(nodes.Fire!);
                nodes.Fire = null;
                return;
            }

            var fire = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(TywGeometry.Center(tile.Hex) + new Vector3(0, 0.1f, 0)))
            {
                Name = $"TYW Fire {tile.Hex}"
            };
            var flames = new ManaSceneNode(_context, Matrix4x4.Identity) { Name = "Flames" };
            flames.Components.Add(new ManaParticleEmitterComponent(flames)
            {
                Shape = ManaParticleShape.Flames,
                BlendMode = ManaParticleBlend.Additive,
                MaxParticles = 60,
                Rate = 30.0f,
                Radius = 0.35f,
                Speed = 0.6f,
                SpreadDegrees = 12.0f,
                Lifetime = 0.6f,
                Gravity = new Vector3(0, 1.6f, 0),
                StartSize = 0.22f,
                EndSize = 0.04f,
                StartColor = new Vector4(1.0f, 0.8f, 0.35f, 0.9f),
                EndColor = new Vector4(1.0f, 0.2f, 0.05f, 0.0f)
            });
            var smoke = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(0, 0.3f, 0)) { Name = "Smoke" };
            smoke.Components.Add(new ManaParticleEmitterComponent(smoke)
            {
                MaxParticles = 60,
                Rate = 12.0f,
                Radius = 0.3f,
                Speed = 0.5f,
                SpreadDegrees = 15.0f,
                Lifetime = 2.6f,
                Gravity = new Vector3(0, 0.5f, 0),
                Drag = 0.4f,
                Turbulence = 0.8f,
                StartSize = 0.2f,
                EndSize = 0.75f,
                StartColor = new Vector4(0.18f, 0.17f, 0.16f, 0.75f),
                EndColor = new Vector4(0.35f, 0.34f, 0.33f, 0.0f)
            });
            fire.AddChild(flames);
            fire.AddChild(smoke);
            nodes.Fire = fire;
            Add(fire);
        }

        private void SyncStock(TywTile tile, HexNodes nodes)
        {
            var shown = tile.Building is TywBuilding.Extractor or TywBuilding.Factory ? (int)MathF.Floor(tile.Stock) : 0;
            if (shown == nodes.StockShown)
            {
                return;
            }

            nodes.StockShown = shown;
            if (shown == 0)
            {
                if (nodes.Stock != null)
                {
                    nodes.Stock.MeshRenderer.IsVisible = false;
                }

                return;
            }

            var color = ResourceColors[(int)tile.Produces];
            var mesh = new TywMesh(_context, b =>
            {
                for (var i = 0; i < shown; i++)
                {
                    var column = i % 3;
                    var layer = i / 3;
                    b.Box(new Vector3((column - 1) * 0.2f, 0.09f + layer * 0.17f, 0.0f), Vector3.UnitX, new Vector3(0.17f, 0.15f, 0.17f), color);
                }
            });

            if (nodes.Stock == null)
            {
                nodes.Stock = new ManaMeshSceneNode(_context, mesh) { Name = $"TYW Stock {tile.Hex}" };
                nodes.Stock.LocalTransform = Matrix4x4.CreateTranslation(
                    TywMeshBuilder.Corner(TywGeometry.Center(tile.Hex), TywGeometry.HexRadius * 0.62f, 2, TywGeometry.TopY));
                Add(nodes.Stock);
            }
            else
            {
                nodes.Stock.Mesh = mesh;
                nodes.Stock.MeshRenderer.Mesh = mesh;
                nodes.Stock.MeshRenderer.Material = mesh.Materials[0];
                nodes.Stock.MeshRenderer.IsVisible = true;
            }
        }

        // A spinning crate over each town in the colour of what it wants.
        private void SyncDemand(TywTile tile, HexNodes nodes)
        {
            if (tile.Building != TywBuilding.Town)
            {
                return;
            }

            if (nodes.DemandShown != tile.Demand)
            {
                nodes.DemandShown = tile.Demand;
                var mesh = _demandMeshes[(int)tile.Demand] ??= new TywMesh(_context, b =>
                    b.Box(Vector3.Zero, Vector3.UnitX, new Vector3(0.45f, 0.45f, 0.45f), ResourceColors[(int)tile.Demand]));
                if (nodes.Demand == null)
                {
                    nodes.Demand = new ManaMeshSceneNode(_context, mesh) { Name = $"TYW Demand {tile.Hex}" };
                    Add(nodes.Demand);
                }
                else
                {
                    nodes.Demand.Mesh = mesh;
                    nodes.Demand.MeshRenderer.Mesh = mesh;
                    nodes.Demand.MeshRenderer.Material = mesh.Materials[0];
                }
            }

            nodes.Demand!.MeshRenderer.IsVisible = tile.SatisfiedUntil <= (_game?.Time ?? 0.0f);
            var time = _clock;
            var bob = 1.7f + 0.12f * MathF.Sin(time * 2.0f + tile.Hex.Q);
            nodes.Demand!.LocalTransform = Matrix4x4.CreateRotationY(time * 1.2f) * Matrix4x4.CreateRotationX(0.3f) *
                                           Matrix4x4.CreateTranslation(TywGeometry.Center(tile.Hex) + new Vector3(0, bob, 0));
        }

        private void SyncTrain(TywGame game, TywTrain train)
        {
            if (!_trains.TryGetValue(train.Id, out var nodes))
            {
                var locomotive = Place(Locomotive, TrainLength, train.Owner, lengthwise: true);
                if (locomotive == null)
                {
                    return;
                }

                nodes = new TrainNodes { Locomotive = locomotive };
                // Steam from the chimney, at the front end, up top.
                var chimney = new ManaSceneNode(_context, Matrix4x4.CreateTranslation(ChimneyOffset)) { Name = "Chimney" };
                nodes.Smoke = new ManaParticleEmitterComponent(chimney)
                {
                    MaxParticles = 40,
                    Rate = 8.0f,
                    Speed = 0.7f,
                    SpreadDegrees = 12.0f,
                    Lifetime = 1.4f,
                    Gravity = new Vector3(0, 0.5f, 0),
                    Drag = 0.8f,
                    Turbulence = 0.7f,
                    StartSize = 0.14f,
                    EndSize = 0.55f,
                    StartColor = new Vector4(0.97f, 0.97f, 0.97f, 0.9f),
                    EndColor = new Vector4(0.82f, 0.82f, 0.85f, 0.0f)
                };
                chimney.Components.Add(nodes.Smoke);
                locomotive.AddChild(chimney);
                _trains[train.Id] = nodes;
                Add(locomotive);
            }

            var cargo = train.Amount > 0 ? (int)train.Cargo : -1;
            if (cargo != nodes.WagonCargo)
            {
                if (nodes.Wagon != null)
                {
                    _scene.Children.Remove(nodes.Wagon);
                    _owned.Remove(nodes.Wagon);
                }

                nodes.WagonCargo = cargo;
                nodes.Wagon = Place(cargo >= 0 ? CargoWagons[cargo] : EmptyWagon, TrainLength * 0.9f, -1, lengthwise: true);
                if (nodes.Wagon != null)
                {
                    Add(nodes.Wagon);
                }
            }

            bool Terminal(TywHex hex) => game.Board[hex]?.IsTerminal == true;
            var step = TywGeometry.HexRadius * TywHex.Sqrt3;
            var lift = new Vector3(0.0f, 0.11f, 0.0f);
            var moving = train.State is TywTrainState.ToPickup or TywTrainState.ToDrop && !train.IsBlocked;
            if (nodes.Smoke != null)
            {
                nodes.Smoke.Rate = moving ? 9.0f : 1.5f;
            }

            Pose(nodes.Locomotive, nodes.LocomotivePose, train.Path, train.Progress);
            if (nodes.Wagon != null)
            {
                Pose(nodes.Wagon, nodes.WagonPose, train.Path, train.Progress - WagonGap / step);
            }

            void Pose(ManaSceneNode node, PoseBlend blend, List<TywHex> path, float s)
            {
                var here = TywGeometry.PathPoint(path, s, Terminal);
                var ahead = TywGeometry.PathPoint(path, s + 0.08f, Terminal);
                var behind = TywGeometry.PathPoint(path, s - 0.08f, Terminal);
                var direction = ahead - behind;
                direction.Y = 0.0f;
                if (direction.LengthSquared() < 1e-6f)
                {
                    // Standing still at a stop: keep facing the way it last went.
                    direction = new Vector3(node.LocalTransform.M11, 0.0f, node.LocalTransform.M13);
                    if (direction.LengthSquared() < 1e-6f)
                    {
                        direction = Vector3.UnitX;
                    }
                }

                direction = Vector3.Normalize(direction);
                var (position, yaw) = blend.Step(here + lift, MathF.Atan2(-direction.Z, direction.X), _clock);
                node.LocalTransform = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position);
            }
        }

        // ---- Pointer feedback -------------------------------------------------------------------------------

        public void ShowHover(TywHex? hex, Vector4 color)
        {
            if (_hover == null)
            {
                return;
            }

            _hover.MeshRenderer.IsVisible = hex != null;
            if (hex is { } at)
            {
                _hover.MeshRenderer.Material.Albedo.Value = color;
                _hover.LocalTransform = Matrix4x4.CreateTranslation(TywGeometry.Center(at) + new Vector3(0, 0.02f, 0));
            }
        }

        /// <summary>The route new track would take for a seat, drawn as a bright line; rebuilt only when it changes.</summary>
        public void ShowPlan(int seat, IReadOnlyList<TywHex>? path, Vector4 color)
        {
            var key = path == null ? "" : string.Join(";", path) + color;
            if (_previews.TryGetValue(seat, out var current) && current.Key == key)
            {
                return;
            }

            lock (_context.World.SceneLock)
            {
                Remove(current.Node);
                _previews[seat] = (null, key);
                if (path == null || path.Count < 2 || _game == null)
                {
                    return;
                }

                var game = _game;
                bool Terminal(TywHex hex) => game.Board[hex]?.IsTerminal == true;
                var points = new List<Vector3>();
                for (var s = 0.0f; s <= path.Count - 1 + 1e-3f; s += 0.1f)
                {
                    points.Add(TywGeometry.PathPoint(path, s, Terminal) + new Vector3(0, 0.25f, 0));
                }

                var node = new ManaMeshSceneNode(_context, new TywMesh(_context, b =>
                {
                    for (var i = 0; i + 1 < points.Count; i++)
                    {
                        var along = points[i + 1] - points[i];
                        b.Box((points[i] + points[i + 1]) * 0.5f, along, new Vector3(along.Length() + 0.02f, 0.06f, 0.22f), color);
                    }
                }, unlit: true))
                {
                    Name = $"TYW Plan {seat}"
                };
                Add(node);
                _previews[seat] = (node, key);
            }
        }

        /// <summary>
        /// A pad player's cursor: a ring in their colour on the hex it is over, and a pin standing on the exact point,
        /// tall enough to find among eight. Null hides it (the player left).
        /// </summary>
        public void ShowCursor(int seat, Vector3? point, TywHex? hex, Vector4 color, float now)
        {
            if (!_cursors.TryGetValue(seat, out var cursor))
            {
                if (point == null)
                {
                    return;
                }

                lock (_context.World.SceneLock)
                {
                    var ring = new ManaMeshSceneNode(_context, new TywMesh(_context,
                        b => b.HexRing(Vector3.Zero, TywGeometry.HexRadius - 0.1f, 0.2f, 0.0f, color), unlit: true))
                    {
                        Name = $"TYW Cursor Ring {seat}"
                    };
                    var dark = color * new Vector4(0.45f, 0.45f, 0.45f, 1.0f);
                    var pin = new ManaMeshSceneNode(_context, new TywMesh(_context, b =>
                    {
                        b.Box(new Vector3(0, 0.8f, 0), Vector3.UnitX, new Vector3(0.07f, 1.6f, 0.07f), dark);
                        b.Box(new Vector3(0, 1.75f, 0), Vector3.UnitX, new Vector3(0.42f, 0.42f, 0.42f), color);
                        b.Box(new Vector3(0, 0.03f, 0), Vector3.UnitX, new Vector3(0.36f, 0.04f, 0.36f), color);
                    }, unlit: true))
                    {
                        Name = $"TYW Cursor Pin {seat}"
                    };
                    Add(ring);
                    Add(pin);
                    cursor = (ring, pin);
                    _cursors[seat] = cursor;
                }
            }

            cursor.Ring.MeshRenderer.IsVisible = hex != null && point != null;
            cursor.Pin.MeshRenderer.IsVisible = point != null;
            if (hex is { } at)
            {
                cursor.Ring.LocalTransform = Matrix4x4.CreateTranslation(TywGeometry.Center(at) + new Vector3(0, 0.03f, 0));
            }

            if (point is { } p)
            {
                // A slow bob and spin, so a cursor left alone still catches the eye.
                var bob = 0.12f * MathF.Sin(now * 3.0f + seat);
                cursor.Pin.LocalTransform = Matrix4x4.CreateRotationY(now * 1.5f + seat) *
                                            Matrix4x4.CreateTranslation(p with { Y = TywGeometry.TopY + bob });
            }
        }
    }
}
