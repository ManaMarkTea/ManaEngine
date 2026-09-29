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

        /// <summary>Generates into the map, replacing the area. The caller records the undo step.</summary>
        public static GridMapWfc.Result Generate(ManaGridMapNode gridMap, GridMapWfcRules rules, Area area, int seed, bool keepPainted, bool connectedOnly)
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
                if (settings.Width != tile.Width || settings.Depth != tile.Depth)
                {
                    gridMap.SetItemSettings(items[t], settings with { Width = tile.Width, Depth = tile.Depth });
                }
            }

            var keep = keepPainted ? Painted(gridMap, rules, items, area) : [];
            var result = GridMapWfc.Solve(rules, area.Width, area.Depth, seed, keep, connectedOnly);
            if (!result.Succeeded)
            {
                return result;
            }

            for (var z = 0; z < area.Depth; z++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    gridMap.ClearTile(new GridMapCell(area.X + x, area.Floor, area.Z + z));
                }
            }

            foreach (var placement in result.Placements)
            {
                var item = items[placement.Tile];
                if (item >= 0)
                {
                    gridMap.SetTile(new GridMapCell(area.X + placement.X, area.Floor, area.Z + placement.Z), new GridMapTile(item, placement.Orientation));
                }
            }

            return result;
        }

        /// <summary>Tiles of the rules already standing wholly inside the area, as cells the solve must keep.</summary>
        private static List<GridMapWfc.Fixed> Painted(ManaGridMapNode gridMap, GridMapWfcRules rules, int[] items, Area area)
        {
            var kept = new List<GridMapWfc.Fixed>();
            foreach (var (anchor, tile) in gridMap.Cells)
            {
                var index = Array.IndexOf(items, tile.Item);
                if (index < 0 || anchor.Y != area.Floor)
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
            ]
        };

        public static IReadOnlyList<(string Name, Func<GridMapWfcRules> Create)> All { get; } = [("Dungeon", Dungeon)];

        private static GridMapWfcTile Tile(string name, string? model, int width, int depth, float weight, GridMapWfcRotations rotations,
            string east, string north, string west, string south) => new()
        {
            Name = name,
            Asset = model == null ? null : Kit + model,
            Size = [width, depth],
            Weight = weight,
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
