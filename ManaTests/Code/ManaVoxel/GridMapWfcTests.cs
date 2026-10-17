using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// <see cref="GridMapWfc"/>: that what it lays joins up -- every label faces the same label, nothing leads
/// out of the area, big rooms come out whole -- checked by rebuilding each cell's sides from the rules
/// rather than from the solver's own parts; and that it runs into a grid map as tiles.
/// </summary>
public static class GridMapWfcChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // The turn the solver cuts tiles by has to be the turn a grid map draws them at: a modelled cell's
        // centre, turned about the tile the way ManaGridMapNode turns it, must land in the cell TurnCell says.
        var turnsAgree = true;
        foreach (var (w, d) in new[] { (1, 1), (3, 2), (5, 3) })
        {
            for (var turn = 0; turn < 4; turn++)
            {
                var (tw, td) = turn % 2 == 0 ? (w, d) : (d, w);
                for (var x = 0; x < w; x++)
                {
                    for (var z = 0; z < d; z++)
                    {
                        var local = new Vector3(x + 0.5f - w * 0.5f, 0.0f, z + 0.5f - d * 0.5f);
                        var turned = Vector3.Transform(local, Matrix4x4.CreateRotationY(turn * MathF.PI * 0.5f));
                        var cell = ((int)MathF.Floor(turned.X + tw * 0.5f), (int)MathF.Floor(turned.Z + td * 0.5f));
                        turnsAgree &= GridMapWfc.TurnCell(x, z, w, d, turn) == cell;
                    }
                }
            }
        }

        Check("a tile's cells turn with it the way a grid map turns the tile", turnsAgree);

        var rules = GridMapWfcPresets.Dungeon();
        var result = GridMapWfc.Solve(rules, 24, 24, seed: 7);
        Check("the dungeon rules fill a 24x24 area", result.Succeeded, $"{result.Error} after {result.Attempts} attempts");
        if (!result.Succeeded)
        {
            return;
        }

        var joins = Mismatches(rules, result.Placements, 24, 24);
        Check("every side faces a matching label and nothing leads out of the area", joins.Count == 0, string.Join("; ", joins.Take(5)));

        var rooms = result.Placements.Count(p => rules.Tiles[p.Tile].Width * rules.Tiles[p.Tile].Depth > 1);
        Check("rooms are laid as well as corridors", rooms > 0, $"{rooms} rooms, {result.Placements.Count} tiles");

        var again = GridMapWfc.Solve(rules, 24, 24, seed: 7);
        Check("the same seed lays the same dungeon", again.Placements.SequenceEqual(result.Placements));
        var other = GridMapWfc.Solve(rules, 24, 24, seed: 8);
        Check("and another seed another one", !other.Placements.SequenceEqual(result.Placements));

        Check("only the largest joined part is kept, so the dungeon is one piece", Components(rules, result.Placements, 24, 24) == 1,
            $"{Components(rules, result.Placements, 24, 24)} pieces");
        var loose = GridMapWfc.Solve(rules, 24, 24, seed: 7, connectedOnly: false);
        Check("unless asked to keep everything", loose.Placements.Count >= result.Placements.Count);

        // Rooms laid first: left to the collapse, a tile needing nine or twenty-five cells to agree rarely wins.
        int RoomsIn(GridMapWfc.Result laid) => laid.Placements.Count(p => rules.Tiles[p.Tile].Width * rules.Tiles[p.Tile].Depth > 1);
        var seeds = Enumerable.Range(1, 8).ToList();
        var chance = seeds.Select(s => GridMapWfc.Solve(rules, 24, 24, seed: s)).ToList();
        var planned = seeds.Select(s => GridMapWfc.Solve(rules, 24, 24, seed: s, rooms: 5)).ToList();
        Check("asking for rooms lays them, where chance alone mostly makes corridors",
            planned.All(r => r.Succeeded) && planned.Average(RoomsIn) >= 5.0 && planned.Average(RoomsIn) >= chance.Average(RoomsIn) + 2.0,
            $"{chance.Average(RoomsIn):0.0} rooms by chance, {planned.Average(RoomsIn):0.0} asked for 5; {planned.Count(r => !r.Succeeded)} failed");
        Check("and what is built round them still joins up",
            planned.Where(r => r.Succeeded).All(r => Mismatches(rules, r.Placements, 24, 24).Count == 0),
            string.Join("; ", planned.Where(r => r.Succeeded).SelectMany(r => Mismatches(rules, r.Placements, 24, 24)).Take(3)));
        Check("and keeping to one piece keeps most of them", planned.Average(RoomsIn) >= 4.0, $"{planned.Average(RoomsIn):0.0}");
        Check("the same seed still lays the same rooms", GridMapWfc.Solve(rules, 24, 24, seed: 3, rooms: 5).Placements.SequenceEqual(planned[2].Placements));

        // Big rooms, built from pieces at any size, laid before the rest: kept, whole, and met at every door.
        var withBig = Enumerable.Range(1, 6).Select(s => GridMapWfc.Solve(rules, 30, 30, seed: s, rooms: 2, bigRooms: 2)).ToList();
        Check("big rooms are laid, and kept", withBig.All(r => r.Succeeded) && withBig.Average(r => r.BigRooms.Count) >= 1.5,
            string.Join(", ", withBig.Select(r => $"{r.BigRooms.Count}{(r.Succeeded ? "" : " failed")}")));
        Check("each as big as the rules ask",
            withBig.SelectMany(r => r.BigRooms).All(room => room.Width is >= 5 and <= 10 && room.Depth is >= 5 and <= 10),
            string.Join(", ", withBig.SelectMany(r => r.BigRooms).Select(room => $"{room.Width}x{room.Depth}")));
        Check("with every door met by a corridor and nothing else touching the room but walls",
            withBig.Where(r => r.Succeeded).All(r => Mismatches(rules, r.Placements, 30, 30, r.BigRooms).Count == 0),
            string.Join("; ", withBig.Where(r => r.Succeeded).SelectMany(r => Mismatches(rules, r.Placements, 30, 30, r.BigRooms)).Take(4)));

        // The size asked for is the size laid, even when it is most of the area.
        var huge = GridMapWfcPresets.Dungeon();
        huge.BigRooms!.MinSize = 20;
        huge.BigRooms.MaxSize = 20;
        var hugeResult = GridMapWfc.Solve(huge, 60, 60, seed: 80464, rooms: 4, bigRooms: 2);
        Check("big rooms twenty cells across are laid and kept when asked for",
            hugeResult.Succeeded && hugeResult.BigRooms.Count == 2 && hugeResult.BigRooms.All(room => room is { Width: 20, Depth: 20 }),
            $"{hugeResult.BigRooms.Count} kept: {string.Join(", ", hugeResult.BigRooms.Select(room => $"{room.Width}x{room.Depth}"))}; {hugeResult.Error}");

        // Big areas have to stay quick: a 60x60 dungeon used to take long enough to look hung.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var big = GridMapWfc.Solve(rules, 60, 60, seed: 5, rooms: 12);
        clock.Stop();
        Check("a 60x60 dungeon generates in a couple of seconds", big.Succeeded && clock.Elapsed.TotalSeconds < 3.0,
            $"{clock.Elapsed.TotalSeconds:0.00}s, {big.Placements.Count} tiles, {big.Attempts} attempts; {big.Error}");

        // A room painted first stays where it was, and the rest is built round it.
        var small = rules.Tiles.FindIndex(tile => tile.Name == "Small room");
        var kept = GridMapWfc.Solve(rules, 16, 16, seed: 3, keep: [new GridMapWfc.Fixed(6, 6, small, 0)], connectedOnly: false);
        Check("a tile already painted is kept, and built round",
            kept.Succeeded && kept.Placements.Contains(new GridMapWfcPlacement(small, 0, 6, 6)) && Mismatches(rules, kept.Placements, 16, 16).Count == 0,
            kept.Error ?? "");

        var round = GridMapWfcRules.FromJson(rules.ToJson());
        Check("rules read back as they are written",
            round.Tiles.Count == rules.Tiles.Count && round.Boundary == rules.Boundary
            && round.Tiles.Zip(rules.Tiles).All(pair => pair.First.Name == pair.Second.Name && pair.First.Rotations == pair.Second.Rotations
                && pair.First.Width == pair.Second.Width && pair.First.Socket(1, 2) == pair.Second.Socket(1, 2)));

        var impossible = new GridMapWfcRules
        {
            Boundary = "wall",
            Tiles = [new GridMapWfcTile { Name = "Only open", Asset = "x", Sockets = new() { ["e"] = ["open"], ["n"] = ["open"], ["w"] = ["open"], ["s"] = ["open"] } }]
        };
        var failed = GridMapWfc.Solve(impossible, 4, 4, seed: 1, maxAttempts: 3);
        Check("rules that cannot close the area in say so instead of laying something broken", !failed.Succeeded && failed.Error != null);

        RunGridMapChecks(rules, Check);
        RunPropChecks((what, ok, detail) => Check(what, ok, detail));

        // The rules that ship in Content/Generators/ are the preset, so a fresh checkout generates the same dungeon.
        var shipped = GridMapWfcGenerator.List().FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == "Dungeon");
        var file = shipped == null ? null : GridMapWfcGenerator.Load(shipped);
        Check("the Dungeon rules file ships, and is the preset",
            file != null && file.Tiles.Count == rules.Tiles.Count
            && file.Tiles.Zip(rules.Tiles).All(pair => pair.First.Name == pair.Second.Name && pair.First.Asset == pair.Second.Asset
                && pair.First.Rotations == pair.Second.Rotations && pair.First.Width == pair.Second.Width && pair.First.Depth == pair.Second.Depth
                && Enumerable.Range(0, 4).All(side => Enumerable.Range(0, 5).All(i => pair.First.Socket(side, i) == pair.Second.Socket(side, i)))),
            shipped ?? $"none in {GridMapWfcGenerator.ResolveFolder()}");

        var shippedProps = GridMapWfcGenerator.FindRules("Dungeon Props");
        var propsPreset = GridMapWfcPresets.DungeonProps();
        Check("the Dungeon Props rules file ships, is the preset, and the Dungeon rules lead to it",
            file?.Then == "Dungeon Props" && shippedProps is { IsProps: true } && shippedProps.Base == "Dungeon"
            && shippedProps.Pairs.Count == propsPreset.Pairs.Count && shippedProps.SideNames.Count == propsPreset.SideNames.Count
            && shippedProps.Tiles.Zip(propsPreset.Tiles).All(pair => pair.First.Name == pair.Second.Name && pair.First.Asset == pair.Second.Asset
                && pair.First.Weight == pair.Second.Weight && pair.First.ModelCell == pair.Second.ModelCell
                && (pair.First.On ?? []).SequenceEqual(pair.Second.On ?? [])
                && Enumerable.Range(0, 4).All(side => pair.First.Socket(side, 0) == pair.Second.Socket(side, 0)))
            && shippedProps.Tiles.Count == propsPreset.Tiles.Count,
            $"then {file?.Then}, props {shippedProps?.Tiles.Count} tiles");

        var shippedClutter = GridMapWfcGenerator.FindRules("Table Clutter");
        var clutterPreset = GridMapWfcPresets.TableClutter();
        Check("the Table Clutter rules ship, are the preset, and the props rules lead to them",
            shippedProps?.Then == "Table Clutter" && shippedClutter is { IsProps: true, Base: "Dungeon Props" }
            && shippedClutter.Slots?.Count == clutterPreset.Slots?.Count
            && shippedClutter.Tiles.Select(t => (t.Name, t.Asset, t.Weight, t.ModelCell)).SequenceEqual(clutterPreset.Tiles.Select(t => (t.Name, t.Asset, t.Weight, t.ModelCell)))
            && shippedProps.Tiles.Single(t => t.Name == "Table").Tag == "table",
            $"clutter {shippedClutter?.Tiles.Count} tiles");

        RunWindowChecks(fonts, Check);
    }

    private static void RunWindowChecks(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        var gridMap = new ManaGridMapNode(context);
        world.DefaultScene.Children.Add(gridMap);

        var root = new UiRoot(fonts);
        root.Content = EditorWorkspace.Attach(root, new ManaUiShowcaseViewModel(), null, persist: false, editor: new VoxelEditorViewModel(), world: world);
        UiWindow? Window() => EditorWorkspace.For(root).DragController?.FindFloating(GridMapWfcManaView.PanelId);
        void Frame()
        {
            root.UpdateLayout(new Vector2(1600, 900));
            root.UpdateLayout(new Vector2(1600, 900));
        }

        try
        {
            GridMapWfcEditing.WindowOpen = false;
            world.Selection.SelectedNode = gridMap;
            Frame();
            check("the generator is not up just because a grid map is selected", Window() == null, "");

            GridMapWfcEditing.WindowOpen = true;
            Frame();
            check("asking for it floats the generator window", Window() != null, "");
            var drawn = root.Render().InstanceCount;
            check("and it lays out and draws", drawn > 40, $"{drawn} instances");

            world.Selection.SelectedNode = null;
            Frame();
            check("selecting something else leaves it up, still on the same map",
                Window() != null && ReferenceEquals(GridMapWfcManaView.Target(world), gridMap), "");

            // Props rules offer each side's kind by name, and do not ask anyone to type pairs of labels.
            GridMapWfcEditing.Replace(GridMapWfcPresets.DungeonProps(), null);
            Frame();
            foreach (var expander in Find<Expander>(Window()!))
            {
                expander.IsExpanded = true;
            }

            Frame();
            var sideLists = Find<ComboBox>(Window()!).Count(combo => combo.Items.Any(item => item?.ToString() == "Faces a table"));
            var labelsShown = Find<TextBlock>(Window()!).Any(text => text.Text == "Pairs");
            check("a props tile's sides are picked from named kinds, with no pairs to know",
                sideLists >= 4 * 9 && !labelsShown, $"{sideLists} side lists; pairs shown: {labelsShown}");
            GridMapWfcEditing.Replace(GridMapWfcPresets.Dungeon(), null);

            Window()?.Close();
            Frame();
            check("closing it by hand is remembered", !GridMapWfcEditing.WindowOpen && Window() == null, "");
        }
        finally
        {
            GridMapWfcEditing.WindowOpen = false;
            world.Selection.SelectedNode = null;
        }
    }

    /// <summary>
    /// The props pass: a dungeon laid, then furnished from what it turned out to be. Each rule is checked
    /// against the dungeon as read back from the map -- a prop only where it may stand, its back to a wall, a
    /// chair facing a table with no wall between -- and re-rolling the props leaves the dungeon alone.
    /// </summary>
    private static void RunPropChecks(Action<string, bool, string> check)
    {
        var steps = new[] { (1, 0), (0, -1), (-1, 0), (0, 1) };
        var originalLoader = ManaGridMapNode.LoadAsset;
        ManaGridMapNode.LoadAsset = LoadCube;
        try
        {
            var dungeon = GridMapWfcPresets.Dungeon();
            var props = GridMapWfcPresets.DungeonProps();
            var wrong = new List<string>();
            int chairs = 0, tables = 0, backed = 0, laid = 0, doorways = 0, onTables = 0, spots = 0;
            for (var seed = 1; seed <= 5; seed++)
            {
                var world = new ManaWorld();
                var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
                world.Context = context;
                world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
                world.Scenes.Add(world.DefaultScene);
                var gridMap = new ManaGridMapNode(context);
                world.DefaultScene.Children.Add(gridMap);
                gridMap.UpdateTransform();

                var area = new GridMapWfcGenerator.Area(0, 0, 30, 30, 0);
                var result = GridMapWfcGenerator.Generate(gridMap, dungeon, area, seed, keepPainted: true, connectedOnly: true, rooms: 3, bigRooms: 1);
                if (result.Props is not { Succeeded: true } furnished)
                {
                    wrong.Add($"seed {seed}: {result.Error ?? result.Props?.Error ?? "no props pass"}");
                    continue;
                }

                var cells = GridMapWfcGenerator.ReadDungeon(gridMap, dungeon, area);
                GridMapWfc.Cell At(int x, int z) => cells[x + z * 30];
                bool Walled(int x, int z, int side)
                {
                    var (nx, nz) = (x + steps[side].Item1, z + steps[side].Item2);
                    return At(x, z).Walls.HasFlag(GridMapAutoTile.Neighbours[side].Side)
                        || nx < 0 || nz < 0 || nx >= 30 || nz >= 30
                        || At(nx, nz).Walls.HasFlag(GridMapAutoTile.Neighbours[(side + 2) % 4].Side);
                }

                var byCell = furnished.Placements.ToDictionary(p => (p.X, p.Z), p => p);
                foreach (var p in furnished.Placements)
                {
                    var tile = props.Tiles[p.Tile];
                    laid++;
                    if (tile.On is { Length: > 0 } on && !on.Contains(At(p.X, p.Z).Tag))
                    {
                        wrong.Add($"{tile.Name} on {At(p.X, p.Z).Tag} at ({p.X},{p.Z})");
                    }

                    for (var modelled = 0; modelled < 4; modelled++)
                    {
                        var side = (modelled + p.Orientation) % 4;
                        var label = tile.Socket(modelled, 0);
                        if (label == ">back")
                        {
                            backed++;
                            if (!Walled(p.X, p.Z, side))
                            {
                                wrong.Add($"{tile.Name} at ({p.X},{p.Z}) has its back to open floor");
                            }
                        }

                        if (label == ">seat")
                        {
                            chairs++;
                            var facing = (p.X + steps[side].Item1, p.Z + steps[side].Item2);
                            if (Walled(p.X, p.Z, side) || !byCell.TryGetValue(facing, out var other) || props.Tiles[other.Tile].Name != "Table")
                            {
                                wrong.Add($"chair at ({p.X},{p.Z}) faces no table");
                            }
                        }
                    }

                    tables += tile.Name == "Table" ? 1 : 0;
                    if (tile.Name == "Table" && Enumerable.Range(0, 4).Any(side => Walled(p.X, p.Z, side)))
                    {
                        wrong.Add($"table against a wall at ({p.X},{p.Z})");
                    }

                    if (At(p.X, p.Z).Tag is "door" or "entry" or "doorstep")
                    {
                        wrong.Add($"{tile.Name} in a doorway ({At(p.X, p.Z).Tag}) at ({p.X},{p.Z})");
                    }
                }

                doorways += cells.Count(cell => cell.Tag == "door");

                // What stands on the tables: only on a table's cell, in both spots, each raised to the tabletop.
                if (furnished.Props is { Succeeded: true } clutter)
                {
                    var tablesHere = furnished.Placements.Where(p => props.Tiles[p.Tile].Name == "Table").Select(p => (p.X, p.Z)).ToHashSet();
                    var clutterRules = GridMapWfcPresets.TableClutter();
                    foreach (var item in clutter.Placements.Where(p => clutterRules.Tiles[p.Tile].Asset != null))
                    {
                        onTables++;
                        if (!tablesHere.Contains((item.X, item.Z)))
                        {
                            wrong.Add($"{clutterRules.Tiles[item.Tile].Name} off any table at ({item.X},{item.Z})");
                        }
                    }

                    var clutterLayers = Enumerable.Range(0, gridMap.LayerCount).Where(l => gridMap.LayerName(l).StartsWith("Base clutter", StringComparison.Ordinal)).ToList();
                    spots = Math.Max(spots, clutterLayers.Count);
                    var raised = clutterLayers.SelectMany(l => gridMap.LayerCells(l).Values).All(t => gridMap.ItemSettings(t.Item).Offset.Y > 0.3f);
                    if (!raised)
                    {
                        wrong.Add("a clutter item not raised to the tabletop");
                    }
                }
                else
                {
                    wrong.Add($"seed {seed}: no clutter pass ({furnished.Props?.Error})");
                }

                // Re-rolling only the props: the dungeon underneath is left exactly as it was.
                var dungeonCells = gridMap.LayerCells(0).ToDictionary(pair => pair.Key, pair => pair.Value);
                var before = gridMap.CaptureState();
                var reroll = GridMapWfcGenerator.Generate(gridMap, props, area, seed + 100, keepPainted: true, connectedOnly: false);
                if (!reroll.Succeeded || gridMap.LayerCells(0).Count != dungeonCells.Count
                    || dungeonCells.Any(pair => !gridMap.LayerCells(0).TryGetValue(pair.Key, out var t) || t != pair.Value))
                {
                    wrong.Add($"seed {seed}: re-rolling props changed the dungeon ({reroll.Error})");
                }

                gridMap.RestoreState(before);
            }

            check("props are laid over a generated dungeon", laid > 50, $"{laid} props over 5 dungeons");
            check("with tables and the chairs round them", tables > 0 && chairs > 0, $"{tables} tables, {chairs} chairs");
            check("doorways are found", doorways > 10, $"{doorways} door cells over 5 dungeons");
            check("small things are laid on the tables, in two spots each, on layers of their own",
                onTables > tables && spots == 2, $"{onTables} on {tables} tables, {spots} spots");
            check("each only where it may stand -- tables off the walls, doorways clear -- its back to a wall, and every chair facing a table",
                wrong.Count == 0 && backed > 0, string.Join("; ", wrong.Take(5)));
        }
        finally
        {
            ManaGridMapNode.LoadAsset = originalLoader;
        }
    }

    private static void RunGridMapChecks(GridMapWfcRules rules, Action<string, bool, string> check)
    {
        var originalLoader = ManaGridMapNode.LoadAsset;
        ManaGridMapNode.LoadAsset = LoadCube;
        try
        {
            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            world.Context = context;
            world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
            world.Scenes.Add(world.DefaultScene);
            var gridMap = new ManaGridMapNode(context);
            world.DefaultScene.Children.Add(gridMap);
            gridMap.UpdateTransform();

            var outside = new GridMapCell(-5, 0, -5);
            gridMap.SetTile(outside, new GridMapTile(gridMap.AddPaletteItem("tree"), 0));
            var before = gridMap.CaptureState();
            var area = new GridMapWfcGenerator.Area(0, 0, 12, 12, 0);
            var result = GridMapWfcGenerator.Generate(gridMap, rules, area, seed: 11, keepPainted: true, connectedOnly: true);
            check("generating into a grid map lays one tile per placement", result.Succeeded && gridMap.Cells.Count == result.Placements.Count + 1,
                $"{gridMap.Cells.Count} cells for {result.Placements.Count} placements; {result.Error}");

            var room = rules.Tiles.FindIndex(tile => tile.Name == "Large room");
            var roomItem = gridMap.Palette.ToList().FindIndex(asset => asset == rules.Tiles[room].Asset);
            check("with each tile's model sized to the cells it covers",
                roomItem >= 0 && gridMap.ItemFootprint(roomItem) == new GridMapFootprint(5, 5), "");
            check("and nothing outside the area touched", gridMap.TryGetTile(outside, out _), "");

            // A tile painted over one the generator laid is the painter's, and generating again keeps it.
            var claimed = gridMap.Cells.First(pair => gridMap.IsGenerated(pair.Key) && rules.Tiles.Any(t => t.Asset == gridMap.Palette[pair.Value.Item]));
            gridMap.SetTile(claimed.Key, claimed.Value with { Orientation = claimed.Value.Orientation });
            check("painting over a generated tile claims it as painted", !gridMap.IsGenerated(claimed.Key), "");
            var rerun = GridMapWfcGenerator.Generate(gridMap, rules, area, seed: 12, keepPainted: true, connectedOnly: false);
            check("so generating again keeps it, and builds round it",
                rerun.Succeeded && gridMap.TryGetTile(claimed.Key, out var stillThere) && stillThere == claimed.Value, rerun.Error ?? "");

            // Generating again with Keep painted on replaces what the generator laid, rather than keeping it all:
            // kept, the old dungeon filled the area and nothing new -- a big room least of all -- had room.
            var firstLaid = gridMap.Cells.Where(pair => gridMap.IsGenerated(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            GridMapWfcGenerator.Generate(gridMap, rules, area, seed: 99, keepPainted: true, connectedOnly: true);
            var changed = firstLaid.Count(pair => !gridMap.TryGetTile(pair.Key, out var now) || now != pair.Value);
            check("re-rolling replaces the generator's own tiles, even with Keep painted on", changed > firstLaid.Count / 4,
                $"{changed} of {firstLaid.Count} changed");
            check("and one painted there before is still painted", gridMap.TryGetTile(outside, out _) && !gridMap.IsGenerated(outside), "");

            var saved = ManaVoxel.Code.Levels.ManaLevelGridMapCodec.Encode(gridMap);
            var layers = ManaVoxel.Code.Levels.ManaLevelGridMapCodec.DecodeLayers(saved);
            check("which tiles were generated is saved with the level",
                layers[0].Generated is { } marks && marks.SetEquals(gridMap.LayerGenerated(0)) && !marks.Contains(outside)
                && layers[0].Cells.All(pair => gridMap.TryGetTile(pair.Key, out var tile) && tile == pair.Value),
                $"{layers[0].Generated?.Count} marked of {layers[0].Cells.Count}");

            gridMap.RestoreState(before);
            check("and undoing puts the map back", gridMap.Cells.Count == 1, $"{gridMap.Cells.Count}");
        }
        finally
        {
            ManaGridMapNode.LoadAsset = originalLoader;
        }
    }

    /// <summary>
    /// Every cell's four labels, rebuilt from the rules: each placed tile's modelled sides, turned onto the
    /// cells it covers. Cells no tile covers are the rules' empty tile. Then each pair of neighbours, and each
    /// side facing out, is checked.
    /// </summary>
    private static List<string> Mismatches(GridMapWfcRules rules, IReadOnlyList<GridMapWfcPlacement> placements, int width, int depth,
        IReadOnlyList<GridMapWfc.Room>? bigRooms = null)
    {
        var steps = new[] { (1, 0), (0, -1), (-1, 0), (0, 1) };
        var empty = rules.Tiles.First(tile => tile.Asset == null);
        var labels = new string?[width, depth, 4];
        var covered = new bool[width, depth];
        var problems = new List<string>();
        foreach (var placement in placements)
        {
            var tile = rules.Tiles[placement.Tile];
            for (var x = 0; x < tile.Width; x++)
            {
                for (var z = 0; z < tile.Depth; z++)
                {
                    var (tx, tz) = GridMapWfc.TurnCell(x, z, tile.Width, tile.Depth, placement.Orientation);
                    var cx = placement.X + tx;
                    var cz = placement.Z + tz;
                    if (cx < 0 || cz < 0 || cx >= width || cz >= depth)
                    {
                        problems.Add($"{tile.Name} hangs out of the area at ({cx},{cz})");
                        continue;
                    }

                    if (covered[cx, cz])
                    {
                        problems.Add($"two tiles on ({cx},{cz})");
                    }

                    covered[cx, cz] = true;
                    for (var side = 0; side < 4; side++)
                    {
                        var inside = x + steps[side].Item1 >= 0 && x + steps[side].Item1 < tile.Width && z + steps[side].Item2 >= 0 && z + steps[side].Item2 < tile.Depth;
                        labels[cx, cz, (side + placement.Orientation) % 4] = inside ? null : tile.Socket(side, side is 0 or 2 ? z : x);
                    }
                }
            }
        }

        // A big room's cells: nothing inside between them, "wall" outward, the joining label at each door.
        foreach (var room in bigRooms ?? [])
        {
            for (var x = room.X; x < room.X + room.Width; x++)
            {
                for (var z = room.Z; z < room.Z + room.Depth; z++)
                {
                    if (covered[x, z])
                    {
                        problems.Add($"a tile on big room cell ({x},{z})");
                    }

                    covered[x, z] = true;
                    for (var side = 0; side < 4; side++)
                    {
                        var nx = x + steps[side].Item1;
                        var nz = z + steps[side].Item2;
                        var inside = nx >= room.X && nx < room.X + room.Width && nz >= room.Z && nz < room.Z + room.Depth;
                        labels[x, z, side] = inside ? null : room.Doors.Contains((x, z, side)) ? rules.Connects : rules.Boundary;
                    }
                }
            }
        }

        for (var x = 0; x < width; x++)
        {
            for (var z = 0; z < depth; z++)
            {
                for (var side = 0; side < 4; side++)
                {
                    var label = covered[x, z] ? labels[x, z, side] : empty.Socket(side, 0);
                    if (label == null)
                    {
                        continue;
                    }

                    var nx = x + steps[side].Item1;
                    var nz = z + steps[side].Item2;
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                    {
                        if (label != rules.Boundary)
                        {
                            problems.Add($"({x},{z}) leads out with {label}");
                        }

                        continue;
                    }

                    var facing = covered[nx, nz] ? labels[nx, nz, (side + 2) % 4] : empty.Socket((side + 2) % 4, 0);
                    if (facing != label)
                    {
                        problems.Add($"({x},{z}) {label} faces {facing ?? "the inside of a tile"}");
                    }
                }
            }
        }

        return problems;
    }

    /// <summary>How many separate groups the laid tiles make, joined through open sides or by being one tile.</summary>
    private static int Components(GridMapWfcRules rules, IReadOnlyList<GridMapWfcPlacement> placements, int width, int depth)
    {
        var owner = new int[width, depth];
        for (var x = 0; x < width; x++)
        {
            for (var z = 0; z < depth; z++)
            {
                owner[x, z] = -1;
            }
        }

        for (var i = 0; i < placements.Count; i++)
        {
            var tile = rules.Tiles[placements[i].Tile];
            for (var x = 0; x < tile.Width; x++)
            {
                for (var z = 0; z < tile.Depth; z++)
                {
                    var (tx, tz) = GridMapWfc.TurnCell(x, z, tile.Width, tile.Depth, placements[i].Orientation);
                    owner[placements[i].X + tx, placements[i].Z + tz] = i;
                }
            }
        }

        // Tiles join when their cells touch across an open side; union by placement.
        var parent = Enumerable.Range(0, placements.Count).ToArray();
        int Find(int a) => parent[a] == a ? a : parent[a] = Find(parent[a]);
        for (var x = 0; x + 1 < width; x++)
        {
            for (var z = 0; z < depth; z++)
            {
                Join(x, z, x + 1, z);
            }
        }

        for (var x = 0; x < width; x++)
        {
            for (var z = 0; z + 1 < depth; z++)
            {
                Join(x, z, x, z + 1);
            }
        }

        void Join(int ax, int az, int bx, int bz)
        {
            if (owner[ax, az] >= 0 && owner[bx, bz] >= 0 && owner[ax, az] != owner[bx, bz] && Open(ax, az, bx, bz))
            {
                parent[Find(owner[ax, az])] = Find(owner[bx, bz]);
            }
        }

        bool Open(int ax, int az, int bx, int bz)
        {
            var placement = placements[owner[ax, az]];
            var tile = rules.Tiles[placement.Tile];
            var side = bx > ax ? 0 : 3;
            var modelled = ((side - placement.Orientation) % 4 + 4) % 4;
            for (var x = 0; x < tile.Width; x++)
            {
                for (var z = 0; z < tile.Depth; z++)
                {
                    if (GridMapWfc.TurnCell(x, z, tile.Width, tile.Depth, placement.Orientation) == (ax - placement.X, az - placement.Z))
                    {
                        return tile.Socket(modelled, modelled is 0 or 2 ? z : x) == rules.Connects;
                    }
                }
            }

            return false;
        }

        return Enumerable.Range(0, placements.Count).Select(Find).Distinct().Count();
    }

    private static ManaSceneNode LoadCube(string asset, ManaWorld world, ManaScene into)
    {
        var holder = new ManaSceneNode(into.Context, Matrix4x4.Identity) { Name = asset };
        into.Children.Add(holder);
        var cube = new ManaShapeFactory(into.Context, into).AddCube(Vector3.Zero, Vector4.One, Vector3.One);
        into.Children.Remove(cube);
        holder.AddChild(cube);
        return holder;
    }

    private static List<T> Find<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        void Walk(UiElement current)
        {
            if (current is T typed)
            {
                found.Add(typed);
            }

            foreach (var child in current.VisualChildren)
            {
                Walk(child);
            }
        }

        Walk(element);
        return found;
    }
}
