using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.GameModes;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

namespace ManaTests;

/// <summary>
/// <see cref="ManaGridMapNode"/>: cells, the palette, tiles sharing one loaded model, the level round trip,
/// and colliders that things actually land on. The last runs against Kenney's road and dungeon kits when
/// the packs are present, since a collider built the wrong way round looks fine and lets everything through.
/// </summary>
public static class GridMapChecks
{
    private const string RoadPack = "kenney_city-kit-roads.zip";
    private const string DungeonPack = "kenney_modular-dungeon-kit_1.0.zip";

    private static ManaContext CreateContext(bool physics)
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };

        // Initialize() wants a real graphics context, and it is what sets this -- which the glTF loader reads.
        world.Context = context;
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        if (physics)
        {
            context.Physics = new ManaPhysics(context);
        }

        return context;
    }

    private static ManaGridMapNode AddGridMap(ManaContext context)
    {
        var gridMap = new ManaGridMapNode(context);
        context.World.DefaultScene.Children.Add(gridMap);
        gridMap.UpdateTransform();
        return gridMap;
    }

    /// <summary>Stands in for asset loading: every "asset" is a unit cube, so no packs are needed.</summary>
    private static ManaSceneNode LoadCube(string asset, ManaWorld world, ManaScene into)
    {
        if (asset.StartsWith("missing", StringComparison.Ordinal))
        {
            throw new FileNotFoundException($"No such asset '{asset}'.");
        }

        // "wide" stands in for a two-cell piece. Under a holder, since a prototype is read relative to its root.
        // "backwall" is authored the way a kit authors a straight wall: one cell around its origin, standing
        // at the back of it -- Kenney's brick-wall runs z -0.5 to -0.32.
        var size = asset.StartsWith("wide", StringComparison.Ordinal) ? new Vector3(2.0f, 1.0f, 1.0f)
            : asset.StartsWith("backwall", StringComparison.Ordinal) ? new Vector3(1.0f, 1.0f, 0.2f)
            : Vector3.One;
        var at = asset.StartsWith("backwall", StringComparison.Ordinal) ? new Vector3(0.0f, 0.0f, -0.4f) : Vector3.Zero;
        var holder = new ManaSceneNode(into.Context, Matrix4x4.Identity) { Name = asset };
        into.Children.Add(holder);
        var cube = new ManaShapeFactory(into.Context, into).AddCube(at, Vector4.One, size);
        into.Children.Remove(cube);
        holder.AddChild(cube);
        return holder;
    }

    private static ManaSceneNode? TileAt(ManaGridMapNode gridMap, GridMapCell cell) =>
        gridMap.Children.FirstOrDefault(child => child.Name.EndsWith($"({cell.X}, {cell.Y}, {cell.Z})", StringComparison.Ordinal));

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var originalLoader = ManaGridMapNode.LoadAsset;
        try
        {
            ManaGridMapNode.LoadAsset = LoadCube;
            RunCellChecks(Check);
            RunRoundTripChecks(Check);
            RunSizingChecks(Check);
            RunLayerChecks(Check);
            RunAutoTileChecks(Check);
            RunPlayerStartChecks(Check);
            RunEditorChecks(fonts, Check);
        }
        finally
        {
            ManaGridMapNode.LoadAsset = originalLoader;
        }

        RunAssetChecks(Check);
    }

    private static void RunCellChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        Check("orientations wrap both ways",
            ManaGridMapNode.WrapOrientation(5) == 1 && ManaGridMapNode.WrapOrientation(-1) == 3 && ManaGridMapNode.WrapOrientation(4) == 0);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        gridMap.CellSize = new Vector3(2.0f, 1.0f, 2.0f);

        Check("a point falls in the cell whose floor it is over",
            gridMap.CellAt(new Vector3(3.9f, 0.0f, -0.1f), 2) == new GridMapCell(1, 2, -1),
            gridMap.CellAt(new Vector3(3.9f, 0.0f, -0.1f), 2).ToString());
        Check("and a tile sits at the centre of that cell's floor",
            gridMap.CellOrigin(new GridMapCell(1, 2, -1)) == new Vector3(3.0f, 2.0f, -1.0f),
            gridMap.CellOrigin(new GridMapCell(1, 2, -1)).ToString());

        var a = gridMap.AddPaletteItem("tile-a");
        var b = gridMap.AddPaletteItem("tile-b");
        Check("adding an asset twice finds the first", gridMap.AddPaletteItem("TILE-A") == a && gridMap.Palette.Count == 2);

        var first = new GridMapCell(0, 0, 0);
        var second = new GridMapCell(1, 0, 0);
        gridMap.SetTile(first, new GridMapTile(a, 1));
        gridMap.SetTile(second, new GridMapTile(b, 6));
        Check("painting builds one tile per cell", gridMap.Children.Count == 2, $"{gridMap.Children.Count} children");
        Check("and wraps the rotation it is given", gridMap.TryGetTile(second, out var wrapped) && wrapped.Orientation == 2);

        var firstTile = TileAt(gridMap, first);
        var turned = firstTile != null ? Vector3.TransformNormal(Vector3.UnitX, firstTile.LocalTransform) : Vector3.Zero;
        Check("a quarter turn turns the tile a quarter about Y",
            Vector3.Distance(turned, -Vector3.UnitZ) < 0.001f, turned.ToString());

        Check("tiles stay out of the outliner and out of level capture",
            gridMap.Children.All(child => child.IsEditorTransient && child.LevelSource == null));

        var repaint = gridMap.Revision;
        gridMap.SetTile(first, new GridMapTile(a, 1));
        Check("repainting a cell with what it already holds changes nothing", gridMap.Revision == repaint);

        var meshesBefore = context.World.Meshes.Count;
        gridMap.SetTile(new GridMapCell(5, 0, 5), new GridMapTile(a, 0));
        var tileMeshes = gridMap.Children
            .SelectMany(child => child.Children)
            .OfType<ManaMeshSceneNode>()
            .Select(node => node.Mesh)
            .Distinct()
            .Count();
        Check("tiles of one palette item share one mesh", tileMeshes == 2 && context.World.Meshes.Count == meshesBefore,
            $"{tileMeshes} distinct meshes for 2 items; world meshes {meshesBefore} -> {context.World.Meshes.Count}");

        gridMap.RemovePaletteItem(a);
        Check("removing a palette item clears its cells and renumbers the rest",
            gridMap.Cells.Count == 1 && gridMap.TryGetTile(second, out var shifted) && shifted.Item == 0 && gridMap.Children.Count == 1,
            $"{gridMap.Cells.Count} cells, {gridMap.Children.Count} tiles");

        var broken = gridMap.AddPaletteItem("missing-tile");
        gridMap.SetTile(first, new GridMapTile(broken, 0));
        Check("a palette item that will not load keeps its cells but builds nothing",
            gridMap.Cells.Count == 2 && gridMap.Children.Count == 1 && gridMap.PaletteError(broken) != null,
            gridMap.PaletteError(broken) ?? "no error");

        gridMap.ClearAll();
        Check("clearing removes every cell and tile", gridMap.Cells.Count == 0 && gridMap.Children.Count == 0);
    }

    private static void RunSizingChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        var small = gridMap.AddPaletteItem("tile");
        var wide = gridMap.AddPaletteItem("wide");
        Check("a new tile covers one cell", gridMap.ItemFootprint(wide) == new GridMapFootprint(1, 1));
        Check("and adding a tile never changes the cells", gridMap.CellSize == Vector3.One);

        gridMap.CellSize = new Vector3(10.0f, 1.0f, 10.0f);
        Check("a fitted one-unit tile on ten-unit cells draws ten times bigger",
            MathF.Abs(gridMap.ItemScale(small) - 10.0f) < 0.001f, gridMap.ItemScale(small).ToString());
        gridMap.SetTile(new GridMapCell(0, 0, 0), new GridMapTile(small, 0));
        var part = gridMap.TileNode(new GridMapCell(0, 0, 0))?.Children.FirstOrDefault();
        var drawnSize = part != null ? Vector3.TransformNormal(Vector3.UnitX, part.LocalTransform).Length() : 0.0f;
        Check("so it fills its cell rather than leaving a gap", MathF.Abs(drawnSize - 10.0f) < 0.001f, drawnSize.ToString());
        Check("a long thin piece fits by its length, not its thickness",
            MathF.Abs(gridMap.ItemScale(wide) - 5.0f) < 0.001f, gridMap.ItemScale(wide).ToString());

        gridMap.SetItemSettings(small, gridMap.ItemSettings(small) with { Scale = 1.5f });
        Check("a tile can be given a fixed scale instead", gridMap.ItemScale(small) == 1.5f);

        gridMap.CellSize = Vector3.One;
        gridMap.ClearAll();
        gridMap.SetItemSettings(wide, gridMap.ItemSettings(wide) with { Width = 2 });
        Check("a two-unit piece set to two cells fits at its own size", MathF.Abs(gridMap.ItemScale(wide) - 1.0f) < 0.001f);
        var anchor = new GridMapCell(0, 0, 0);
        gridMap.SetTile(anchor, new GridMapTile(wide, 0));
        Check("and covers the cell beside its anchor",
            gridMap.TryGetTileCovering(new GridMapCell(1, 0, 0), out var coveringAnchor, out _) && coveringAnchor == anchor
            && ReferenceEquals(gridMap.TileNode(new GridMapCell(1, 0, 0)), gridMap.TileNode(anchor)));
        var wideCentre = gridMap.TileNode(anchor)?.LocalTransform.Translation ?? Vector3.Zero;
        Check("standing centred over both", Vector3.Distance(wideCentre, new Vector3(1.0f, 0.0f, 0.5f)) < 0.001f, wideCentre.ToString());
        Check("turned a quarter, it covers the cell in front instead",
            gridMap.Footprint(new GridMapTile(wide, 1)) == new GridMapFootprint(1, 2)
            && gridMap.CoveredCells(anchor, new GridMapTile(wide, 1)).Contains(new GridMapCell(0, 0, 1)));

        gridMap.SetTile(new GridMapCell(1, 0, 0), new GridMapTile(small, 0));
        Check("painting over half of a big tile replaces it",
            gridMap.Cells.Count == 1 && gridMap.TryGetTile(new GridMapCell(1, 0, 0), out var replaced) && replaced.Item == small
            && !gridMap.TryGetTileCovering(anchor, out _, out _),
            $"{gridMap.Cells.Count} tiles");

        gridMap.ClearAll();
        gridMap.SetTile(anchor, new GridMapTile(wide, 0));
        gridMap.SetTile(anchor, new GridMapTile(wide, 1));
        Check("turning a placed tile where it stands keeps one tile, turned",
            gridMap.Cells.Count == 1 && gridMap.TryGetTile(anchor, out var turned) && turned.Orientation == 1
            && gridMap.TryGetTileCovering(new GridMapCell(0, 0, 1), out _, out _) && !gridMap.TryGetTileCovering(new GridMapCell(1, 0, 0), out _, out _));
        gridMap.SetTile(anchor, new GridMapTile(wide, 0));
        gridMap.ClearTile(new GridMapCell(1, 0, 0));
        Check("clearing any cell of a big tile clears the tile", gridMap.Cells.Count == 0 && gridMap.Children.Count == 0);

        // An edge piece stands against the back of its cell: a thin wall one cell long, cells of 4.
        gridMap.CellSize = new Vector3(4.0f, 1.0f, 4.0f);
        gridMap.SetItemSettings(wide, GridMapItemSettings.Default with { Align = GridMapAlign.Edge });
        var edge = Vector3.Transform(Vector3.Zero, gridMap.ModelTransform(wide));
        Check("an edge-aligned wall sits against the back of its cell, not in the middle",
            MathF.Abs(edge.Z - (-2.0f + gridMap.ItemScale(wide) * 0.5f)) < 0.001f, edge.ToString());
        gridMap.SetItemSettings(wide, GridMapItemSettings.Default with { Width = 2 });
        var lengthways = gridMap.ItemScale(wide);
        gridMap.SetItemSettings(wide, GridMapItemSettings.Default with { Width = 2, Turn = 1 });
        Check("a model's own turn swaps which way it fits",
            MathF.Abs(lengthways - 4.0f) < 0.001f && MathF.Abs(gridMap.ItemScale(wide) - 2.0f) < 0.001f,
            $"{lengthways} then {gridMap.ItemScale(wide)}");

        gridMap.CellSize = Vector3.One;
        gridMap.SetItemSettings(wide, GridMapItemSettings.Default with { Width = 2 });
        gridMap.SetTile(new GridMapCell(4, 0, 4), new GridMapTile(small, 0));
        gridMap.SetTile(new GridMapCell(5, 0, 4), new GridMapTile(small, 0));
        var before = gridMap.CaptureState();
        gridMap.SetTile(new GridMapCell(4, 0, 4), new GridMapTile(wide, 0));
        Check("a big tile knocks out the neighbour it lands on", gridMap.Cells.Count == 1);
        gridMap.RestoreState(before);
        Check("and putting the state back brings that neighbour back too",
            gridMap.Cells.Count == 2 && gridMap.TryGetTile(new GridMapCell(5, 0, 4), out _) && gridMap.Children.Count == 2,
            $"{gridMap.Cells.Count} tiles, {gridMap.Children.Count} nodes");

        var backWall = gridMap.AddPaletteItem("backwall");
        Check("a tile is placed as modelled by default", gridMap.ItemSettings(backWall).Align == GridMapAlign.Model);
        Check("so a wall authored at the back of its cell stays there, where the corner piece meets it",
            gridMap.ModelTransform(backWall).Translation == Vector3.Zero, gridMap.ModelTransform(backWall).Translation.ToString());
        Check("and fits by the cell it was modelled in, not by its thickness",
            MathF.Abs(gridMap.ItemScale(backWall) - 1.0f) < 0.001f, gridMap.ItemScale(backWall).ToString());
        gridMap.SetItemSettings(backWall, GridMapItemSettings.Default with { Align = GridMapAlign.Centre });
        Check("centring by bounds is still there for models pivoted elsewhere",
            MathF.Abs(gridMap.ModelTransform(backWall).Translation.Z - 0.4f) < 0.001f, gridMap.ModelTransform(backWall).Translation.ToString());

        gridMap.SetItemSettings(small, new GridMapItemSettings { Width = 2, Depth = 3, Scale = 2.0f, Turn = 2, Align = GridMapAlign.Edge });
        var level = ManaLevel.FromJson(ManaLevelCapture.Capture(context.World, "Sizes").ToJson());
        var reloaded = CreateContext(physics: false);
        var loaded = ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.OfType<ManaGridMapNode>().SingleOrDefault();
        Check("each tile's settings are saved with the level",
            loaded != null && loaded.ItemSettings(small) == gridMap.ItemSettings(small) && loaded.ItemSettings(wide) == gridMap.ItemSettings(wide),
            $"{loaded?.ItemSettings(small)}");
    }

    private static void RunLayerChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        var floor = gridMap.AddPaletteItem("floor");
        var wall = gridMap.AddPaletteItem("backwall");
        var cell = new GridMapCell(2, 0, 3);
        gridMap.SetTile(cell, new GridMapTile(floor, 0));

        var walls = gridMap.AddLayer("Walls");
        Check("a new layer becomes the one painted on", gridMap.ActiveLayer == walls && gridMap.LayerCount == 2);
        gridMap.SetTile(cell, new GridMapTile(wall, 1));
        Check("so a wall can stand in the same cell as the floor under it",
            gridMap.TileCount == 2 && gridMap.LayerCells(0).ContainsKey(cell) && gridMap.LayerCells(walls).ContainsKey(cell)
            && gridMap.Children.Count == 2, $"{gridMap.TileCount} tiles, {gridMap.Children.Count} nodes");

        gridMap.SetLayerVisible(0, false);
        Check("a hidden layer builds nothing but keeps its cells",
            gridMap.Children.Count == 1 && gridMap.LayerCells(0).Count == 1, $"{gridMap.Children.Count} nodes");
        gridMap.SetLayerVisible(0, true);
        Check("and comes back when shown", gridMap.Children.Count == 2);

        var before = gridMap.CaptureState();
        gridMap.ClearAll();
        Check("clearing clears only the active layer", gridMap.LayerCells(walls).Count == 0 && gridMap.LayerCells(0).Count == 1);
        gridMap.RestoreState(before);
        Check("and undoing it puts that layer back", gridMap.LayerCells(walls).Count == 1 && gridMap.Children.Count == 2);

        gridMap.RenameLayer(0, "Ground");
        gridMap.SetLayerVisible(walls, false);
        var level = ManaLevel.FromJson(ManaLevelCapture.Capture(context.World, "Layers").ToJson());
        var reloaded = CreateContext(physics: false);
        var loaded = ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.OfType<ManaGridMapNode>().SingleOrDefault();
        Check("layers are saved with their names, visibility and cells",
            loaded != null && loaded.LayerCount == 2 && loaded.LayerName(0) == "Ground" && loaded.LayerName(1) == "Walls"
            && loaded.IsLayerVisible(0) && !loaded.IsLayerVisible(1)
            && loaded.LayerCells(1).TryGetValue(cell, out var savedWall) && savedWall == new GridMapTile(wall, 1)
            && loaded.Children.Count == 1,
            $"{loaded?.LayerCount} layers, {loaded?.Children.Count} nodes");

        gridMap.RemoveLayer(walls);
        Check("removing a layer removes its tiles", gridMap.LayerCount == 1 && gridMap.TileCount == 1 && gridMap.Children.Count == 1);
        gridMap.RemoveLayer(0);
        Check("but the last layer stays", gridMap.LayerCount == 1);

        // A level saved before layers: 9-byte records, no layer list.
        var old = new ManaLevelGridMap
        {
            Palette = new List<string> { "floor" },
            Count = 1,
            Data = Convert.ToBase64String(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3 })
        };
        var oldLayers = ManaLevelGridMapCodec.DecodeLayers(old);
        Check("a level saved before layers loads into one layer",
            oldLayers.Count == 1 && oldLayers[0].Cells.TryGetValue(new GridMapCell(1, 0, 2), out var oldTile) && oldTile == new GridMapTile(0, 3));
    }

    private static void RunAutoTileChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        Check("a quarter turn walks the sides round: +X to -Z",
            GridMapAutoTile.Rotate(GridMapSides.East, 1) == GridMapSides.North
            && GridMapAutoTile.Rotate(GridMapSides.West | GridMapSides.South, 1) == (GridMapSides.South | GridMapSides.East)
            && GridMapAutoTile.Rotate(GridMapSides.East, -1) == GridMapSides.South);
        Check("sides read back as they are written",
            GridMapAutoTile.Parse(GridMapAutoTile.Describe(GridMapSides.East | GridMapSides.South)) == (GridMapSides.East | GridMapSides.South)
            && GridMapAutoTile.Parse("none") == GridMapSides.None && GridMapAutoTile.Parse("15") == GridMapSides.All);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        var names = new[] { "square", "end", "straight", "bend", "tee", "cross" };
        var sides = new[]
        {
            GridMapSides.None, GridMapSides.East, GridMapSides.East | GridMapSides.West, GridMapSides.West | GridMapSides.South,
            GridMapSides.East | GridMapSides.West | GridMapSides.South, GridMapSides.All
        };
        var items = names.Select(gridMap.AddPaletteItem).ToArray();
        var set = gridMap.AddAutoTileSet(new GridMapAutoTileSet("Road", items.Select((item, i) => new GridMapAutoTilePiece(item, sides[i])).ToList()));
        int Item(string name) => items[Array.IndexOf(names, name)];
        GridMapTile? At(int x, int z) => gridMap.TryGetTile(new GridMapCell(x, 0, z), out var tile) ? tile : null;

        gridMap.PaintAutoTile(set, new GridMapCell(0, 0, 0));
        Check("a road cell on its own is the piece that joins nothing", At(0, 0) == new GridMapTile(Item("square"), 0), $"{At(0, 0)}");

        gridMap.PaintAutoTile(set, new GridMapCell(1, 0, 0));
        Check("a second beside it makes both ends, facing each other",
            At(0, 0) == new GridMapTile(Item("end"), 0) && At(1, 0) == new GridMapTile(Item("end"), 2), $"{At(0, 0)} {At(1, 0)}");

        gridMap.PaintAutoTile(set, new GridMapCell(2, 0, 0));
        Check("a third turns the middle one into a straight", At(1, 0)?.Item == Item("straight") && At(2, 0) == new GridMapTile(Item("end"), 2),
            $"{At(1, 0)} {At(2, 0)}");

        gridMap.PaintAutoTile(set, new GridMapCell(1, 0, 1));
        Check("a branch off it to +Z makes a T-junction", At(1, 0) == new GridMapTile(Item("tee"), 0), $"{At(1, 0)}");
        Check("and the branch is an end facing back up it (-Z)", At(1, 1) == new GridMapTile(Item("end"), 1), $"{At(1, 1)}");

        gridMap.PaintAutoTile(set, new GridMapCell(1, 0, -1));
        Check("one to -Z as well makes a crossroad", At(1, 0)?.Item == Item("cross"), $"{At(1, 0)}");

        gridMap.ClearTile(new GridMapCell(1, 0, -1));
        Check("clearing it takes the crossroad back to a T", At(1, 0) == new GridMapTile(Item("tee"), 0), $"{At(1, 0)}");

        gridMap.ClearTile(new GridMapCell(2, 0, 0));
        Check("and clearing the east arm leaves a bend from -X round to +Z", At(1, 0) == new GridMapTile(Item("bend"), 0), $"{At(1, 0)}");

        var before = gridMap.CaptureState();
        gridMap.SetTile(new GridMapCell(1, 0, 1), new GridMapTile(Item("square"), 0));
        gridMap.SetTile(new GridMapCell(1, 0, 1), new GridMapTile(gridMap.AddPaletteItem("tree"), 0));
        Check("a tile from outside the set painted over a road cuts the road there", At(1, 0) == new GridMapTile(Item("end"), 2), $"{At(1, 0)}");
        gridMap.RestoreState(before);
        Check("and undoing puts the bend back", At(1, 0) == new GridMapTile(Item("bend"), 0) && gridMap.AutoTiles.Count == 1, $"{At(1, 0)}");

        var level = ManaLevel.FromJson(ManaLevelCapture.Capture(context.World, "Roads").ToJson());
        var reloaded = CreateContext(physics: false);
        var loaded = ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.OfType<ManaGridMapNode>().SingleOrDefault();
        Check("auto-tile sets are saved with their pieces and sides",
            loaded != null && loaded.AutoTiles.Count == 1 && loaded.AutoTiles[0].SameAs(gridMap.AutoTiles[0]),
            loaded == null ? "no map" : string.Join(", ", loaded.AutoTiles.SelectMany(s => s.Pieces).Select(p => $"{p.Item}:{p.Sides}")));

        // A set missing a T-junction has to join three ways with what it has: the crossroad, not a gap.
        var partial = new GridMapAutoTileSet("No tee", gridMap.AutoTiles[set].Pieces.Where(piece => piece.Item != Item("tee")).ToList());
        GridMapAutoTile.TryResolve(partial, GridMapSides.East | GridMapSides.West | GridMapSides.South, out var nearest);
        Check("a set without the exact piece uses the nearest that leaves no join missing", nearest.Item == Item("cross"), $"{nearest}");

        gridMap.RemovePaletteItem(Item("square"));
        Check("removing a palette item drops it from the set and moves the rest down",
            gridMap.AutoTiles[set].Pieces.Count == 5 && gridMap.AutoTiles[set].Pieces[0].Item == Item("end") - 1);

        var preset = CreateContext(physics: false);
        var presetMap = AddGridMap(preset);
        var road = GridMapAutoTilePresets.AddTo(presetMap, GridMapAutoTilePresets.Road);
        Check("the road preset fills the palette with the city kit's pieces, one set over them",
            presetMap.Palette.Count == GridMapAutoTilePresets.Road.Pieces.Count
            && presetMap.AutoTiles[road].Pieces.Count == presetMap.Palette.Count
            && presetMap.Palette.All(asset => asset.Contains("kenney_city-kit-roads.zip", StringComparison.Ordinal)));

        var path = VoxelEditorGameMode.GridStrokePath(new GridMapCell(0, 0, 0), new GridMapCell(3, 0, 2)).ToList();
        var joined = path.Zip(path.Skip(1)).All(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Z - pair.Second.Z) == 1)
            && Math.Abs(path[0].X) + Math.Abs(path[0].Z) == 1;
        Check("a quick diagonal drag paints every cell between, each beside the last", joined && path[^1] == new GridMapCell(3, 0, 2) && path.Count == 5,
            string.Join(" ", path.Select(c => $"({c.X},{c.Z})")));
    }

    /// <summary>A player start's character height survives a save, and a start that says nothing loads at the default.</summary>
    private static void RunPlayerStartChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: false);
        var start = new ManaPlayerStartNode(context, Vector3.Zero);
        context.World.DefaultScene.Children.Add(start);
        var plain = ManaLevel.FromJson(ManaLevelCapture.Capture(context.World, "Start").ToJson());
        check("a start left at the default height writes no character entry",
            plain.Nodes.Single(node => node.Kind == ManaLevelSourceKind.PlayerStart).Character == null, "");

        start.CharacterHeight = 0.5f;
        var level = ManaLevel.FromJson(ManaLevelCapture.Capture(context.World, "Start").ToJson());
        var reloaded = CreateContext(physics: false);
        var loaded = ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.OfType<ManaPlayerStartNode>().SingleOrDefault();
        check("a height set on a start with no character chosen is saved with it",
            loaded != null && MathF.Abs(loaded.CharacterHeight - 0.5f) < 1e-4f && string.IsNullOrEmpty(loaded.CharacterName),
            $"{loaded?.CharacterHeight}");

        var bare = ManaLevel.FromJson(ManaLevelCapture.Capture(CreateContext(physics: false).World, "None").ToJson());
        bare.Nodes.Add(new ManaLevelNode { Kind = ManaLevelSourceKind.PlayerStart, Name = "Old start" });
        var old = CreateContext(physics: false);
        var oldStart = ManaLevelLoader.Load(bare, old, old.World.DefaultScene, replaceAuthoredNodes: false)
            .Nodes.OfType<ManaPlayerStartNode>().SingleOrDefault();
        check("a start saved with no character entry plays at the default height",
            oldStart != null && MathF.Abs(oldStart.CharacterHeight - ManaPlayerStartNode.DefaultCharacterHeight) < 1e-4f, $"{oldStart?.CharacterHeight}");
    }

    private static void RunRoundTripChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        gridMap.Name = "Dungeon";
        gridMap.Position = new Vector3(4.0f, 0.5f, -2.0f);
        gridMap.CellSize = new Vector3(4.0f, 3.0f, 4.0f);
        gridMap.Collision = false;
        var floor = gridMap.AddPaletteItem("floor");
        var wall = gridMap.AddPaletteItem("wall");
        var painted = new Dictionary<GridMapCell, GridMapTile>
        {
            [new GridMapCell(0, 0, 0)] = new(floor, 0),
            [new GridMapCell(-3, 0, 7)] = new(wall, 3),
            [new GridMapCell(2, -1, -300)] = new(floor, 2),
            [new GridMapCell(1, 4, 1)] = new(wall, 1)
        };
        foreach (var (cell, tile) in painted)
        {
            gridMap.SetTile(cell, tile);
        }

        var json = ManaLevelCapture.Capture(context.World, "Grid Round Trip").ToJson();
        var level = ManaLevel.FromJson(json);
        var entry = level.Nodes.SingleOrDefault(node => node.Kind == ManaLevelSourceKind.GridMap);
        Check("a grid map saves as one node, not one per tile",
            level.Nodes.Count == 1 && entry?.Children == null && entry?.GridMap is { Count: 4 },
            $"{level.Nodes.Count} nodes, {entry?.GridMap?.Count} cells");

        var reloaded = CreateContext(physics: false);
        var result = ManaLevelLoader.Load(level, reloaded, reloaded.World.DefaultScene, replaceAuthoredNodes: false);
        var loaded = result.Nodes.OfType<ManaGridMapNode>().SingleOrDefault();
        Check("and loads back as a grid map", loaded != null, string.Join(" | ", result.Warnings));
        if (loaded == null)
        {
            return;
        }

        Check("with its name, place and settings",
            loaded.Name == "Dungeon" && loaded.Position == gridMap.Position
            && loaded.CellSize == gridMap.CellSize && !loaded.Collision,
            $"{loaded.Name} {loaded.Position} {loaded.CellSize} collision={loaded.Collision}");
        Check("its palette in order", loaded.Palette.SequenceEqual(gridMap.Palette), string.Join(", ", loaded.Palette));
        Check("and every cell, rotations included",
            loaded.Cells.Count == painted.Count && painted.All(pair => loaded.TryGetTile(pair.Key, out var tile) && tile == pair.Value));
        Check("with its tiles rebuilt", loaded.Children.Count == painted.Count, $"{loaded.Children.Count} tiles");
    }

    /// <summary>
    /// Drops a box onto a single glTF-wound (counter-clockwise from above) quad, which is solid from one side
    /// only -- so this is the check that the collider's triangles face the way the model's do.
    /// </summary>
    private static float DropOntoUpwardQuad()
    {
        var context = CreateContext(physics: true);
        var floor = new ManaSceneNode(context, Matrix4x4.Identity);
        context.World.DefaultScene.Children.Add(floor);
        floor.UpdateTransform();
        Vector3[] quad =
        {
            new(-2, 0, -2), new(-2, 0, 2), new(2, 0, 2),
            new(-2, 0, -2), new(2, 0, 2), new(2, 0, -2)
        };
        var normal = Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]);
        if (normal.Y <= 0.0f)
        {
            throw new InvalidOperationException("The test quad is wound the wrong way round.");
        }

        context.Physics.AddTriangleMeshBody(floor, quad);
        var box = context.Physics.AddDynamicBoxBodyDirect(new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.3f));
        for (var step = 0; step < 180; step++)
        {
            context.Physics.Simulation.Timestep(1.0f / 60.0f, context.Physics.ThreadDispatcher);
        }

        return context.Physics.Simulation.Bodies[box].Pose.Position.Y;
    }

    private static void RunEditorChecks(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        var other = AddGridMap(context);
        gridMap.AddPaletteItem("floor");
        gridMap.AddPaletteItem("wall");

        try
        {
            GridMapEditing.Brush = 7;
            GridMapEditing.Begin(gridMap);
            Check("starting to paint keeps the brush inside the palette", GridMapEditing.Brush == 1, GridMapEditing.Brush.ToString());
            Check("and paints that map, not another", GridMapEditing.IsPainting(gridMap) && !GridMapEditing.IsPainting(other));

            GridMapEditing.Orientation = 0;
            GridMapEditing.Rotate(-1);
            Check("turning the brush back from zero wraps to three quarter turns", GridMapEditing.Orientation == 3);

            GridMapEditing.Brush = GridMapEditing.Blank;
            GridMapEditing.Stop();
            GridMapEditing.Begin(gridMap);
            Check("the Blank brush survives starting to paint, and clears", GridMapEditing.Brush == GridMapEditing.Blank && GridMapEditing.Erasing);
            GridMapEditing.Brush = 0;

            // The tools live in a window of their own, which comes up when a grid map is selected and goes with it.
            var root = new UiRoot(fonts);
            var editor = new VoxelEditorViewModel();
            root.Content = ManaUiDockDemo.Attach(
                root, new ManaUiShowcaseViewModel(), null, persist: false, editor: editor, world: context.World);
            root.UpdateLayout(new Vector2(1600, 900));

            bool PanelOpen(UiRoot where) =>
                ManaUiDockDemo.LastDragController?.FindFloating(GridMapToolManaView.PanelId) != null
                || Hosts(where.Content!).Any(host => host.AllItems().Any(item => item.Id == GridMapToolManaView.PanelId));
            Check("the grid map window is not open with nothing selected", !PanelOpen(root));
            Check("and the inspector does not carry the grid map tools",
                !InspectorWidgetFactory.CreateDefault().AllFor(gridMap).Any(widget => widget.GetType().Name.Contains("GridMap")));

            context.World.Selection.SelectedNode = gridMap;
            root.UpdateLayout(new Vector2(1600, 900));
            var window = ManaUiDockDemo.LastDragController?.FindFloating(GridMapToolManaView.PanelId);
            Check("selecting a grid map floats the grid map window", window != null);
            Check("leaving every docked panel where it was",
                !Hosts(root.Content!).Any(host => host.AllItems().Any(item => item.Id == GridMapToolManaView.PanelId))
                && Hosts(root.Content!).Any(host => host.AllItems().Any(item => item.Id == "outliner")));

            root.UpdateLayout(new Vector2(1600, 900));
            var drawn = root.Render().InstanceCount;
            Check("and it lays out and draws", drawn > 20, $"{drawn} instances");

            var drop = GridMapToolManaView.PaletteDropBounds;
            Check("its palette is where a model dragged from the asset browser can be dropped",
                drop.Width > 0.0f && GridMapToolManaView.IsOverPalette(new Vector2(drop.X + drop.Width * 0.5f, drop.Y + drop.Height * 0.5f))
                && !GridMapToolManaView.IsOverPalette(new Vector2(drop.X - 5.0f, drop.Y - 5.0f)),
                drop.ToString());

            context.World.Selection.SelectedNode = null;
            root.UpdateLayout(new Vector2(1600, 900));
            Check("selecting something else closes it again", !PanelOpen(root));

            // A layout saved while a grid map was selected must not bring the window back at start-up.
            var layout = Path.Combine(Path.GetTempPath(), $"manaui-gridmap-{Guid.NewGuid():N}.json");
            try
            {
                var saving = new UiRoot(fonts);
                saving.Content = ManaUiDockDemo.Attach(
                    saving, new ManaUiShowcaseViewModel(), null, persist: true, editor: editor, world: context.World, layoutPath: layout);
                context.World.Selection.SelectedNode = gridMap;
                saving.UpdateLayout(new Vector2(1600, 900));
                var savedOpen = PanelOpen(saving);

                context.World.Selection.SelectedNode = null;
                var reopened = new UiRoot(fonts);
                reopened.Content = ManaUiDockDemo.Attach(
                    reopened, new ManaUiShowcaseViewModel(), null, persist: true, editor: editor, world: context.World, layoutPath: layout);
                reopened.UpdateLayout(new Vector2(1600, 900));
                reopened.UpdateLayout(new Vector2(1600, 900));
                Check("a layout saved with the window up does not bring it back with nothing selected",
                    savedOpen && !PanelOpen(reopened));
            }
            finally
            {
                File.Delete(layout);
            }
        }
        finally
        {
            GridMapEditing.Stop();
        }
    }

    private static List<DockHost> Hosts(UiElement element)
    {
        var found = new List<DockHost>();
        void Walk(UiElement current)
        {
            if (current is DockHost host)
            {
                found.Add(host);
            }

            for (var i = 0; i < current.VisualChildCount; i++)
            {
                Walk(current.GetVisualChild(i));
            }
        }

        Walk(element);
        return found;
    }

    private static void RunAssetChecks(Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var quadRest = DropOntoUpwardQuad();
        Check("a mesh collider is solid on the side its faces point", quadRest > 0.1f && quadRest < 0.3f,
            $"came to rest at y={quadRest:0.###}");

        var road = MeshAssetLibrary.EncodeReadableZip(RoadPack, "Models/GLB format/road-straight.glb");
        var corridor = MeshAssetLibrary.EncodeReadableZip(DungeonPack, "Models/GLB format/corridor.glb");
        if (!MeshAssetLibrary.TryDecodeZip(road, out var roadZip, out _) || !File.Exists(roadZip)
            || !MeshAssetLibrary.TryDecodeZip(corridor, out var dungeonZip, out _) || !File.Exists(dungeonZip))
        {
            Check("the road and dungeon kits are present for the collider checks", false, roadZip);
            return;
        }

        var context = CreateContext(physics: true);
        var gridMap = AddGridMap(context);
        var roadItem = gridMap.AddPaletteItem(road);
        var corridorItem = gridMap.AddPaletteItem(corridor);
        var roadSize = gridMap.PaletteItemSize(roadItem);
        var corridorSize = gridMap.PaletteItemSize(corridorItem);
        Check("a road tile loads", roadSize is { X: > 0.0f, Z: > 0.0f }, $"road {roadSize}, error {gridMap.PaletteError(roadItem)}");
        Check("and so does a corridor", corridorSize is { X: > 0.0f, Z: > 0.0f }, $"corridor {corridorSize}");

        gridMap.CellSize = roadSize is { } size ? new Vector3(size.X, 1.0f, size.Z) : Vector3.One;
        var cell = new GridMapCell(0, 0, 0);
        gridMap.SetTile(cell, new GridMapTile(roadItem, 0));
        var tile = TileAt(gridMap, cell);
        var collider = tile?.Components.OfType<ManaCollisionComponent>().SingleOrDefault();
        Check("a painted tile gets a triangle-mesh collider", collider?.Shape is BepuPhysics.Collidables.Mesh);

        // A box dropped onto the middle of the road must come to rest on it rather than fall through.
        var physics = context.Physics;
        var origin = gridMap.CellOrigin(cell);
        var top = roadSize?.Y ?? 0.0f;
        var box = physics.AddDynamicBoxBodyDirect(origin + new Vector3(0.0f, top + 1.0f, 0.0f), new Vector3(0.3f));
        for (var step = 0; step < 180; step++)
        {
            physics.Simulation.Timestep(1.0f / 60.0f, physics.ThreadDispatcher);
        }

        var rest = physics.Simulation.Bodies[box].Pose.Position.Y;
        Check("something dropped on a road tile lands on it", rest > top - 0.2f && rest < top + 0.6f,
            $"came to rest at y={rest:0.###}; road top {top:0.###}");

        gridMap.ClearTile(cell);
        Check("clearing the tile removes its collider from the simulation",
            collider == null || !physics.Simulation.Bodies.BodyExists(collider.Body));

        RunRoadPresetGeometryChecks(Check);
        RunWalkChecks(Check);
    }

    /// <summary>
    /// A character walks the length of a row of tiles at a steady requested speed, as the controller drives
    /// it: horizontal velocity set every step, gravity left to the solver. Kit floors are not flat -- stones
    /// are bevelled, corridors have rubble -- and a tile's colliders must not turn that into drag. It may
    /// check for a frame where two tiles meet, but never stall.
    /// </summary>
    private static void RunWalkChecks(Action<string, bool, string> check)
    {
        foreach (var (label, model, height) in new[]
        {
            ("a flat floor", "template-floor.glb", 1.8f),
            ("a dungeon corridor", "corridor.glb", 1.8f),
            ("a dungeon corridor, small hero", "corridor.glb", 0.76f),
            ("a dungeon room floor", "room-large.glb", 1.8f)
        })
        {
            var context = CreateContext(physics: true);
            var gridMap = AddGridMap(context);
            gridMap.CellSize = new Vector3(4.0f);
            var item = gridMap.AddPaletteItem(MeshAssetLibrary.EncodeReadableZip(DungeonPack, "Models/GLB format/" + model));
            var span = model.StartsWith("room-large", StringComparison.Ordinal) ? 5 : 1;
            // Smoothed as the dungeon rules smooth it, which is how a generated dungeon lays it.
            var smooth = GridMapWfcPresets.Dungeon().Tiles.FirstOrDefault(tile => tile.Asset?.EndsWith("/" + model, StringComparison.Ordinal) == true)?.Smooth ?? 0.0f;
            gridMap.SetItemSettings(item, gridMap.ItemSettings(item) with { Width = span, Depth = span, Smooth = smooth });
            for (var x = 0; x < 8; x += span)
            {
                gridMap.SetTile(new GridMapCell(x, 0, 0), new GridMapTile(item, 0));
            }

            // Down the middle of the corridor, along X, in the room's middle row.
            var z = span * 4.0f * 0.5f;
            var body = ManaCharacterBody.Create(context, new Vector3(2.0f, 0.3f, z), height * 0.18f, height);
            var physics = context.Physics!;
            const float speed = 3.0f;
            for (var step = 0; step < 30; step++)
            {
                physics.Simulation.Timestep(1.0f / 60.0f, physics.ThreadDispatcher);
            }

            var start = body.FootPosition;
            var slowest = float.MaxValue;
            for (var step = 0; step < 120; step++)
            {
                var velocity = body.Velocity;
                body.Velocity = new Vector3(speed, velocity.Y, 0.0f);
                var before = body.FootPosition.X;
                physics.Simulation.Timestep(1.0f / 60.0f, physics.ThreadDispatcher);
                if (step > 10)
                {
                    slowest = MathF.Min(slowest, (body.FootPosition.X - before) * 60.0f);
                }
            }

            var travelled = body.FootPosition.X - start.X;
            check($"walking across {label} keeps its pace", travelled > speed * 2.0f * 0.9f && slowest > speed * 0.3f,
                $"{travelled:0.00} of {speed * 2.0f:0.00} in 2s, slowest {slowest:0.00} u/s, y {start.Y:0.00} to {body.FootPosition.Y:0.00}");
        }
    }

    /// <summary>
    /// The road preset's sides against the models themselves: a road piece is open where its asphalt reaches
    /// the middle of a cell edge. Placed as a painted tile would be, turned, so this also holds the turning
    /// convention to the neighbour each side is meant to face.
    /// </summary>
    private static void RunRoadPresetGeometryChecks(Action<string, bool, string> check)
    {
        var context = CreateContext(physics: false);
        var gridMap = AddGridMap(context);
        var set = GridMapAutoTilePresets.AddTo(gridMap, GridMapAutoTilePresets.Road);
        var wrong = new List<string>();
        foreach (var piece in gridMap.AutoTiles[set].Pieces)
        {
            for (var turn = 0; turn < ManaGridMapNode.OrientationCount; turn++)
            {
                var anchor = new GridMapCell(turn * 3, 0, 0);
                var tile = new GridMapTile(piece.Item, turn);
                gridMap.SetTile(anchor, tile);
                var node = gridMap.TileNode(anchor);
                if (node == null)
                {
                    wrong.Add($"{gridMap.PaletteItemName(piece.Item)} did not load: {gridMap.PaletteError(piece.Item)}");
                    break;
                }

                node.UpdateTransform();
                var points = new List<Vector3>();
                Collect(node);
                var centre = gridMap.CellOrigin(anchor);
                var open = GridMapSides.None;
                foreach (var (side, x, z) in GridMapAutoTile.Neighbours)
                {
                    // Edge-middle points: on the edge facing this side, near its middle.
                    var edge = centre + new Vector3(x, 0.0f, z) * 0.5f;
                    if (points.Any(p => MathF.Abs((p.X - edge.X) * x + (p.Z - edge.Z) * z) < 0.02f
                            && MathF.Abs((p.X - edge.X) * z + (p.Z - edge.Z) * x) < 0.15f))
                    {
                        open |= side;
                    }
                }

                var expected = GridMapAutoTile.Rotate(piece.Sides, turn);
                if (open != expected)
                {
                    wrong.Add($"{gridMap.PaletteItemName(piece.Item)} turned {turn}: opens {GridMapAutoTile.Describe(open)}, set says {GridMapAutoTile.Describe(expected)}");
                }

                void Collect(ManaSceneNode current)
                {
                    foreach (var renderer in current.Components.OfType<MeshRendererComponent>())
                    {
                        if (renderer.Mesh != null)
                        {
                            points.AddRange(renderer.Mesh.GetOrCreateCpuData().Positions.Select(p => Vector3.Transform(p, current.WorldTransform)));
                        }
                    }

                    foreach (var child in current.Children)
                    {
                        Collect(child);
                    }
                }
            }
        }

        check("each road piece opens on the sides the preset says, however it is turned", wrong.Count == 0, string.Join("; ", wrong));
    }
}
