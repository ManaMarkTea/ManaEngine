using ManaEditor.Code.Assets;

namespace ManaEditor.Code.GridMaps
{
    /// <summary>
    /// Runs <see cref="GridMapWfc"/> into a grid map: an area of the active layer on one floor, laid with the
    /// rules' tiles as ordinary palette tiles. What the rules' tiles already cover in the area can be kept, so
    /// a room painted by hand is built round rather than over.
    /// </summary>
    public static class GridMapWfcGenerator
    {
        public const string FolderName = "Content/Generators";

        public const string Extension = ".manawfc";

        public sealed record Area(int X, int Z, int Width, int Depth, int Floor);

        /// <summary>
        /// Generates into the map, replacing the area, and then furnishes it when the rules name a props pass
        /// (<see cref="GridMapWfcRules.Then"/>). Props rules on their own furnish what is already there. The
        /// caller records the undo step.
        /// </summary>
        public static GridMapWfc.Result Generate(
            ManaGridMapNode gridMap, GridMapWfcRules rules, Area area, int seed, bool keepPainted, bool connectedOnly, int rooms = 0, int bigRooms = 0)
        {
            if (rules.IsProps)
            {
                return GenerateProps(gridMap, rules, FindRules(rules.Base) ?? GridMapWfcPresets.Dungeon(), area, seed, keepPainted);
            }

            var result = GenerateDungeon(gridMap, rules, area, seed, keepPainted, connectedOnly, rooms, bigRooms);
            if (!result.Succeeded)
            {
                return result;
            }

            // Its own seed, one on from the dungeon's, so re-rolling the props alone does not repeat these.
            return FindRules(rules.Then) is { IsProps: true } props
                ? result with { Props = GenerateProps(gridMap, props, rules, area, unchecked(seed + 1), keepPainted) }
                : result;
        }

        private static GridMapWfc.Result GenerateDungeon(
            ManaGridMapNode gridMap, GridMapWfcRules rules, Area area, int seed, bool keepPainted, bool connectedOnly, int rooms, int bigRooms)
        {
            // Each tile's model is a palette item, sized to the cells the rules say it covers.
            var items = new int[rules.Tiles.Count];
            for (var t = 0; t < rules.Tiles.Count; t++)
            {
                var tile = rules.Tiles[t];
                items[t] = -1;
                if (string.IsNullOrWhiteSpace(tile.Asset))
                {
                    continue;
                }

                items[t] = gridMap.AddPaletteItem(tile.Asset);
                var settings = gridMap.ItemSettings(items[t]);
                if (settings.Width != tile.Width || settings.Depth != tile.Depth || settings.Smooth != tile.Smooth)
                {
                    gridMap.SetItemSettings(items[t], settings with { Width = tile.Width, Depth = tile.Depth, Smooth = tile.Smooth });
                }
            }

            var keep = keepPainted ? Painted(gridMap, rules, items, area) : [];
            var result = GridMapWfc.Solve(rules, area.Width, area.Depth, seed, keep, connectedOnly, rooms: rooms, bigRooms: bigRooms);
            if (!result.Succeeded)
            {
                return result;
            }

            ClearArea(gridMap, area);
            if (WallsLayer(gridMap, create: false) is { } walls)
            {
                OnLayer(gridMap, walls, () => ClearArea(gridMap, area));
            }

            // Props laid on the old dungeon would stand in the new one's walls. Painted ones are kept.
            if (PropsLayer(gridMap, create: false) is { } oldProps)
            {
                OnLayer(gridMap, oldProps, () => ClearArea(gridMap, area, onlyGenerated: true));
            }

            foreach (var placement in result.Placements)
            {
                var item = items[placement.Tile];
                if (item >= 0)
                {
                    gridMap.SetGeneratedTile(new GridMapCell(area.X + placement.X, area.Floor, area.Z + placement.Z), new GridMapTile(item, placement.Orientation));
                }
            }

            if (result.BigRooms.Count > 0 && rules.BigRooms is { IsUsable: true } kit)
            {
                LayBigRooms(gridMap, kit, result.BigRooms, area);
            }

            return result;
        }

        private static void ClearArea(ManaGridMapNode gridMap, Area area, bool onlyGenerated = false)
        {
            for (var z = 0; z < area.Depth; z++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    var cell = new GridMapCell(area.X + x, area.Floor, area.Z + z);
                    if (!onlyGenerated || (gridMap.TryGetTileCovering(cell, out var anchor, out _) && gridMap.IsGenerated(anchor)))
                    {
                        gridMap.ClearTile(cell);
                    }
                }
            }
        }

        /// <summary>
        /// Furnishes a dungeon already in the map: reads what each cell of the area is and where its walls are
        /// (<see cref="ReadDungeon"/>), and lays the props rules' tiles on a props layer of their own, so
        /// re-rolling the furniture leaves the dungeon alone.
        /// </summary>
        public static GridMapWfc.Result GenerateProps(
            ManaGridMapNode gridMap, GridMapWfcRules props, GridMapWfcRules dungeon, Area area, int seed, bool keepPainted, int depth = 0)
        {
            var context = ReadDungeon(gridMap, dungeon, area);
            var slots = props.Slots is { Count: > 0 } listed ? listed : [[0.0f, 0.0f, 0.0f]];
            var placements = new List<GridMapWfcPlacement>();
            GridMapWfc.Result? last = null;
            for (var slot = 0; slot < slots.Count; slot++)
            {
                var spot = slots[slot] is { Length: >= 3 } at ? new Vector3(at[0], at[1], at[2]) : Vector3.Zero;
                var items = PropItems(gridMap, props, slot, spot);
                var layer = PropsLayer(gridMap, props, slot, create: true)!.Value;
                var keep = keepPainted ? OnLayer(gridMap, layer, () => Painted(gridMap, props, items, area)) : [];

                // A seed of its own for each spot, so the front of a table does not repeat the back.
                last = GridMapWfc.Solve(props, area.Width, area.Depth, unchecked(seed + slot * 7919), keep, connectedOnly: false, context: context);
                if (!last.Succeeded)
                {
                    return last;
                }

                OnLayer(gridMap, layer, () =>
                {
                    ClearArea(gridMap, area, onlyGenerated: keepPainted);
                    foreach (var placement in last.Placements)
                    {
                        if (items[placement.Tile] >= 0)
                        {
                            gridMap.SetGeneratedTile(
                                new GridMapCell(area.X + placement.X, area.Floor, area.Z + placement.Z),
                                new GridMapTile(items[placement.Tile], placement.Orientation));
                        }
                    }
                });

                placements.AddRange(last.Placements);
            }

            var result = new GridMapWfc.Result(placements, last?.Attempts ?? 0, null);

            // Props can lead to more props -- the furniture to what stands on the tables -- read off what these laid.
            return depth < 4 && FindRules(props.Then) is { IsProps: true } next
                ? result with { Props = GenerateProps(gridMap, next, props, area, unchecked(seed + 1), keepPainted, depth + 1) }
                : result;
        }

        /// <summary>
        /// Each props tile's palette item for one spot: kept at its own size against the grid rather than fitted to
        /// its cells -- a barrel is not a room -- and moved to the spot. A spot after the first is a "#" variant of
        /// the model, so the same candle can stand at the front of one table and the back of another.
        /// </summary>
        private static int[] PropItems(ManaGridMapNode gridMap, GridMapWfcRules props, int slot, Vector3 spot)
        {
            var items = new int[props.Tiles.Count];
            for (var t = 0; t < props.Tiles.Count; t++)
            {
                var tile = props.Tiles[t];
                items[t] = -1;
                if (string.IsNullOrWhiteSpace(tile.Asset))
                {
                    continue;
                }

                items[t] = gridMap.AddPaletteItem(slot == 0 ? tile.Asset : $"{tile.Asset}#{slot + 1}");
                var offset = tile.Offset is { Length: >= 3 } shift ? new Vector3(shift[0], shift[1], shift[2]) : Vector3.Zero;
                var settings = GridMapItemSettings.Default with
                {
                    Width = tile.Width,
                    Depth = tile.Depth,
                    Scale = tile.ModelCell > 0.0f ? gridMap.CellSize.X / tile.ModelCell : 0.0f,
                    Smooth = tile.Smooth,
                    Offset = offset + spot
                };
                if (gridMap.ItemSettings(items[t]) != settings)
                {
                    gridMap.SetItemSettings(items[t], settings);
                }
            }

            return items;
        }

        /// <summary>
        /// What each cell of the area is, read off the tiles in it: the dungeon rules' own tiles by their tags and
        /// side labels (a side labelled with the rules' edge label is a wall), and big rooms by their pieces --
        /// a floor is room, with a wall where the walls layer has one; a corner has walls on its two outsides.
        /// Cells with nothing in them are "empty"; tiles the rules do not know are "other".
        /// </summary>
        public static GridMapWfc.Cell[] ReadDungeon(ManaGridMapNode gridMap, GridMapWfcRules dungeon, Area area)
        {
            if (dungeon.IsProps)
            {
                return ReadProps(gridMap, dungeon, area);
            }

            var byAsset = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var t = 0; t < dungeon.Tiles.Count; t++)
            {
                if (dungeon.Tiles[t].Asset is { } asset)
                {
                    byAsset.TryAdd(asset, t);
                }
            }

            var kit = dungeon.BigRooms;
            var walls = WallsLayer(gridMap, create: false);
            var wallLabel = string.IsNullOrWhiteSpace(dungeon.Boundary) ? GridMapWfc.DefaultSocket : dungeon.Boundary.Trim();
            var cells = new GridMapWfc.Cell[area.Width * area.Depth];
            for (var z = 0; z < area.Depth; z++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    var at = new GridMapCell(area.X + x, area.Floor, area.Z + z);
                    cells[x + z * area.Width] = Read(at);
                }
            }

            MarkDoorways(cells, area.Width, area.Depth);
            return cells;

            GridMapWfc.Cell Read(GridMapCell at)
            {
                if (!gridMap.TryGetTileCovering(at, out var anchor, out var tile))
                {
                    return new GridMapWfc.Cell("empty", GridMapSides.None);
                }

                var asset = gridMap.Palette[tile.Item];
                if (kit != null && string.Equals(asset, kit.Corner, StringComparison.OrdinalIgnoreCase))
                {
                    return new GridMapWfc.Cell("room", GridMapAutoTile.Rotate(GridMapSides.East | GridMapSides.South, tile.Orientation - kit.CornerTurn));
                }

                if (kit != null && string.Equals(asset, kit.Floor, StringComparison.OrdinalIgnoreCase))
                {
                    var sides = GridMapSides.None;
                    if (walls is { } layer && gridMap.LayerCells(layer).TryGetValue(at, out var wall)
                        && string.Equals(gridMap.Palette[wall.Item], kit.Wall, StringComparison.OrdinalIgnoreCase))
                    {
                        sides = GridMapAutoTile.Rotate(GridMapSides.South, wall.Orientation - kit.WallTurn);
                    }

                    return new GridMapWfc.Cell("room", sides);
                }

                if (!byAsset.TryGetValue(asset, out var index))
                {
                    return new GridMapWfc.Cell("other", GridMapSides.None);
                }

                // Which modelled cell of the tile this is, then each of its outer sides' labels, turned into place.
                var rule = dungeon.Tiles[index];
                var (dx, dz) = (at.X - anchor.X, at.Z - anchor.Z);
                var (mx, mz) = (0, 0);
                for (var i = 0; i < rule.Width; i++)
                {
                    for (var j = 0; j < rule.Depth; j++)
                    {
                        if (GridMapWfc.TurnCell(i, j, rule.Width, rule.Depth, tile.Orientation) == (dx, dz))
                        {
                            (mx, mz) = (i, j);
                        }
                    }
                }

                var walled = GridMapSides.None;
                for (var side = 0; side < 4; side++)
                {
                    var modelled = ((side - tile.Orientation) % 4 + 4) % 4;
                    var (sx, sz) = (mx + GridMapAutoTile.Neighbours[modelled].X, mz + GridMapAutoTile.Neighbours[modelled].Z);
                    if (sx >= 0 && sz >= 0 && sx < rule.Width && sz < rule.Depth)
                    {
                        continue;
                    }

                    var label = rule.Socket(modelled, modelled is 0 or 2 ? mz : mx);
                    if (dungeon.Joins(label, wallLabel) && !dungeon.Joins(label, dungeon.Connects))
                    {
                        walled |= GridMapAutoTile.Neighbours[side].Side;
                    }
                }

                return new GridMapWfc.Cell(rule.EffectiveTag, walled);
            }
        }

        /// <summary>
        /// Keeps the way in and out of every room clear. A room cell open to something that is not room is a
        /// "door"; the room cells just inside a door are its "entry", and the corridor cells just outside it
        /// its "doorstep". Props standing on "room" leave all three alone, so no table blocks a doorway.
        /// </summary>
        private static void MarkDoorways(GridMapWfc.Cell[] cells, int width, int depth)
        {
            bool Open(int x, int z, int side, out int next)
            {
                var (nx, nz) = (x + GridMapAutoTile.Neighbours[side].X, z + GridMapAutoTile.Neighbours[side].Z);
                next = nx + nz * width;
                return nx >= 0 && nz >= 0 && nx < width && nz < depth
                    && !cells[x + z * width].Walls.HasFlag(GridMapAutoTile.Neighbours[side].Side)
                    && !cells[next].Walls.HasFlag(GridMapAutoTile.Neighbours[(side + 2) % 4].Side);
            }

            var doors = new List<int>();
            for (var cell = 0; cell < cells.Length; cell++)
            {
                if (cells[cell].Tag != "room")
                {
                    continue;
                }

                for (var side = 0; side < 4; side++)
                {
                    if (Open(cell % width, cell / width, side, out var next) && cells[next].Tag is not ("room" or "door"))
                    {
                        doors.Add(cell);
                        break;
                    }
                }
            }

            foreach (var door in doors)
            {
                cells[door] = cells[door] with { Tag = "door" };
            }

            foreach (var door in doors)
            {
                for (var side = 0; side < 4; side++)
                {
                    if (!Open(door % width, door / width, side, out var next))
                    {
                        continue;
                    }

                    cells[next] = cells[next].Tag switch
                    {
                        "room" => cells[next] with { Tag = "entry" },
                        "corridor" => cells[next] with { Tag = "doorstep" },
                        _ => cells[next]
                    };
                }
            }
        }

        /// <summary>
        /// What props passes laid, for one to stand on: each cell tagged by the props tile in it -- a table is
        /// "table" -- or "empty". There are no walls on a tabletop.
        /// </summary>
        private static GridMapWfc.Cell[] ReadProps(ManaGridMapNode gridMap, GridMapWfcRules props, Area area)
        {
            var byAsset = new Dictionary<string, GridMapWfcTile>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in props.Tiles.Where(tile => tile.Asset != null))
            {
                byAsset.TryAdd(tile.Asset!, tile);
            }

            var cells = new GridMapWfc.Cell[area.Width * area.Depth];
            var layer = PropsLayer(gridMap, props, 0, create: false);
            for (var z = 0; z < area.Depth; z++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    var at = new GridMapCell(area.X + x, area.Floor, area.Z + z);
                    var tag = "empty";
                    if (layer is { } found && gridMap.LayerCells(found).TryGetValue(at, out var laid)
                        && byAsset.TryGetValue(ManaGridMapNode.AssetOf(gridMap.Palette[laid.Item]), out var tile))
                    {
                        tag = string.IsNullOrWhiteSpace(tile.Tag) ? "prop" : tile.Tag.Trim();
                    }

                    cells[x + z * area.Width] = new GridMapWfc.Cell(tag, GridMapSides.None);
                }
            }

            return cells;
        }

        /// <summary>A rules file in Content/Generators/ by name, or a preset of that name, or null for none.</summary>
        public static GridMapWfcRules? FindRules(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var path = List().FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (path != null)
            {
                try
                {
                    return Load(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
                {
                    return null;
                }
            }

            return GridMapWfcPresets.All.FirstOrDefault(preset => string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Create?.Invoke();
        }

        /// <summary>The layer a dungeon's props go on: the one named for the active layer's props, made when asked.</summary>
        private static int? PropsLayer(ManaGridMapNode gridMap, bool create) => NamedLayer(gridMap, "props", create);

        /// <summary>The layer one spot of a props pass goes on: its rules' layer, numbered after the first spot.</summary>
        private static int? PropsLayer(ManaGridMapNode gridMap, GridMapWfcRules props, int slot, bool create)
        {
            var name = string.IsNullOrWhiteSpace(props.Layer) ? "props" : props.Layer.Trim();
            return NamedLayer(gridMap, slot == 0 ? name : $"{name} {slot + 1}", create);
        }

        /// <summary>
        /// Builds each big room from the kit's pieces: a floor in every cell, a corner piece in each corner, and on
        /// the walls layer a wall along every edge cell's outside but where a door is. A cell holds one tile a
        /// layer, and an edge cell needs a floor and a wall, so the walls go on a layer of their own.
        /// </summary>
        private static void LayBigRooms(ManaGridMapNode gridMap, GridMapWfcBigRooms kit, IReadOnlyList<GridMapWfc.Room> rooms, Area area)
        {
            int Item(string? asset, GridMapItemSettings settings)
            {
                if (string.IsNullOrWhiteSpace(asset))
                {
                    return -1;
                }

                var item = gridMap.AddPaletteItem(asset);
                if (gridMap.ItemSettings(item) != settings)
                {
                    gridMap.SetItemSettings(item, settings);
                }

                return item;
            }

            var floor = Item(kit.Floor, GridMapItemSettings.Default with { Smooth = kit.Smooth });
            var corner = Item(kit.Corner, GridMapItemSettings.Default with { Smooth = kit.Smooth });
            var wall = Item(kit.Wall, GridMapItemSettings.Default with { Offset = new Vector3(0.0f, 0.0f, kit.WallOffset) });
            var walls = wall >= 0 ? WallsLayer(gridMap, create: true) : null;

            foreach (var room in rooms)
            {
                for (var x = room.X; x < room.X + room.Width; x++)
                {
                    for (var z = room.Z; z < room.Z + room.Depth; z++)
                    {
                        // Which sides of the room this cell is on: its outsides.
                        var outside = GridMapSides.None;
                        outside |= x == room.X + room.Width - 1 ? GridMapSides.East : GridMapSides.None;
                        outside |= z == room.Z ? GridMapSides.North : GridMapSides.None;
                        outside |= x == room.X ? GridMapSides.West : GridMapSides.None;
                        outside |= z == room.Z + room.Depth - 1 ? GridMapSides.South : GridMapSides.None;
                        var cell = new GridMapCell(area.X + x, area.Floor, area.Z + z);

                        if (GridMapAutoTile.Count(outside) == 2 && corner >= 0)
                        {
                            gridMap.SetGeneratedTile(cell, new GridMapTile(corner, kit.CornerTurn + TurnFor(GridMapSides.East | GridMapSides.South, outside)));
                            continue;
                        }

                        if (floor >= 0)
                        {
                            gridMap.SetGeneratedTile(cell, new GridMapTile(floor, 0));
                        }

                        if (outside == GridMapSides.None || walls is not { } layer)
                        {
                            continue;
                        }

                        foreach (var (side, _, _) in GridMapAutoTile.Neighbours)
                        {
                            var index = Array.FindIndex(GridMapAutoTile.Neighbours, n => n.Side == side);
                            if (outside.HasFlag(side) && !room.Doors.Contains((x, z, index)))
                            {
                                OnLayer(gridMap, layer, () => gridMap.SetGeneratedTile(cell, new GridMapTile(wall, kit.WallTurn + TurnFor(GridMapSides.South, side))));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>The quarter turns that take sides as modelled to the sides wanted.</summary>
        private static int TurnFor(GridMapSides modelled, GridMapSides wanted)
        {
            for (var turn = 0; turn < ManaGridMapNode.OrientationCount; turn++)
            {
                if (GridMapAutoTile.Rotate(modelled, turn) == wanted)
                {
                    return turn;
                }
            }

            return 0;
        }

        /// <summary>The layer big rooms' walls go on: the one named for the active layer's walls, made when asked.</summary>
        private static int? WallsLayer(ManaGridMapNode gridMap, bool create) => NamedLayer(gridMap, "walls", create);

        private static int? NamedLayer(ManaGridMapNode gridMap, string suffix, bool create)
        {
            var name = $"{gridMap.LayerName(gridMap.ActiveLayer)} {suffix}";
            for (var layer = 0; layer < gridMap.LayerCount; layer++)
            {
                if (gridMap.LayerName(layer) == name)
                {
                    return layer;
                }
            }

            if (!create)
            {
                return null;
            }

            var active = gridMap.ActiveLayer;
            var made = gridMap.AddLayer(name);
            gridMap.ActiveLayer = active;
            return made;
        }

        private static void OnLayer(ManaGridMapNode gridMap, int layer, Action action) =>
            OnLayer(gridMap, layer, () =>
            {
                action();
                return 0;
            });

        private static T OnLayer<T>(ManaGridMapNode gridMap, int layer, Func<T> action)
        {
            var active = gridMap.ActiveLayer;
            gridMap.ActiveLayer = layer;
            try
            {
                return action();
            }
            finally
            {
                gridMap.ActiveLayer = active;
            }
        }

        /// <summary>Tiles of the rules already standing wholly inside the area, as cells the solve must keep.</summary>
        private static List<GridMapWfc.Fixed> Painted(ManaGridMapNode gridMap, GridMapWfcRules rules, int[] items, Area area)
        {
            var kept = new List<GridMapWfc.Fixed>();
            foreach (var (anchor, tile) in gridMap.Cells)
            {
                // Only what was painted: a generator's own tiles are what running it again replaces.
                var index = Array.IndexOf(items, tile.Item);
                if (index < 0 || anchor.Y != area.Floor || gridMap.IsGenerated(anchor))
                {
                    continue;
                }

                var footprint = gridMap.Footprint(tile);
                var x = anchor.X - area.X;
                var z = anchor.Z - area.Z;
                if (x >= 0 && z >= 0 && x + footprint.Width <= area.Width && z + footprint.Depth <= area.Depth)
                {
                    kept.Add(new GridMapWfc.Fixed(x, z, index, tile.Orientation));
                }
            }

            return kept;
        }

        /// <summary>Where rules files live: a Generators folder beside Levels.</summary>
        public static string ResolveFolder() => ManaProjectFolder.Resolve(FolderName);

        public static IReadOnlyList<string> List()
        {
            try
            {
                var folder = ResolveFolder();
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, "*" + Extension).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        public static GridMapWfcRules Load(string path) => GridMapWfcRules.FromJson(File.ReadAllText(path));

        /// <summary>Saves under the rules' name in the Generators folder, and returns the path written.</summary>
        public static string Save(GridMapWfcRules rules, string? path = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var name = string.Concat((string.IsNullOrWhiteSpace(rules.Name) ? "Rules" : rules.Name).Split(Path.GetInvalidFileNameChars()));
                path = Path.Combine(ResolveFolder(), name + Extension);
            }

            File.WriteAllText(path, rules.ToJson());
            return path;
        }
    }

    /// <summary>Starting rules for the packs in Assets/. Defaults only: every tile, weight and label can be changed.</summary>
    public static class GridMapWfcPresets
    {
        private const string Kit = "zip:kenney_modular-dungeon-kit_1.0.zip/Models/GLB format/";

        /// <summary>
        /// Kenney's modular dungeon kit: corridors, and square and wide rooms with a door in the middle of each
        /// side. The open sides are the ones each model is built open on, read off the models.
        /// </summary>
        public static GridMapWfcRules Dungeon() => new()
        {
            Name = "Dungeon",
            Boundary = "wall",
            Connects = "open",
            Then = "Dungeon Props",
            Tiles =
            [
                Tile("Empty", null, 1, 1, 6.0f, GridMapWfcRotations.None, "wall", "wall", "wall", "wall"),
                Tile("Corridor", "corridor.glb", 1, 1, 3.0f, GridMapWfcRotations.All, "open", "wall", "open", "wall"),
                Tile("Corner", "corridor-corner.glb", 1, 1, 1.5f, GridMapWfcRotations.All, "wall", "open", "open", "wall"),
                Tile("Junction", "corridor-junction.glb", 1, 1, 0.8f, GridMapWfcRotations.All, "open", "open", "open", "wall"),
                Tile("Crossing", "corridor-intersection.glb", 1, 1, 0.3f, GridMapWfcRotations.None, "open", "open", "open", "open"),
                Tile("Dead end", "corridor-end.glb", 1, 1, 0.4f, GridMapWfcRotations.All, "open", "wall", "wall", "wall"),
                Tile("Small room", "room-small.glb", 3, 3, 0.35f, GridMapWfcRotations.None, "wall,open,wall", "wall,open,wall", "wall,open,wall", "wall,open,wall"),
                Tile("Wide room", "room-wide.glb", 5, 3, 0.2f, GridMapWfcRotations.All,
                    "wall,open,wall", "wall,wall,open,wall,wall", "wall,open,wall", "wall,wall,open,wall,wall"),
                Tile("Large room", "room-large.glb", 5, 5, 0.12f, GridMapWfcRotations.None,
                    "wall,wall,open,wall,wall", "wall,wall,open,wall,wall", "wall,wall,open,wall,wall", "wall,wall,open,wall,wall")
            ],

            // The kit's template pieces: a plain floor tile, a straight wall built to stand on a floor tile's edge
            // (half a cell out from a cell's centre, its outside +Z), and a rounded corner with its own floor
            // whose outsides are +X and +Z.
            BigRooms = new GridMapWfcBigRooms
            {
                Floor = Kit + "template-floor.glb",
                Wall = Kit + "template-wall.glb",
                Corner = Kit + "template-corner.glb",
                WallOffset = 0.5f,
                MinSize = 5,
                MaxSize = 10,
                Smooth = 0.06f
            }
        };

        public static IReadOnlyList<(string Name, Func<GridMapWfcRules> Create)> All { get; } =
            [("Dungeon", Dungeon), ("Dungeon Props", DungeonProps), ("Table Clutter", TableClutter)];

        private const string Graveyard = "Assets/Models/Kenny/";

        /// <summary>
        /// Small things for the dungeon's tables, from Kenney's graveyard pieces and mini-dungeon kit: candles,
        /// bowls, plates, chalices, potions and lanterns. Read off the props layer -- only a table's cell is
        /// "table" -- and laid in two spots on each tabletop, raised to the top of Kenney's table.
        /// </summary>
        public static GridMapWfcRules TableClutter() => new()
        {
            Name = "Table Clutter",
            Kind = "props",
            Base = "Dungeon Props",
            Layer = "clutter",
            Boundary = string.Empty,
            Connects = string.Empty,
            Slots = [[-0.18f, 0.315f, 0.1f], [0.18f, 0.315f, -0.08f]],
            SideNames = new Dictionary<string, string> { ["free"] = "Anything" },
            Tiles =
            [
                Prop("Nothing", null, 0.8f, GridMapWfcRotations.None, null, "free", "free", "free", "free"),
                Clutter("Candle", Graveyard + "candle.glb", 1.0f),
                Clutter("Candles", Graveyard + "candle-multiple.glb", 0.4f),
                Clutter("Bowl", Graveyard + "detail-bowl.glb", 0.7f),
                Clutter("Plate", Graveyard + "detail-plate.glb", 0.9f),
                Clutter("Chalice", Graveyard + "detail-chalice.glb", 0.6f),
                Clutter("Lantern", Graveyard + "lantern-candle.glb", 0.3f),
                Clutter("Potion", Props + "potion.glb", 0.5f, modelCell: 2.5f)
            ]
        };

        private static GridMapWfcTile Tagged(GridMapWfcTile tile, string tag)
        {
            tile.Tag = tag;
            return tile;
        }

        private static GridMapWfcTile Clutter(string name, string asset, float weight, float modelCell = 1.0f) => new()
        {
            Name = name,
            Asset = asset,
            Size = [1, 1],
            Weight = weight,
            Rotations = GridMapWfcRotations.All,
            On = ["table"],
            ModelCell = modelCell,
            Sockets = new Dictionary<string, string[]> { ["e"] = ["free"], ["n"] = ["free"], ["w"] = ["free"], ["s"] = ["free"] }
        };

        private const string Props = "zip:kenney_mini-dungeon.zip/Models/GLB format/";

        /// <summary>
        /// Furniture for the dungeon, from Kenney's mini-dungeon kit, a cell to a model unit. Every side is
        /// "free" -- anything may stand beside it -- but for three kinds of side:
        /// <list type="bullet">
        /// <item>">back" has to face a wall: barrels, chests, pots and banners stand against one;</item>
        /// <item>a chair's front, ">seat", has to face a table, whose sides are "table";</item>
        /// <item>and "wall", what a wall shows a prop, joins "free", ">back" and "table" but not ">seat".</item>
        /// </list>
        /// The '>' keeps those two from joining themselves: two chairs do not face each other, nor two barrels
        /// stand back to back in the middle of a room. Most of a room is left empty, and nothing but stones, traps
        /// and banners goes in a corridor, so it stays walkable. A table's sides do not join "wall", so a table
        /// stands out in the room with space round it for chairs, never in a corner. Doorways stay clear: see
        /// MarkDoorways.
        /// </summary>
        public static GridMapWfcRules DungeonProps() => new()
        {
            Name = "Dungeon Props",
            Kind = "props",
            Base = "Dungeon",
            Layer = "props",
            Then = "Table Clutter",
            Boundary = "wall",
            Connects = string.Empty,
            Pairs =
            [
                ["free", "wall"],
                [">back", "wall"],
                ["table", "free"],
                [">seat", "table"]
            ],
            SideNames = new Dictionary<string, string>
            {
                ["free"] = "Anything",
                [">back"] = "Back to a wall",
                [">seat"] = "Faces a table",
                ["table"] = "Table"
            },
            Tiles =
            [
                Prop("Empty", null, 6.0f, GridMapWfcRotations.None, null, "free", "free", "free", "free"),
                Prop("Barrel", "barrel.glb", 1.4f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Chest", "chest.glb", 0.5f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Pot", "pot.glb", 0.6f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Banner", "banner.glb", 0.9f, GridMapWfcRotations.All, ["room", "corridor"], "free", ">back", "free", "free", offset: [0.0f, 0.0f, 0.14f]),
                Tagged(Prop("Table", "table.glb", 0.7f, GridMapWfcRotations.Half, ["room"], "table", "table", "table", "table"), "table"),
                Prop("Chair", "chair.glb", 2.5f, GridMapWfcRotations.All, ["room"], "free", "free", "free", ">seat"),
                Prop("Column", "column.glb", 0.25f, GridMapWfcRotations.None, ["room"], "free", "free", "free", "free"),
                Prop("Stones", "stones.glb", 0.25f, GridMapWfcRotations.All, ["room", "corridor"], "free", "free", "free", "free"),
                Prop("Debris", Graveyard + "debris.glb", 0.5f, GridMapWfcRotations.All, ["room", "corridor"], "free", "free", "free", "free", external: true),
                Prop("Wood debris", Graveyard + "debris-wood.glb", 0.4f, GridMapWfcRotations.All, ["room", "corridor"], "free", "free", "free", "free", external: true),
                Prop("Lantern", Graveyard + "lantern-candle.glb", 0.3f, GridMapWfcRotations.None, ["room"], "free", ">back", "free", "free", external: true),
                Prop("Trap", "trap.glb", 0.15f, GridMapWfcRotations.None, ["corridor"], "free", "free", "free", "free")
            ]
        };

        private static GridMapWfcTile Prop(string name, string? model, float weight, GridMapWfcRotations rotations, string[]? on,
            string east, string north, string west, string south, float[]? offset = null, bool external = false) => new()
        {
            Name = name,
            Asset = model == null ? null : external ? model : Props + model,
            Size = [1, 1],
            Weight = weight,
            Rotations = rotations,
            On = on,
            ModelCell = model == null ? 0.0f : 1.0f,
            Offset = offset,
            Sockets = new Dictionary<string, string[]>
            {
                ["e"] = [east],
                ["n"] = [north],
                ["w"] = [west],
                ["s"] = [south]
            }
        };

        // The kit's floors are scattered with cracks and rubble a twentieth of a unit high, flattened out of
        // the colliders so they do not catch a walking character. The ledge along the walls stays a ledge.
        private static GridMapWfcTile Tile(string name, string? model, int width, int depth, float weight, GridMapWfcRotations rotations,
            string east, string north, string west, string south) => new()
        {
            Name = name,
            Asset = model == null ? null : Kit + model,
            Size = [width, depth],
            Weight = weight,
            Smooth = model == null ? 0.0f : 0.06f,
            Rotations = rotations,
            Sockets = new Dictionary<string, string[]>
            {
                ["e"] = Split(east),
                ["n"] = Split(north),
                ["w"] = Split(west),
                ["s"] = Split(south)
            }
        };

        public static string[] Split(string labels) =>
            labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
