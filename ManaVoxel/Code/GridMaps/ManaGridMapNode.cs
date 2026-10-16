using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.GridMaps
{
    /// <summary>A cell of a <see cref="ManaGridMapNode"/>. Y is the floor.</summary>
    public readonly record struct GridMapCell(int X, int Y, int Z);

    /// <summary>What a tile is: a palette index, and how many quarter turns about Y it is rotated.</summary>
    public readonly record struct GridMapTile(int Item, int Orientation);

    /// <summary>How many cells a palette item covers, before rotation.</summary>
    public readonly record struct GridMapFootprint(int Width, int Depth);

    /// <summary>Where a tile's model stands in the cells it covers.</summary>
    public enum GridMapAlign
    {
        /// <summary>
        /// As modelled: the model's origin at the centre of its cells. Grid kits (Kenney's all are) author every
        /// piece to one cell around its origin -- a straight wall already at the back, a corner already round
        /// the corner -- so this is what makes their pieces meet.
        /// </summary>
        Model,

        /// <summary>Centred by its bounds, for a model whose origin is somewhere else -- a prop pivoted at a corner.</summary>
        Centre,

        /// <summary>Its back against the cells' back edge -- walls, fences. Turning the brush walks it round the cell.</summary>
        Edge
    }

    /// <summary>
    /// How one palette item is laid into the grid. Each item has its own, because a kit mixes pieces that are
    /// nothing alike: a 1x1 floor, a 2x1 bridge, a wall that is a sliver of a cell deep.
    /// </summary>
    public sealed record GridMapItemSettings
    {
        public static GridMapItemSettings Default { get; } = new();

        /// <summary>Cells covered across, before the tile is turned.</summary>
        public int Width { get; init; } = 1;

        /// <summary>Cells covered deep, before the tile is turned.</summary>
        public int Depth { get; init; } = 1;

        /// <summary>Uniform scale; zero fits the model to its cells (see <see cref="ManaGridMapNode.ItemScale"/>).</summary>
        public float Scale { get; init; }

        /// <summary>Quarter turns applied to the model itself, for one modelled facing the other way.</summary>
        public int Turn { get; init; }

        public GridMapAlign Align { get; init; } = GridMapAlign.Model;

        /// <summary>
        /// A height, in the model's own units, below which its collider is flat: every collider point lower
        /// than this is raised to it. A kit floor is paved with bevelled stones and rubble a hand high, which
        /// look right but catch a walking character's feet on every edge; the drawn model keeps them.
        /// </summary>
        public float Smooth { get; init; }

        /// <summary>
        /// A shift of the model within its tile, in cells, before the tile is turned. A kit's walls are built to
        /// stand on the line between two floor tiles, half a cell from where a grid cell's centre puts them.
        /// </summary>
        public Vector3 Offset { get; init; }

        public bool Fits => Scale <= 0.0f;
    }

    /// <summary>One layer of a grid map, as saved and as undo keeps it.</summary>
    /// <param name="Generated">Anchors a generator laid, as opposed to ones painted; see ManaGridMapNode.IsGenerated.</param>
    public sealed record GridMapLayerState(
        string Name,
        bool Visible,
        IReadOnlyDictionary<GridMapCell, GridMapTile> Cells,
        IReadOnlySet<GridMapCell>? Generated = null);

    /// <summary>Everything a grid map is, for undo: settings, palette and every layer's cells, taken whole.</summary>
    public sealed record GridMapState(
        Vector3 CellSize,
        bool Collision,
        IReadOnlyList<string> Palette,
        IReadOnlyList<GridMapItemSettings> Settings,
        IReadOnlyList<GridMapLayerState> Layers,
        IReadOnlyList<GridMapAutoTileSet>? AutoTiles = null)
    {
        /// <summary>The first layer's cells -- all of them, on a map with one layer.</summary>
        public IReadOnlyDictionary<GridMapCell, GridMapTile> Cells => Layers.Count > 0 ? Layers[0].Cells : new Dictionary<GridMapCell, GridMapTile>();
    }

    /// <summary>
    /// A grid of mesh tiles painted from a palette -- Godot's GridMap. A tile is stored at its anchor cell, the
    /// corner of the footprint it covers, as a palette index and a rotation; the node builds each tile from
    /// that and keeps it in step.
    /// <para>
    /// Each palette asset is loaded once, into a prototype, and every tile using it shares that prototype's
    /// meshes and materials. Loading a GLB per tile would parse the same file hundreds of times and put a
    /// fresh copy of its meshes into the world for each one.
    /// </para>
    /// <para>
    /// <b>Sizing</b> is per palette item (<see cref="GridMapItemSettings"/>): how many cells it covers, and a
    /// scale that by default fits the model to them -- so a road kit on 10-unit cells draws ten times bigger
    /// rather than as 1x1 tiles with gaps between, and a thin wall does not shrink anything.
    /// </para>
    /// <para>
    /// <b>Layers</b> let tiles share a cell -- a floor on one, the walls standing on it on another. Each has its
    /// own cells; the cell API (<see cref="SetTile"/>, <see cref="Cells"/> and the rest) works on
    /// <see cref="ActiveLayer"/>. A hidden layer builds nothing, visual or collider, in the editor and in play.
    /// </para>
    /// <para>
    /// Tiles are children of this node but marked editor-transient, and carry no level source, so the
    /// outliner and level capture see one node: the cells are saved, and the tiles come back from them.
    /// Size a map with <see cref="CellSize"/>, not the node's scale: colliders take a tile's position and
    /// rotation only, so a scaled map draws bigger than it collides.
    /// </para>
    /// </summary>
    public sealed class ManaGridMapNode : ManaSceneNode
    {
        public const int OrientationCount = 4;

        public const string DefaultLayerName = "Base";

        private readonly List<string> _palette = new();
        private readonly List<GridMapItemSettings> _settings = new();
        private readonly List<GridMapLayer> _layers = new() { new GridMapLayer(DefaultLayerName, true) };
        private int _activeLayer;
        private readonly List<GridMapAutoTileSet> _autoTiles = new();
        private readonly Dictionary<string, GridMapPrototype> _prototypes = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (Matrix4x4 Model, float Smooth, Vector3[] Triangles)> _colliderTriangles = new();
        private Vector3 _cellSize = Vector3.One;
        private bool _collision = true;

        public ManaGridMapNode(ManaContext context) : base(context, Matrix4x4.Identity)
        {
            Name = "Grid Map";
            LevelSource = ManaLevelSource.GridMap;
        }

        /// <summary>Loads a palette asset. Swapped out by tests, which have no asset packs to read.</summary>
        public static Func<string, ManaWorld, ManaScene, ManaSceneNode> LoadAsset { get; set; } =
            (asset, world, into) => ManaLevelLoader.LoadMeshAsset(asset, world, into);

        /// <summary>Bumped on every change, so a view can poll one number instead of subscribing.</summary>
        public int Revision { get; private set; }

        public event Action? Changed;

        /// <summary>Replaced whenever the palette changes, and only then -- a view rebuilds its palette on a new one.</summary>
        public object PaletteToken { get; private set; } = new();

        /// <summary>Replaced when a layer is added, removed or renamed -- a view rebuilds its layer list on a new one.</summary>
        public object LayersToken { get; private set; } = new();

        /// <summary>Replaced when an auto-tile set is added, removed or edited.</summary>
        public object AutoTilesToken { get; private set; } = new();

        public Vector3 CellSize
        {
            get => _cellSize;
            set
            {
                var clamped = Vector3.Max(value, new Vector3(0.05f));
                if (clamped == _cellSize)
                {
                    return;
                }

                _cellSize = clamped;
                RebuildTiles();
            }
        }

        /// <summary>Whether tiles get triangle-mesh colliders. Off for decoration nobody walks on.</summary>
        public bool Collision
        {
            get => _collision;
            set
            {
                if (value == _collision)
                {
                    return;
                }

                _collision = value;
                RebuildTiles();
            }
        }

        public IReadOnlyList<string> Palette => _palette;

        private GridMapLayer Active => _layers[_activeLayer];

        /// <summary>The layer the cell API reads and paints: 0 until something picks another.</summary>
        public int ActiveLayer
        {
            get => _activeLayer;
            set
            {
                var clamped = Math.Clamp(value, 0, _layers.Count - 1);
                if (clamped != _activeLayer)
                {
                    _activeLayer = clamped;
                    NotifyChanged();
                }
            }
        }

        public int LayerCount => _layers.Count;

        public string LayerName(int layer) => (uint)layer < (uint)_layers.Count ? _layers[layer].Name : string.Empty;

        public bool IsLayerVisible(int layer) => (uint)layer < (uint)_layers.Count && _layers[layer].Visible;

        /// <summary>Tiles on a layer, by anchor cell.</summary>
        public IReadOnlyDictionary<GridMapCell, GridMapTile> LayerCells(int layer) =>
            (uint)layer < (uint)_layers.Count ? _layers[layer].Cells : new Dictionary<GridMapCell, GridMapTile>();

        /// <summary>Anchors a generator laid on a layer; see <see cref="IsGenerated"/>.</summary>
        public IReadOnlySet<GridMapCell> LayerGenerated(int layer) =>
            (uint)layer < (uint)_layers.Count ? _layers[layer].Generated : new HashSet<GridMapCell>();

        /// <summary>Every tile on every layer.</summary>
        public int TileCount => _layers.Sum(layer => layer.Cells.Count);

        /// <summary>Adds an empty layer on top and makes it the active one.</summary>
        public int AddLayer(string? name = null)
        {
            var unique = string.IsNullOrWhiteSpace(name) ? $"Layer {_layers.Count + 1}" : name.Trim();
            _layers.Add(new GridMapLayer(unique, true));
            _activeLayer = _layers.Count - 1;
            LayersToken = new object();
            NotifyChanged();
            return _activeLayer;
        }

        /// <summary>Removes a layer and its tiles. The last layer stays: a map always has one to paint on.</summary>
        public void RemoveLayer(int layer)
        {
            if (_layers.Count <= 1 || (uint)layer >= (uint)_layers.Count)
            {
                return;
            }

            ClearLayerNodes(_layers[layer]);
            _layers.RemoveAt(layer);
            LayersToken = new object();
            _activeLayer = Math.Clamp(_activeLayer >= layer ? _activeLayer - 1 : _activeLayer, 0, _layers.Count - 1);
            NotifyChanged();
        }

        public void RenameLayer(int layer, string name)
        {
            if ((uint)layer < (uint)_layers.Count && !string.IsNullOrWhiteSpace(name) && _layers[layer].Name != name.Trim())
            {
                _layers[layer].Name = name.Trim();
                LayersToken = new object();
                NotifyChanged();
            }
        }

        public void SetLayerVisible(int layer, bool visible)
        {
            if ((uint)layer >= (uint)_layers.Count || _layers[layer].Visible == visible)
            {
                return;
            }

            var target = _layers[layer];
            target.Visible = visible;
            ClearLayerNodes(target);
            BuildLayerNodes(target);
            NotifyChanged();
        }

        /// <summary>Tiles by anchor cell. A tile covering several cells appears once, at its anchor.</summary>
        public IReadOnlyDictionary<GridMapCell, GridMapTile> Cells => Active.Cells;

        /// <summary>Adds an asset to the palette, or finds it if it is already there.</summary>
        public int AddPaletteItem(string asset)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(asset);

            var existing = _palette.FindIndex(item => string.Equals(item, asset, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                return existing;
            }

            _palette.Add(asset);
            _settings.Add(GridMapItemSettings.Default);
            PaletteToken = new object();
            NotifyChanged();
            return _palette.Count - 1;
        }

        /// <summary>Removes a palette item and every tile painted with it; later items move down one.</summary>
        public void RemovePaletteItem(int index)
        {
            if ((uint)index >= (uint)_palette.Count)
            {
                return;
            }

            _palette.RemoveAt(index);
            _settings.RemoveAt(index);
            for (var i = 0; i < _autoTiles.Count; i++)
            {
                var pieces = _autoTiles[i].Pieces
                    .Where(piece => piece.Item != index)
                    .Select(piece => piece.Item > index ? piece with { Item = piece.Item - 1 } : piece)
                    .ToList();
                _autoTiles[i] = _autoTiles[i] with { Pieces = pieces };
            }

            AutoTilesToken = new object();
            foreach (var layer in _layers)
            {
                foreach (var (cell, tile) in layer.Cells.ToList())
                {
                    if (tile.Item == index)
                    {
                        layer.Cells.Remove(cell);
                    }
                    else if (tile.Item > index)
                    {
                        layer.Cells[cell] = tile with { Item = tile.Item - 1 };
                    }
                }
            }

            PaletteToken = new object();
            RebuildTiles();
        }

        /// <summary>Why a palette item shows nothing, or null when it loaded.</summary>
        public string? PaletteError(int index)
        {
            return (uint)index < (uint)_palette.Count
                && _prototypes.TryGetValue(_palette[index], out var prototype)
                ? prototype.Error
                : null;
        }

        /// <summary>The size of a palette item's model, in its own units. Null until it loads.</summary>
        public Vector3? PaletteItemSize(int index)
        {
            if ((uint)index >= (uint)_palette.Count)
            {
                return null;
            }

            var prototype = GetPrototype(_palette[index]);
            return prototype.HasGeometry ? prototype.BoundsMax - prototype.BoundsMin : null;
        }

        /// <summary>
        /// The footprint Fit sizes a model by. As modelled, it is the square the model fills around its origin --
        /// a wall at the back of its cell still measures the whole cell, as its author meant; otherwise its bounds.
        /// </summary>
        private Vector3? FitSize(int item)
        {
            if ((uint)item >= (uint)_palette.Count)
            {
                return null;
            }

            var prototype = GetPrototype(_palette[item]);
            if (!prototype.HasGeometry)
            {
                return null;
            }

            if (ItemSettings(item).Align != GridMapAlign.Model)
            {
                return prototype.BoundsMax - prototype.BoundsMin;
            }

            var reach = Vector3.Max(Vector3.Abs(prototype.BoundsMin), Vector3.Abs(prototype.BoundsMax));
            return new Vector3(reach.X * 2.0f, prototype.BoundsMax.Y - prototype.BoundsMin.Y, reach.Z * 2.0f);
        }

        /// <summary>A palette item's name, as the asset library would show it.</summary>
        public string PaletteItemName(int index) =>
            (uint)index < (uint)_palette.Count ? TileName(_palette[index]) : string.Empty;

        public GridMapItemSettings ItemSettings(int item) =>
            (uint)item < (uint)_settings.Count ? _settings[item] : GridMapItemSettings.Default;

        public void SetItemSettings(int item, GridMapItemSettings settings)
        {
            if ((uint)item >= (uint)_settings.Count)
            {
                return;
            }

            settings = settings with
            {
                Width = Math.Clamp(settings.Width, 1, 32),
                Depth = Math.Clamp(settings.Depth, 1, 32),
                Scale = float.IsFinite(settings.Scale) ? MathF.Max(0.0f, settings.Scale) : 0.0f,
                Smooth = float.IsFinite(settings.Smooth) ? MathF.Max(0.0f, settings.Smooth) : 0.0f,
                Offset = float.IsFinite(settings.Offset.X + settings.Offset.Y + settings.Offset.Z) ? settings.Offset : Vector3.Zero,
                Turn = WrapOrientation(settings.Turn)
            };
            if (_settings[item] == settings)
            {
                return;
            }

            _settings[item] = settings;
            RebuildTiles();
        }

        public GridMapFootprint ItemFootprint(int item)
        {
            var settings = ItemSettings(item);
            return new GridMapFootprint(settings.Width, settings.Depth);
        }

        /// <summary>
        /// The scale an item's model is drawn at: its own, or, when it fits, the largest uniform scale that keeps
        /// it inside its cells -- a 1x1 road tile on 10-unit cells draws ten times bigger, while a wall a sliver
        /// deep is sized by its length and not blown up by its thickness.
        /// </summary>
        public float ItemScale(int item)
        {
            var settings = ItemSettings(item);
            if (!settings.Fits)
            {
                return settings.Scale;
            }

            if (FitSize(item) is not { } size)
            {
                return 1.0f;
            }

            var (across, deep) = settings.Turn % 2 == 0 ? (size.X, size.Z) : (size.Z, size.X);
            var fit = float.MaxValue;
            if (across > 0.001f)
            {
                fit = MathF.Min(fit, settings.Width * _cellSize.X / across);
            }

            if (deep > 0.001f)
            {
                fit = MathF.Min(fit, settings.Depth * _cellSize.Z / deep);
            }

            return fit == float.MaxValue ? 1.0f : fit;
        }

        /// <summary>The footprint a tile actually covers: its item's, swapped on a quarter turn.</summary>
        public GridMapFootprint Footprint(GridMapTile tile)
        {
            var footprint = ItemFootprint(tile.Item);
            return tile.Orientation % 2 == 0 ? footprint : new GridMapFootprint(footprint.Depth, footprint.Width);
        }

        /// <summary>Every cell a tile anchored at <paramref name="anchor"/> covers.</summary>
        public IEnumerable<GridMapCell> CoveredCells(GridMapCell anchor, GridMapTile tile)
        {
            var footprint = Footprint(tile);
            for (var x = 0; x < footprint.Width; x++)
            {
                for (var z = 0; z < footprint.Depth; z++)
                {
                    yield return new GridMapCell(anchor.X + x, anchor.Y, anchor.Z + z);
                }
            }
        }

        public bool TryGetTile(GridMapCell anchor, out GridMapTile tile) => Active.Cells.TryGetValue(anchor, out tile);

        /// <summary>The tile covering a cell, wherever its anchor is.</summary>
        public bool TryGetTileCovering(GridMapCell cell, out GridMapCell anchor, out GridMapTile tile)
        {
            anchor = Active.CoveredBy.TryGetValue(cell, out var found) ? found : cell;
            return Active.Cells.TryGetValue(anchor, out tile);
        }

        /// <summary>The node built for the tile covering a cell, or null when it is empty or did not load.</summary>
        public ManaSceneNode? TileNode(GridMapCell cell) =>
            TryGetTileCovering(cell, out var anchor, out _) ? Active.Tiles.GetValueOrDefault(anchor) : null;

        /// <summary>Puts a tile at an anchor, replacing whatever it lands on -- including bigger tiles it only partly covers.</summary>
        public void SetTile(GridMapCell anchor, GridMapTile tile)
        {
            if ((uint)tile.Item >= (uint)_palette.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(tile), $"Palette item {tile.Item} does not exist.");
            }

            tile = tile with { Orientation = WrapOrientation(tile.Orientation) };
            var turnOnly = Active.Cells.TryGetValue(anchor, out var current) && current.Item == tile.Item;
            if (!PutTile(anchor, tile))
            {
                return;
            }

            // Turning a piece leaves which cells are in which set as they were, so its neighbours stay put.
            if (!turnOnly)
            {
                RefreshAutoTileNeighbours(CoveredCells(anchor, tile));
            }

            NotifyChanged();
        }

        /// <summary>
        /// Whether the tile anchored at a cell was laid by a generator rather than painted. A generator run again
        /// replaces its own tiles and keeps painted ones, so painting a tile claims it as painted.
        /// </summary>
        public bool IsGenerated(GridMapCell anchor) => Active.Generated.Contains(anchor);

        /// <summary>Puts a tile down as a generator's: see <see cref="IsGenerated"/>.</summary>
        public void SetGeneratedTile(GridMapCell anchor, GridMapTile tile)
        {
            SetTile(anchor, tile);
            if (Active.Cells.ContainsKey(anchor))
            {
                Active.Generated.Add(anchor);
            }
        }

        /// <summary>Puts a tile down with nothing else following: no neighbours re-picked, no change notice.</summary>
        private bool PutTile(GridMapCell anchor, GridMapTile tile)
        {
            if (Active.Cells.TryGetValue(anchor, out var current) && current == tile)
            {
                Active.Generated.Remove(anchor);
                return false;
            }

            foreach (var cell in CoveredCells(anchor, tile).ToList())
            {
                if (TryGetTileCovering(cell, out var displaced, out _))
                {
                    RemoveAnchor(displaced);
                }
            }

            Active.Cells[anchor] = tile;
            Active.Generated.Remove(anchor);
            PlaceAnchor(anchor, tile);
            return true;
        }

        /// <summary>Clears the tile covering a cell.</summary>
        public void ClearTile(GridMapCell cell)
        {
            if (!TryGetTileCovering(cell, out var anchor, out var tile))
            {
                return;
            }

            var covered = CoveredCells(anchor, tile).ToList();
            RemoveAnchor(anchor);
            RefreshAutoTileNeighbours(covered);
            NotifyChanged();
        }

        public IReadOnlyList<GridMapAutoTileSet> AutoTiles => _autoTiles;

        public int AddAutoTileSet(GridMapAutoTileSet set)
        {
            _autoTiles.Add(Sanitise(set));
            AutoTilesToken = new object();
            NotifyChanged();
            return _autoTiles.Count - 1;
        }

        /// <summary>Edits a set. Tiles already painted keep their pieces until they are painted or re-picked again.</summary>
        public void SetAutoTileSet(int index, GridMapAutoTileSet set)
        {
            if ((uint)index >= (uint)_autoTiles.Count || _autoTiles[index].SameAs(set))
            {
                return;
            }

            _autoTiles[index] = Sanitise(set);
            AutoTilesToken = new object();
            NotifyChanged();
        }

        /// <summary>Removes a set. Its tiles stay as painted: they are ordinary palette tiles.</summary>
        public void RemoveAutoTileSet(int index)
        {
            if ((uint)index >= (uint)_autoTiles.Count)
            {
                return;
            }

            _autoTiles.RemoveAt(index);
            AutoTilesToken = new object();
            NotifyChanged();
        }

        /// <summary>The first set a palette item is a piece of, or -1.</summary>
        public int AutoTileSetOf(int item) => _autoTiles.FindIndex(set => set.Contains(item));

        /// <summary>Which of a cell's sides have a tile from the set next to them, on the active layer.</summary>
        public GridMapSides AutoTileNeighbours(int set, GridMapCell cell)
        {
            var sides = GridMapSides.None;
            if ((uint)set >= (uint)_autoTiles.Count)
            {
                return sides;
            }

            foreach (var (side, x, z) in GridMapAutoTile.Neighbours)
            {
                if (TryGetTileCovering(new GridMapCell(cell.X + x, cell.Y, cell.Z + z), out _, out var neighbour)
                    && _autoTiles[set].Contains(neighbour.Item))
                {
                    sides |= side;
                }
            }

            return sides;
        }

        /// <summary>The piece painting the set into a cell would put there, for the brush preview.</summary>
        public GridMapTile? ResolveAutoTile(int set, GridMapCell cell) =>
            (uint)set < (uint)_autoTiles.Count && GridMapAutoTile.TryResolve(_autoTiles[set], AutoTileNeighbours(set, cell), out var tile)
                ? tile
                : null;

        /// <summary>
        /// Paints the set into a cell: the piece that joins it to its neighbours from the set, and the neighbours
        /// re-picked to join it back. False when the set has no pieces.
        /// </summary>
        public bool PaintAutoTile(int set, GridMapCell cell)
        {
            if (ResolveAutoTile(set, cell) is not { } tile)
            {
                return false;
            }

            if (TryGetTile(cell, out var current) && current == tile)
            {
                return true;
            }

            PutTile(cell, tile);
            RefreshAutoTileNeighbours(CoveredCells(cell, tile));
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Re-picks every auto-tile next to the cells given, after those cells changed. One ring only: a
        /// neighbour re-picked stays in its set, so the cells beyond it see no change.
        /// </summary>
        private void RefreshAutoTileNeighbours(IEnumerable<GridMapCell> changed)
        {
            if (_autoTiles.Count == 0)
            {
                return;
            }

            var cells = changed.ToHashSet();
            var anchors = new HashSet<GridMapCell>();
            foreach (var cell in cells)
            {
                foreach (var (_, x, z) in GridMapAutoTile.Neighbours)
                {
                    var next = new GridMapCell(cell.X + x, cell.Y, cell.Z + z);
                    if (!cells.Contains(next) && TryGetTileCovering(next, out var anchor, out _))
                    {
                        anchors.Add(anchor);
                    }
                }
            }

            foreach (var anchor in anchors)
            {
                if (!TryGetTile(anchor, out var tile))
                {
                    continue;
                }

                var set = AutoTileSetOf(tile.Item);
                if (set >= 0 && ResolveAutoTile(set, anchor) is { } picked && picked != tile)
                {
                    PutTile(anchor, picked);
                }
            }
        }

        /// <summary>Drops pieces whose items are not in the palette.</summary>
        private GridMapAutoTileSet Sanitise(GridMapAutoTileSet set) => set with
        {
            Name = string.IsNullOrWhiteSpace(set.Name) ? $"Set {_autoTiles.Count + 1}" : set.Name.Trim(),
            Pieces = set.Pieces
                .Where(piece => (uint)piece.Item < (uint)_palette.Count)
                .Select(piece => piece with { Sides = piece.Sides & GridMapSides.All })
                .ToList()
        };

        /// <summary>Clears the active layer.</summary>
        public void ClearAll()
        {
            ClearLayerNodes(Active);
            Active.Cells.Clear();
            NotifyChanged();
        }

        /// <summary>Replaces the whole grid at once with one layer -- a level load, or a test -- rebuilding tiles once.</summary>
        public void Load(
            Vector3 cellSize,
            IEnumerable<string> palette,
            IEnumerable<KeyValuePair<GridMapCell, GridMapTile>> cells,
            bool collision,
            IEnumerable<GridMapItemSettings>? settings = null)
        {
            Load(cellSize, palette, new[] { new GridMapLayerState(DefaultLayerName, true, cells.ToDictionary(pair => pair.Key, pair => pair.Value)) }, collision, settings);
        }

        /// <summary>Replaces the whole grid at once, every layer, rebuilding tiles once.</summary>
        public void Load(
            Vector3 cellSize,
            IEnumerable<string> palette,
            IReadOnlyList<GridMapLayerState> layers,
            bool collision,
            IEnumerable<GridMapItemSettings>? settings = null,
            IEnumerable<GridMapAutoTileSet>? autoTiles = null)
        {
            foreach (var layer in _layers)
            {
                ClearLayerNodes(layer);
            }

            _cellSize = Vector3.Max(cellSize, new Vector3(0.05f));
            _collision = collision;
            _palette.Clear();
            _palette.AddRange(palette);
            _settings.Clear();
            _settings.AddRange((settings ?? []).Take(_palette.Count));
            while (_settings.Count < _palette.Count)
            {
                _settings.Add(GridMapItemSettings.Default);
            }

            _layers.Clear();
            foreach (var saved in layers)
            {
                var layer = new GridMapLayer(string.IsNullOrWhiteSpace(saved.Name) ? $"Layer {_layers.Count + 1}" : saved.Name, saved.Visible);
                foreach (var (cell, tile) in saved.Cells)
                {
                    if ((uint)tile.Item < (uint)_palette.Count)
                    {
                        layer.Cells[cell] = tile with { Orientation = WrapOrientation(tile.Orientation) };
                    }
                }

                layer.Generated.UnionWith((saved.Generated ?? new HashSet<GridMapCell>()).Where(layer.Cells.ContainsKey));

                _layers.Add(layer);
            }

            if (_layers.Count == 0)
            {
                _layers.Add(new GridMapLayer(DefaultLayerName, true));
            }

            _autoTiles.Clear();
            foreach (var set in autoTiles ?? [])
            {
                _autoTiles.Add(Sanitise(set));
            }

            _activeLayer = Math.Clamp(_activeLayer, 0, _layers.Count - 1);
            AutoTilesToken = new object();
            PaletteToken = new object();
            LayersToken = new object();
            RebuildTiles();
        }

        public GridMapState CaptureState() => new(
            _cellSize,
            _collision,
            _palette.ToList(),
            _settings.ToList(),
            _layers.Select(layer => new GridMapLayerState(
                layer.Name, layer.Visible, new Dictionary<GridMapCell, GridMapTile>(layer.Cells), new HashSet<GridMapCell>(layer.Generated))).ToList(),
            _autoTiles.ToList());

        /// <summary>
        /// Puts a captured state back. When only cells differ -- a paint stroke -- only the tiles that changed are
        /// rebuilt, so undoing a stroke on a big map does not rebuild the whole map.
        /// </summary>
        public void RestoreState(GridMapState state)
        {
            var sameSettings = state.CellSize == _cellSize
                && state.Collision == _collision
                && state.Palette.SequenceEqual(_palette)
                && state.Settings.SequenceEqual(_settings)
                && state.Layers.Count == _layers.Count
                && state.Layers.Select((layer, i) => layer.Name == _layers[i].Name && layer.Visible == _layers[i].Visible).All(same => same);
            if (!sameSettings)
            {
                var active = _activeLayer;
                Load(state.CellSize, state.Palette, state.Layers, state.Collision, state.Settings, state.AutoTiles);
                ActiveLayer = active;
                return;
            }

            // Sets change no tile by themselves, so putting them back needs no rebuild.
            var sets = state.AutoTiles ?? [];
            if (sets.Count != _autoTiles.Count || !sets.Zip(_autoTiles).All(pair => pair.First.SameAs(pair.Second)))
            {
                _autoTiles.Clear();
                _autoTiles.AddRange(sets);
                AutoTilesToken = new object();
            }

            for (var i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                var wanted = state.Layers[i].Cells;
                foreach (var (anchor, tile) in layer.Cells.ToList())
                {
                    if (!wanted.TryGetValue(anchor, out var keep) || keep != tile)
                    {
                        RemoveAnchor(layer, anchor);
                    }
                }

                foreach (var (anchor, tile) in wanted)
                {
                    if (!layer.Cells.ContainsKey(anchor))
                    {
                        layer.Cells[anchor] = tile;
                        PlaceAnchor(layer, anchor, tile);
                    }
                }

                layer.Generated.Clear();
                layer.Generated.UnionWith(state.Layers[i].Generated ?? new HashSet<GridMapCell>());
            }

            NotifyChanged();
        }

        /// <summary>
        /// A palette asset's model name without its extension -- "road-bend". Not MeshAssetLibrary.DisplayName,
        /// which the play-only web build does not compile.
        /// </summary>
        public static string TileName(string asset)
        {
            asset = AssetOf(asset);
            var path = ZipAssetReferences.TryDecodeZip(asset, out _, out var entry) ? entry : asset;
            return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        }

        /// <summary>
        /// The model a palette entry draws. An entry may end in a "#" variant -- "candle.glb#2" -- so one model
        /// can be in the palette twice with different settings: a candle at the front of a table and one at the
        /// back. Everything that loads the model reads it through this.
        /// </summary>
        public static string AssetOf(string entry)
        {
            var hash = entry.LastIndexOf('#');
            return hash > 0 && hash > entry.LastIndexOfAny(['/', '\\']) ? entry[..hash] : entry;
        }

        /// <summary>The "#" variant a palette entry carries, with its "#", or empty for none; see <see cref="AssetOf"/>.</summary>
        public static string VariantOf(string entry) => entry[AssetOf(entry).Length..];

        public static int WrapOrientation(int orientation) =>
            ((orientation % OrientationCount) + OrientationCount) % OrientationCount;

        /// <summary>The centre of a cell's floor, in this node's space.</summary>
        public Vector3 CellOrigin(GridMapCell cell) =>
            new((cell.X + 0.5f) * _cellSize.X, cell.Y * _cellSize.Y, (cell.Z + 0.5f) * _cellSize.Z);

        /// <summary>The cell on <paramref name="floor"/> that a point in this node's space falls in.</summary>
        public GridMapCell CellAt(Vector3 localPoint, int floor) =>
            new((int)MathF.Floor(localPoint.X / _cellSize.X), floor, (int)MathF.Floor(localPoint.Z / _cellSize.Z));

        /// <summary>Where a tile goes: turned, and standing at the centre of the floor of the cells it covers.</summary>
        public Matrix4x4 TileTransform(GridMapCell anchor, GridMapTile tile)
        {
            var footprint = Footprint(tile);
            var centre = new Vector3(
                (anchor.X + footprint.Width * 0.5f) * _cellSize.X,
                anchor.Y * _cellSize.Y,
                (anchor.Z + footprint.Depth * 0.5f) * _cellSize.Z);
            return Matrix4x4.CreateRotationY(WrapOrientation(tile.Orientation) * MathF.PI * 0.5f)
                * Matrix4x4.CreateTranslation(centre);
        }

        /// <summary>
        /// How a palette item's model sits inside its tile: its origin at the cells' centre (or, for Centre and
        /// Edge, its bounds), its own floor kept, turned by the item's own turn, scaled, and for an edge piece
        /// pushed back until it stands against the cells' back edge.
        /// </summary>
        public Matrix4x4 ModelTransform(int item)
        {
            var prototype = (uint)item < (uint)_palette.Count ? GetPrototype(_palette[item]) : null;
            if (prototype is not { HasGeometry: true })
            {
                return Matrix4x4.Identity;
            }

            var settings = ItemSettings(item);
            var centre = settings.Align == GridMapAlign.Model ? Vector3.Zero : (prototype.BoundsMin + prototype.BoundsMax) * 0.5f;
            var size = prototype.BoundsMax - prototype.BoundsMin;
            var scale = ItemScale(item);
            var model = Matrix4x4.CreateTranslation(-centre.X, 0.0f, -centre.Z)
                * Matrix4x4.CreateRotationY(settings.Turn * MathF.PI * 0.5f)
                * Matrix4x4.CreateScale(scale);

            if (settings.Align == GridMapAlign.Edge)
            {
                var deep = (settings.Turn % 2 == 0 ? size.Z : size.X) * scale;
                model *= Matrix4x4.CreateTranslation(0.0f, 0.0f, (deep - settings.Depth * _cellSize.Z) * 0.5f);
            }

            if (settings.Offset != Vector3.Zero)
            {
                model *= Matrix4x4.CreateTranslation(settings.Offset * _cellSize);
            }

            return model;
        }

        /// <summary>
        /// A free-standing copy of a palette item's model, for an editor brush preview. Not a child of this
        /// node and not registered anywhere: the caller places it with <see cref="TileTransform"/> and removes it.
        /// </summary>
        public ManaSceneNode? CreatePreview(int item)
        {
            if ((uint)item >= (uint)_palette.Count)
            {
                return null;
            }

            var prototype = GetPrototype(_palette[item]);
            return prototype.Error != null ? null : prototype.Instantiate(Context, "Grid Map Brush", ModelTransform(item));
        }

        private void RebuildTiles()
        {
            _colliderTriangles.Clear();
            foreach (var layer in _layers)
            {
                ClearLayerNodes(layer);
                BuildLayerNodes(layer);
            }

            NotifyChanged();
        }

        private void ClearLayerNodes(GridMapLayer layer)
        {
            foreach (var anchor in layer.Tiles.Keys.ToList())
            {
                RemoveTileNode(layer, anchor);
            }

            layer.CoveredBy.Clear();
        }

        private void BuildLayerNodes(GridMapLayer layer)
        {
            foreach (var (anchor, tile) in layer.Cells)
            {
                PlaceAnchor(layer, anchor, tile);
            }
        }

        private void PlaceAnchor(GridMapCell anchor, GridMapTile tile) => PlaceAnchor(Active, anchor, tile);

        private void PlaceAnchor(GridMapLayer layer, GridMapCell anchor, GridMapTile tile)
        {
            foreach (var cell in CoveredCells(anchor, tile))
            {
                layer.CoveredBy[cell] = anchor;
            }

            if (layer.Visible)
            {
                BuildTileNode(layer, anchor, tile);
            }
        }

        private void RemoveAnchor(GridMapCell anchor) => RemoveAnchor(Active, anchor);

        private void RemoveAnchor(GridMapLayer layer, GridMapCell anchor)
        {
            if (!layer.Cells.Remove(anchor, out var tile))
            {
                return;
            }

            layer.Generated.Remove(anchor);

            foreach (var cell in CoveredCells(anchor, tile))
            {
                if (layer.CoveredBy.TryGetValue(cell, out var owner) && owner == anchor)
                {
                    layer.CoveredBy.Remove(cell);
                }
            }

            RemoveTileNode(layer, anchor);
        }

        private void BuildTileNode(GridMapLayer layer, GridMapCell anchor, GridMapTile tile)
        {
            var prototype = GetPrototype(_palette[tile.Item]);
            if (!prototype.HasGeometry)
            {
                return;
            }

            var model = ModelTransform(tile.Item);
            var node = prototype.Instantiate(Context, $"{prototype.Name} ({anchor.X}, {anchor.Y}, {anchor.Z})", model);
            node.SetLocalTransform(TileTransform(anchor, tile));
            lock (Context.World.SceneLock)
            {
                AddChild(node);
            }

            // The collider takes its pose from the tile's world transform, so that has to be current first.
            node.UpdateTransform();
            if (_collision && prototype.Triangles.Length >= 3 && Context.Physics != null)
            {
                Context.Physics.AddTriangleMeshBody(node, ColliderTriangles(tile.Item, prototype, model, ItemSettings(tile.Item).Smooth));
            }

            layer.Tiles[anchor] = node;
        }

        /// <summary>
        /// A palette item's triangles placed and scaled as its tiles are, shared by every tile of it -- flattened
        /// below the item's <see cref="GridMapItemSettings.Smooth"/> height, where a triangle wholly below it
        /// goes flat and one flattened to nothing is dropped.
        /// </summary>
        private Vector3[] ColliderTriangles(int item, GridMapPrototype prototype, Matrix4x4 model, float smooth)
        {
            if (_colliderTriangles.TryGetValue(item, out var cached) && cached.Model == model && cached.Smooth == smooth)
            {
                return cached.Triangles;
            }

            var triangles = new List<Vector3>(prototype.Triangles.Length);
            for (var i = 0; i + 2 < prototype.Triangles.Length; i += 3)
            {
                var a = Flatten(prototype.Triangles[i]);
                var b = Flatten(prototype.Triangles[i + 1]);
                var c = Flatten(prototype.Triangles[i + 2]);
                if (smooth > 0.0f && Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f)
                {
                    continue;
                }

                triangles.Add(Vector3.Transform(a, model));
                triangles.Add(Vector3.Transform(b, model));
                triangles.Add(Vector3.Transform(c, model));
            }

            var result = triangles.ToArray();
            _colliderTriangles[item] = (model, smooth, result);
            return result;

            Vector3 Flatten(Vector3 point) => point.Y < smooth ? point with { Y = smooth } : point;
        }

        private void RemoveTileNode(GridMapLayer layer, GridMapCell anchor)
        {
            if (!layer.Tiles.Remove(anchor, out var node))
            {
                return;
            }

            if (Context.Physics != null)
            {
                foreach (var collision in node.GetComponents<ManaCollisionComponent>())
                {
                    Context.Physics.RemoveBody(collision.Body);
                }
            }

            lock (Context.World.SceneLock)
            {
                RemoveChild(node);
            }
        }

        private GridMapPrototype GetPrototype(string asset)
        {
            if (!_prototypes.TryGetValue(asset, out var prototype))
            {
                prototype = GridMapPrototype.Load(asset, Context);
                _prototypes[asset] = prototype;
            }

            return prototype;
        }

        private void NotifyChanged()
        {
            Revision++;
            Changed?.Invoke();
        }
    }

    /// <summary>One layer's cells, which cells its big tiles cover, and the nodes built for them.</summary>
    internal sealed class GridMapLayer(string name, bool visible)
    {
        public string Name { get; set; } = name;

        public bool Visible { get; set; } = visible;

        public Dictionary<GridMapCell, GridMapTile> Cells { get; } = new();

        public Dictionary<GridMapCell, GridMapCell> CoveredBy { get; } = new();

        /// <summary>Anchors a generator laid; see ManaGridMapNode.IsGenerated.</summary>
        public HashSet<GridMapCell> Generated { get; } = new();

        public Dictionary<GridMapCell, ManaSceneNode> Tiles { get; } = new();
    }

    /// <summary>One palette asset, loaded once and flattened to the meshes a tile is made of.</summary>
    internal sealed class GridMapPrototype
    {
        public string Name { get; private init; } = string.Empty;

        public List<(string Name, ManaMesh Mesh, ManaMaterial Material, Matrix4x4 Transform)> Parts { get; } = new();

        /// <summary>Three vertices per triangle, in the model's own space, for its colliders.</summary>
        public Vector3[] Triangles { get; private set; } = [];

        public Vector3 BoundsMin { get; private set; }

        public Vector3 BoundsMax { get; private set; }

        public string? Error { get; private init; }

        public bool HasGeometry => Error == null && Parts.Count > 0;

        public static GridMapPrototype Load(string asset, ManaContext context)
        {
            var name = ManaGridMapNode.TileName(asset);
            ManaSceneNode root;
            try
            {
                // A holding scene the world never sees: the model is read for its meshes, not placed.
                root = ManaGridMapNode.LoadAsset(ManaGridMapNode.AssetOf(asset), context.World, new ManaScene(context) { Name = "Grid Map Palette" });
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"Grid map palette item '{asset}' could not be loaded: {exception.Message}");
                return new GridMapPrototype { Name = name, Error = exception.Message };
            }

            var prototype = new GridMapPrototype { Name = name };
            root.UpdateTransform();
            Matrix4x4.Invert(root.WorldTransform, out var toRoot);

            var triangles = new List<Vector3>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            Collect(root);

            prototype.Triangles = triangles.ToArray();
            prototype.BoundsMin = prototype.Parts.Count > 0 ? min : Vector3.Zero;
            prototype.BoundsMax = prototype.Parts.Count > 0 ? max : Vector3.Zero;
            return prototype;

            void Collect(ManaSceneNode node)
            {
                foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
                {
                    if (renderer.Mesh == null)
                    {
                        continue;
                    }

                    var transform = node.WorldTransform * toRoot;
                    prototype.Parts.Add((node.Name, renderer.Mesh, renderer.Material, transform));

                    ManaCore.Graphics.ManaMeshCpuData cpu;
                    try
                    {
                        cpu = renderer.Mesh.GetOrCreateCpuData();
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    foreach (var position in cpu.Positions)
                    {
                        var point = Vector3.Transform(position, transform);
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }

                    for (var i = 0; i + 2 < cpu.Indexes.Length; i += 3)
                    {
                        triangles.Add(Vector3.Transform(cpu.Positions[cpu.Indexes[i]], transform));
                        triangles.Add(Vector3.Transform(cpu.Positions[cpu.Indexes[i + 1]], transform));
                        triangles.Add(Vector3.Transform(cpu.Positions[cpu.Indexes[i + 2]], transform));
                    }
                }

                foreach (var child in node.Children)
                {
                    Collect(child);
                }
            }
        }

        /// <summary>
        /// A tile: an empty root with one mesh node per part, all sharing this prototype's meshes.
        /// <paramref name="model"/> places the model inside the root -- see <c>ManaGridMapNode.ModelTransform</c>.
        /// </summary>
        public ManaSceneNode Instantiate(ManaContext context, string name, Matrix4x4 model)
        {
            var root = new ManaSceneNode(context, Matrix4x4.Identity) { Name = name, IsEditorTransient = true };
            foreach (var part in Parts)
            {
                var node = new ManaMeshSceneNode(context, part.Mesh) { Name = part.Name, IsEditorTransient = true };
                node.MeshRenderer.Material = part.Material;
                node.SetLocalTransform(part.Transform * model);
                root.AddChild(node);
            }

            return root;
        }
    }
}
