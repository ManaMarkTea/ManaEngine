using ManaVoxel.Code.Assets;

namespace ManaVoxel.Code.GridMaps
{
    /// <summary>
    /// Runs <see cref="GridMapWfc"/> into a grid map: an area of the active layer on one floor, laid with the
    /// rules' tiles as ordinary palette tiles. What the rules' tiles already cover in the area can be kept, so
    /// a room painted by hand is built round rather than over.
    /// </summary>
    public static class GridMapWfcGenerator
    {
        public const string FolderName = "Generators";

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
            ManaGridMapNode gridMap, GridMapWfcRules props, GridMapWfcRules dungeon, Area area, int seed, bool keepPainted)
        {
            var context = ReadDungeon(gridMap, dungeon, area);
            var items = new int[props.Tiles.Count];
            for (var t = 0; t < props.Tiles.Count; t++)
            {
                var tile = props.Tiles[t];
                items[t] = -1;
                if (string.IsNullOrWhiteSpace(tile.Asset))
                {
                    continue;
                }

                // Kept at its own size against the grid rather than fitted to its cells: a barrel is not a room.
                items[t] = gridMap.AddPaletteItem(tile.Asset);
                var settings = GridMapItemSettings.Default with
                {
                    Width = tile.Width,
                    Depth = tile.Depth,
                    Scale = tile.ModelCell > 0.0f ? gridMap.CellSize.X / tile.ModelCell : 0.0f,
                    Smooth = tile.Smooth,
                    Offset = tile.Offset is { Length: >= 3 } offset ? new Vector3(offset[0], offset[1], offset[2]) : Vector3.Zero
                };
                if (gridMap.ItemSettings(items[t]) != settings)
                {
                    gridMap.SetItemSettings(items[t], settings);
                }
            }

            var layer = PropsLayer(gridMap, create: true)!.Value;
            var keep = keepPainted ? OnLayer(gridMap, layer, () => Painted(gridMap, props, items, area)) : [];
            var result = GridMapWfc.Solve(props, area.Width, area.Depth, seed, keep, connectedOnly: false, context: context);
            if (!result.Succeeded)
            {
                return result;
            }

            OnLayer(gridMap, layer, () =>
            {
                ClearArea(gridMap, area, onlyGenerated: keepPainted);
                foreach (var placement in result.Placements)
                {
                    if (items[placement.Tile] >= 0)
                    {
                        gridMap.SetGeneratedTile(
                            new GridMapCell(area.X + placement.X, area.Floor, area.Z + placement.Z),
                            new GridMapTile(items[placement.Tile], placement.Orientation));
                    }
                }
            });

            return result;
        }

        /// <summary>
        /// What each cell of the area is, read off the tiles in it: the dungeon rules' own tiles by their tags and
        /// side labels (a side labelled with the rules' edge label is a wall), and big rooms by their pieces --
        /// a floor is room, with a wall where the walls layer has one; a corner has walls on its two outsides.
        /// Cells with nothing in them are "empty"; tiles the rules do not know are "other".
        /// </summary>
        public static GridMapWfc.Cell[] ReadDungeon(ManaGridMapNode gridMap, GridMapWfcRules dungeon, Area area)
        {
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

        /// <summary>A rules file in Generators/ by name, or a preset of that name, or null for none.</summary>
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
            [("Dungeon", Dungeon), ("Dungeon Props", DungeonProps)];

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
        /// and banners goes in a corridor, so it stays walkable.
        /// </summary>
        public static GridMapWfcRules DungeonProps() => new()
        {
            Name = "Dungeon Props",
            Kind = "props",
            Base = "Dungeon",
            Boundary = "wall",
            Connects = string.Empty,
            Pairs =
            [
                ["free", "wall"],
                [">back", "wall"],
                ["table", "wall"],
                ["table", "free"],
                [">seat", "table"]
            ],
            Tiles =
            [
                Prop("Empty", null, 14.0f, GridMapWfcRotations.None, null, "free", "free", "free", "free"),
                Prop("Barrel", "barrel.glb", 1.4f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Chest", "chest.glb", 0.5f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Pot", "pot.glb", 0.6f, GridMapWfcRotations.All, ["room"], "free", ">back", "free", "free"),
                Prop("Banner", "banner.glb", 0.9f, GridMapWfcRotations.All, ["room", "corridor"], "free", ">back", "free", "free", offset: [0.0f, 0.0f, 0.14f]),
                Prop("Table", "table.glb", 0.7f, GridMapWfcRotations.Half, ["room"], "table", "table", "table", "table"),
                Prop("Chair", "chair.glb", 2.5f, GridMapWfcRotations.All, ["room"], "free", "free", "free", ">seat"),
                Prop("Column", "column.glb", 0.25f, GridMapWfcRotations.None, ["room"], "free", "free", "free", "free"),
                Prop("Stones", "stones.glb", 0.25f, GridMapWfcRotations.All, ["room", "corridor"], "free", "free", "free", "free"),
                Prop("Trap", "trap.glb", 0.15f, GridMapWfcRotations.None, ["corridor"], "free", "free", "free", "free")
            ]
        };

        private static GridMapWfcTile Prop(string name, string? model, float weight, GridMapWfcRotations rotations, string[]? on,
            string east, string north, string west, string south, float[]? offset = null) => new()
        {
            Name = name,
            Asset = model == null ? null : Props + model,
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
