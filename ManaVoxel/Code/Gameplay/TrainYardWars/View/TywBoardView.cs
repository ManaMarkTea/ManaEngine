using Mana;
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
        private ManaMeshSceneNode? _preview;
        private string _previewKey = "";
        private float _clock;

        public TywBoardView(ManaContext context, ManaScene scene)
        {
            _context = context;
            _scene = scene;
        }

        private sealed class HexNodes
        {
            public int Revision = -1;
            public ManaMeshSceneNode? Track;
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
        }

        /// <summary>Loads every model the board uses, off the main thread during the loading screen.</summary>
        public void Preload()
        {
            var assets = new List<string>
            {
                Forest + "tree.glb", Forest + "tree-high.glb", Forest + "rocks-low.glb", Forest + "stones.glb",
                Forest + "rocks-high.glb", Forest + "plant.glb", Factory + "hopper-round.glb",
                Factory + "hopper-high-square.glb", Locomotive, EmptyWagon
            };
            assets.AddRange(CargoWagons);
            foreach (var asset in assets)
            {
                Model(asset);
            }
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
                _game = game;

                var sea = new ManaWaterNode(_context, new Vector2(400.0f, 400.0f), 96) { Name = "TYW Sea" };
                sea.Position = new Vector3(0.0f, 0.12f, 0.0f);
                sea.UpdateTransform();
                Add(sea);

                foreach (var tile in game.Board.Tiles)
                {
                    AddTile(tile);
                    _hexes[tile.Hex] = new HexNodes();
                }

                _hover = new ManaMeshSceneNode(_context, new TywMesh(_context,
                    b => b.HexRing(Vector3.Zero, TywGeometry.HexRadius - 0.08f, 0.14f, 0.0f, new Vector4(1.0f, 1.0f, 1.0f, 1.0f)), unlit: true))
                {
                    Name = "TYW Hover"
                };
                _hover.MeshRenderer.IsVisible = false;
                Add(_hover);
                _preview = null;
                _previewKey = "";
            }
        }

        private void Add(ManaSceneNode node)
        {
            _scene.Children.Add(node);
            _owned.Add(node);
        }

        // ---- The board, once ------------------------------------------------------------------------------

        private static Vector4 TerrainColor(TywTerrain terrain) => terrain switch
        {
            TywTerrain.Forest => new Vector4(0.27f, 0.52f, 0.23f, 1.0f),
            TywTerrain.Hills => new Vector4(0.66f, 0.58f, 0.43f, 1.0f),
            TywTerrain.Fields => new Vector4(0.80f, 0.74f, 0.36f, 1.0f),
            TywTerrain.Mountain => new Vector4(0.52f, 0.52f, 0.56f, 1.0f),
            TywTerrain.Lake => new Vector4(0.20f, 0.42f, 0.55f, 1.0f),
            TywTerrain.Town => new Vector4(0.74f, 0.71f, 0.64f, 1.0f),
            TywTerrain.Depot => new Vector4(0.55f, 0.55f, 0.55f, 1.0f),
            _ => new Vector4(0.52f, 0.70f, 0.34f, 1.0f)
        };

        private void AddTile(TywTile tile)
        {
            var random = new Random(tile.Hex.Q * 7919 + tile.Hex.R * 104729 + 17);
            var center = TywGeometry.Center(tile.Hex);
            var jitter = 0.94f + 0.12f * (float)random.NextDouble();
            var color = TerrainColor(tile.Terrain) * new Vector4(jitter, jitter, jitter, 1.0f);
            if (tile.Terrain == TywTerrain.Depot)
            {
                color = Vector4.Lerp(color, PlayerColors[tile.BuildingOwner], 0.55f);
            }

            var top = tile.Terrain == TywTerrain.Lake ? -0.35f : TywGeometry.TopY;
            var side = color * new Vector4(0.62f, 0.58f, 0.52f, 1.0f);
            var mesh = new TywMesh(_context, b =>
            {
                b.HexPrism(Vector3.Zero, TywGeometry.HexRadius - 0.04f, -1.2f, top, color, side);
                if (tile.Terrain == TywTerrain.Fields)
                {
                    // Crop rows in the back corners, clear of the track.
                    foreach (var corner in new[] { 4, 5, 0 })
                    {
                        var at = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.6f, corner, top + 0.06f);
                        for (var row = -1; row <= 1; row++)
                        {
                            b.Box(at + new Vector3(0.0f, 0.0f, row * 0.16f), Vector3.UnitX, new Vector3(0.62f, 0.1f, 0.08f),
                                new Vector4(0.93f, 0.78f, 0.25f, 1.0f));
                        }
                    }
                }

                if (tile.Richness > 0)
                {
                    var pip = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.7f, 3, top + 0.03f);
                    for (var i = 0; i < tile.Richness; i++)
                    {
                        b.Box(pip + new Vector3((i - (tile.Richness - 1) * 0.5f) * 0.2f, 0.0f, 0.0f), Vector3.UnitX,
                            new Vector3(0.13f, 0.06f, 0.13f), new Vector4(1.0f, 0.98f, 0.9f, 1.0f));
                    }
                }

                if (tile.Terrain == TywTerrain.Town)
                {
                    foreach (var corner in new[] { 4, 5, 0, 1 })
                    {
                        var at = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.62f, corner, top);
                        var height = 0.35f + 0.25f * (float)random.NextDouble();
                        b.Box(at + new Vector3(0, height * 0.5f, 0), Vector3.UnitX, new Vector3(0.5f, height, 0.45f), new Vector4(0.95f, 0.9f, 0.8f, 1.0f));
                        b.Box(at + new Vector3(0, height + 0.08f, 0), Vector3.UnitX, new Vector3(0.58f, 0.16f, 0.52f), new Vector4(0.75f, 0.25f, 0.2f, 1.0f));
                    }
                }

                if (tile.Terrain == TywTerrain.Depot)
                {
                    var owner = PlayerColors[tile.BuildingOwner];
                    var shed = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.58f, 5, top);
                    b.Box(shed + new Vector3(0, 0.3f, 0), Vector3.UnitX, new Vector3(1.0f, 0.6f, 0.55f), new Vector4(0.9f, 0.87f, 0.8f, 1.0f));
                    b.Box(shed + new Vector3(0, 0.66f, 0), Vector3.UnitX, new Vector3(1.1f, 0.12f, 0.65f), owner);
                    var pole = TywMeshBuilder.Corner(Vector3.Zero, TywGeometry.HexRadius * 0.6f, 1, top);
                    b.Box(pole + new Vector3(0, 0.6f, 0), Vector3.UnitX, new Vector3(0.05f, 1.2f, 0.05f), new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                    b.Box(pole + new Vector3(0.2f, 1.05f, 0), Vector3.UnitX, new Vector3(0.38f, 0.24f, 0.03f), owner);
                }
            });

            var node = new ManaMeshSceneNode(_context, mesh) { Name = $"TYW Hex {tile.Hex}" };
            node.Position = center with { Y = 0.0f };
            node.UpdateTransform();
            Add(node);

            switch (tile.Terrain)
            {
                case TywTerrain.Forest:
                    Scatter(tile, random, new[] { "tree.glb", "tree-high.glb" }, 1.0f, 1.5f, 3);
                    break;
                case TywTerrain.Hills:
                    Scatter(tile, random, new[] { "rocks-low.glb", "stones.glb" }, 0.35f, 0.55f, 3);
                    break;
                case TywTerrain.Mountain:
                    Scatter(tile, random, new[] { "rocks-high.glb" }, 1.1f, 1.6f, 3);
                    break;
                case TywTerrain.Plains:
                    if (random.NextDouble() < 0.5)
                    {
                        Scatter(tile, random, new[] { "plant.glb" }, 0.35f, 0.5f, 1);
                    }

                    break;
            }
        }

        // Scenery sits in the back corners, where track never runs.
        private void Scatter(TywTile tile, Random random, string[] names, float minHeight, float maxHeight, int count)
        {
            var corners = new List<int> { 4, 5, 0 };
            for (var i = 0; i < count && corners.Count > 0; i++)
            {
                var corner = corners[random.Next(corners.Count)];
                corners.Remove(corner);
                var at = TywMeshBuilder.Corner(TywGeometry.Center(tile.Hex), TywGeometry.HexRadius * (0.56f + 0.1f * (float)random.NextDouble()), corner, TywGeometry.TopY);
                var height = minHeight + (maxHeight - minHeight) * (float)random.NextDouble();
                var node = Place(Forest + names[random.Next(names.Length)], height, -1, footprint: 1.0f);
                if (node != null)
                {
                    node.LocalTransform = Matrix4x4.CreateRotationY((float)random.NextDouble() * MathF.Tau) * Matrix4x4.CreateTranslation(at);
                    Add(node);
                }
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

            var model = Matrix4x4.CreateTranslation(-middle.X, -prototype.BoundsMin.Y, -middle.Z) * Matrix4x4.CreateScale(scale) * turn;
            var node = prototype.Instantiate(_context, Path.GetFileNameWithoutExtension(asset), model);
            if (player >= 0)
            {
                foreach (var child in node.Children.OfType<ManaMeshSceneNode>())
                {
                    child.MeshRenderer.Material = Tinted(asset, child.MeshRenderer.Material, player);
                }
            }

            return node;
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
                        RebuildTrack(game, tile, nodes);
                        RebuildBuilding(tile, nodes);
                    }

                    SyncStock(tile, nodes);
                    SyncDemand(tile, nodes);
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

            node.LocalTransform = Matrix4x4.CreateTranslation(at);
            nodes.Building = node;
            Add(node);
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
            Pose(nodes.Locomotive, train.Path, train.Progress);
            if (nodes.Wagon != null)
            {
                Pose(nodes.Wagon, train.Path, train.Progress - WagonGap / step);
            }

            void Pose(ManaSceneNode node, List<TywHex> path, float s)
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
                var yaw = MathF.Atan2(-direction.Z, direction.X);
                node.LocalTransform = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(here + lift);
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

        /// <summary>The route new track would take, drawn as a bright line; rebuilt only when it changes.</summary>
        public void ShowPlan(IReadOnlyList<TywHex>? path, Vector4 color)
        {
            var key = path == null ? "" : string.Join(";", path) + color;
            if (key == _previewKey)
            {
                return;
            }

            _previewKey = key;
            lock (_context.World.SceneLock)
            {
                if (_preview != null)
                {
                    _scene.Children.Remove(_preview);
                    _owned.Remove(_preview);
                    _preview = null;
                }

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

                _preview = new ManaMeshSceneNode(_context, new TywMesh(_context, b =>
                {
                    for (var i = 0; i + 1 < points.Count; i++)
                    {
                        var along = points[i + 1] - points[i];
                        b.Box((points[i] + points[i + 1]) * 0.5f, along, new Vector3(along.Length() + 0.02f, 0.06f, 0.22f), color);
                    }
                }, unlit: true))
                {
                    Name = "TYW Plan"
                };
                Add(_preview);
            }
        }
    }
}
