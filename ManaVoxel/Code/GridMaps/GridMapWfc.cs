using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManaVoxel.Code.GridMaps
{
    /// <summary>Which quarter turns a generator tile may be used at.</summary>
    public enum GridMapWfcRotations
    {
        /// <summary>All four.</summary>
        All,

        /// <summary>As modelled only.</summary>
        None,

        /// <summary>As modelled and half way round -- a piece that reads the same from either end.</summary>
        Half
    }

    /// <summary>
    /// One piece a generator lays: a model, the cells it covers, how often it is picked, and a socket label on
    /// every cell of every side. Two pieces may stand side by side where the labels facing each other are the
    /// same -- "open" to "open" joins a corridor, "wall" to "wall" puts two backs together.
    /// </summary>
    public sealed class GridMapWfcTile
    {
        public string Name { get; set; } = "Tile";

        /// <summary>The model, as a palette asset. Null lays nothing -- the empty space between rooms.</summary>
        public string? Asset { get; set; }

        /// <summary>Cells covered along X and Z, as modelled.</summary>
        public int[] Size { get; set; } = [1, 1];

        public float Weight { get; set; } = 1.0f;

        /// <summary>Collider flat below this height, in the model's units -- see GridMapItemSettings.Smooth.</summary>
        public float Smooth { get; set; }

        /// <summary>
        /// What a cell of this tile is, for a later pass placing things on it: "room", "corridor", "empty"...
        /// Unset, it is "empty" for a tile with no model, "room" for one covering several cells, and
        /// "corridor" for the rest.
        /// </summary>
        public string? Tag { get; set; }

        /// <summary>For a prop pass: the tags of the cells this tile may stand in. Unset, it may stand anywhere.</summary>
        public string[]? On { get; set; }

        /// <summary>
        /// The size of one cell in the model's own units, so a prop keeps its size against the grid -- a barrel
        /// half a cell high stays half a cell high. Zero fits the model to its cells, as a dungeon piece is.
        /// </summary>
        public float ModelCell { get; set; }

        /// <summary>A shift of the model within its tile, in cells -- a banner pulled in off the wall behind it.</summary>
        public float[]? Offset { get; set; }

        [JsonIgnore]
        public string EffectiveTag => !string.IsNullOrWhiteSpace(Tag) ? Tag.Trim()
            : Asset == null ? "empty"
            : Width * Depth > 1 ? "room"
            : "corridor";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GridMapWfcRotations Rotations { get; set; } = GridMapWfcRotations.All;

        /// <summary>
        /// Labels by side, "e" (+X), "n" (-Z), "w" (-X), "s" (+Z): one per cell along that side, in order of
        /// rising Z for e and w and rising X for n and s. A side given one label uses it for every cell.
        /// </summary>
        public Dictionary<string, string[]> Sockets { get; set; } = new();

        [JsonIgnore]
        public int Width => Math.Clamp(Size.Length > 0 ? Size[0] : 1, 1, 16);

        [JsonIgnore]
        public int Depth => Math.Clamp(Size.Length > 1 ? Size[1] : 1, 1, 16);

        /// <summary>The label on one cell of one side (0 E, 1 N, 2 W, 3 S), as modelled.</summary>
        public string Socket(int side, int index)
        {
            var labels = Sockets.TryGetValue(GridMapWfc.SideKeys[side], out var found) ? found : null;
            if (labels == null || labels.Length == 0)
            {
                return GridMapWfc.DefaultSocket;
            }

            return (labels.Length == 1 ? labels[0] : labels[Math.Clamp(index, 0, labels.Length - 1)]).Trim();
        }

        public GridMapWfcTile Clone() => new()
        {
            Name = Name,
            Asset = Asset,
            Size = Size.ToArray(),
            Weight = Weight,
            Smooth = Smooth,
            Tag = Tag,
            On = On?.ToArray(),
            ModelCell = ModelCell,
            Offset = Offset?.ToArray(),
            Rotations = Rotations,
            Sockets = Sockets.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())
        };
    }

    /// <summary>
    /// Rooms of any size, built from pieces rather than modelled whole: a floor for every cell, a wall along
    /// each edge cell's outer side, a corner piece in each corner, and a gap in the wall for each door. The
    /// generator lays them before the collapse, which then only has to reach their doors.
    /// </summary>
    public sealed class GridMapWfcBigRooms
    {
        /// <summary>A floor tile, laid in every cell but the corners.</summary>
        public string? Floor { get; set; }

        /// <summary>A straight wall, laid in each edge cell on a second layer so the floor stays under it.</summary>
        public string? Wall { get; set; }

        /// <summary>A corner piece, with its own floor, laid in each corner cell.</summary>
        public string? Corner { get; set; }

        /// <summary>Quarter turns that face the wall's outside to +Z (south), as the corner's outsides are +X and +Z.</summary>
        public int WallTurn { get; set; }

        public int CornerTurn { get; set; }

        /// <summary>How far the wall is moved towards the outside, in cells -- a kit wall is built to stand on the line between cells.</summary>
        public float WallOffset { get; set; } = 0.5f;

        /// <summary>Cells across, and deep, a room can be: at least 3, so every side has a middle for a door.</summary>
        public int MinSize { get; set; } = 5;

        public int MaxSize { get; set; } = 9;

        /// <summary>Collider flat below this height, as for a tile; see GridMapItemSettings.Smooth.</summary>
        public float Smooth { get; set; }

        [JsonIgnore]
        public bool IsUsable => !string.IsNullOrWhiteSpace(Floor);
    }

    /// <summary>A generator's rules, as saved to a .manawfc file.</summary>
    public sealed class GridMapWfcRules
    {
        public string Format { get; set; } = "mana-wfc";

        public int Version { get; set; } = 1;

        public string Name { get; set; } = "Rules";

        /// <summary>
        /// The label every side facing out of the generated area must have -- "wall" closes a dungeon in.
        /// Empty lets anything face out.
        /// </summary>
        public string Boundary { get; set; } = "wall";

        /// <summary>A label that joins two cells of the same dungeon, for keeping only the largest joined part.</summary>
        public string Connects { get; set; } = "open";

        /// <summary>
        /// "props" for rules that furnish a finished dungeon rather than lay one: each cell is offered only
        /// what may stand on what is there, and a wall between two cells stands between their props.
        /// </summary>
        public string? Kind { get; set; }

        [JsonIgnore]
        public bool IsProps => string.Equals(Kind, "props", StringComparison.OrdinalIgnoreCase);

        /// <summary>A props rules file, by name, to run over what these rules lay -- the dungeon's furniture.</summary>
        public string? Then { get; set; }

        /// <summary>For props: the rules file, by name, the dungeon underneath was laid with, to read it by.</summary>
        public string? Base { get; set; }

        /// <summary>For props: the layer they go on, after the dungeon's own -- "props" makes "Base props".</summary>
        public string? Layer { get; set; }

        /// <summary>
        /// For props: places within a cell, in cells, to fill one at a time -- each on a layer of its own, since a
        /// layer holds one tile a cell. Two spots on a tabletop are two things on every table. Unset is one, the
        /// cell's centre.
        /// </summary>
        public List<float[]>? Slots { get; set; }

        /// <summary>
        /// Labels that join each other as well as themselves, both ways: ["seat", "table"] lets a chair's front
        /// face a table, ["free", "wall"] lets a prop stand against a wall. Everything else joins only its equal.
        /// </summary>
        public List<string[]> Pairs { get; set; } = new();

        /// <summary>
        /// Plain names for the side labels a person picks between -- "&gt;back" is "Back to a wall" -- so the
        /// editor offers a side a list of what it can be rather than a label to type and pairs to know.
        /// </summary>
        public Dictionary<string, string> SideNames { get; set; } = new();

        /// <summary>
        /// Whether two labels may face each other: the same, or listed together in <see cref="Pairs"/>. A label
        /// starting with '>' never joins itself -- a chair's ">seat" faces a table, not another chair.
        /// </summary>
        public bool Joins(string a, string b) =>
            (string.Equals(a, b, StringComparison.Ordinal) && !a.StartsWith('>'))
            || Pairs.Any(pair => pair.Length >= 2
                && ((pair[0] == a && pair[1] == b) || (pair[0] == b && pair[1] == a)));

        public List<GridMapWfcTile> Tiles { get; set; } = new();

        /// <summary>Pieces big rooms are built from, when the rules have them.</summary>
        public GridMapWfcBigRooms? BigRooms { get; set; }

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string ToJson() => ManaCore.Serialization.ManaJsonFormat.Indent(JsonSerializer.Serialize(this, Json));

        public static GridMapWfcRules FromJson(string json) =>
            JsonSerializer.Deserialize<GridMapWfcRules>(json, Json) ?? throw new InvalidDataException("Not a generator rules file.");

        public GridMapWfcRules Clone() => FromJson(ToJson());
    }

    /// <summary>One tile the generator decided on: which rules tile, turned how, anchored at which cell of the area.</summary>
    public readonly record struct GridMapWfcPlacement(int Tile, int Orientation, int X, int Z);

    /// <summary>
    /// Wave function collapse over a grid of cells. Every tile, at every turn it allows, is cut into one part
    /// per cell it covers; parts of the same tile join only to each other, through labels no other tile has,
    /// so a big room comes out whole or not at all. Then the usual: collapse the cell with the fewest
    /// weighted choices left, spread what that rules out to its neighbours, and start over from a new seed
    /// when a cell runs out of choices.
    /// </summary>
    public static class GridMapWfc
    {
        public static readonly string[] SideKeys = ["e", "n", "w", "s"];

        public const string DefaultSocket = "wall";

        /// <summary>
        /// What a prop pass knows of a cell of the finished dungeon: its tag ("room", "corridor", "empty") and
        /// the sides with a wall -- which a prop's side has to be able to face, and which no prop joins across.
        /// </summary>
        public readonly record struct Cell(string Tag, GridMapSides Walls);

        private const int MaxSize = 96;

        /// <summary>Finished layouts compared when keeping to one piece; see <see cref="Solve"/>.</summary>
        private const int Candidates = 6;

        /// <summary>Steps across each side, in turn order, as for <see cref="GridMapAutoTile.Neighbours"/>.</summary>
        private static readonly (int X, int Z)[] Steps = [(1, 0), (0, -1), (-1, 0), (0, 1)];

        /// <summary>A big room laid: its first cell, its size, and its door cells with the side each opens on.</summary>
        public sealed record Room(int X, int Z, int Width, int Depth, IReadOnlyList<(int X, int Z, int Side)> Doors);

        public sealed record Result(IReadOnlyList<GridMapWfcPlacement> Placements, int Attempts, string? Error)
        {
            public IReadOnlyList<Room> BigRooms { get; init; } = [];

            /// <summary>The props pass run after, when the rules name one; null when there was none.</summary>
            public Result? Props { get; init; }

            public bool Succeeded => Error == null;
        }

        /// <summary>A cell the area already has, as a tile and turn, which the solve must keep.</summary>
        public readonly record struct Fixed(int X, int Z, int Tile, int Orientation);

        /// <summary>
        /// Fills a <paramref name="width"/> by <paramref name="depth"/> area. <paramref name="keep"/> anchors
        /// tiles already in place; <paramref name="connectedOnly"/> keeps only the largest joined part, so a
        /// dungeon is one dungeon and not stray corridors.
        /// </summary>
        public static Result Solve(
            GridMapWfcRules rules,
            int width,
            int depth,
            int seed,
            IReadOnlyList<Fixed>? keep = null,
            bool connectedOnly = true,
            int maxAttempts = 40,
            int rooms = 0,
            int bigRooms = 0,
            IReadOnlyList<Cell>? context = null)
        {
            if (width < 1 || depth < 1 || width > MaxSize || depth > MaxSize)
            {
                return new Result([], 0, $"The area has to be 1 to {MaxSize} cells each way.");
            }

            var model = Model.Build(rules);
            if (model.Parts.Count == 0)
            {
                return new Result([], 0, "The rules have no tiles.");
            }

            // Kept to one piece, a layout loses whatever its corridors did not happen to join up -- rooms laid
            // first most of all. So a few finished layouts are tried and the one that keeps the most rooms, then
            // the most tiles, wins. Solves take milliseconds; a dungeon with its rooms cut away is the cost here.
            var wanted = connectedOnly ? Math.Clamp(Candidates, 1, maxAttempts) : 1;
            List<GridMapWfcPlacement>? best = null;
            List<Room> bestRooms = [];
            var bestScore = (-1, -1, -1);
            var attempts = 0;
            for (var attempt = 0; attempt < maxAttempts && wanted > 0; attempt++)
            {
                attempts = attempt + 1;

                // Not HashCode.Combine: that is seeded afresh every run, and a seed has to lay the same dungeon
                // tomorrow as today.
                var random = new Random(unchecked(seed * 7919 + attempt * 104729));
                var big = PlaceBigRooms(rules, width, depth, bigRooms, keep ?? [], random);
                var pinned = (keep ?? []).Concat(PlaceRooms(rules, width, depth, rooms, keep ?? [], random, big)).ToList();
                var blocked = BlockedCells(rules, big, width, depth);
                var solver = new Solver(model, rules, width, depth, random, blocked, context);
                if (solver.Run(pinned) is not { } parts)
                {
                    continue;
                }

                wanted--;
                var (placed, keptRooms) = Place(model, rules, parts, width, depth, connectedOnly, blocked, big);
                var score = (keptRooms.Count, placed.Count(p => rules.Tiles[p.Tile].Width * rules.Tiles[p.Tile].Depth > 1), placed.Count);
                if (score.CompareTo(bestScore) > 0)
                {
                    best = placed;
                    bestRooms = keptRooms;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                return new Result(best, attempts, null) { BigRooms = bestRooms };
            }

            return new Result([], maxAttempts, "No arrangement fits these rules here. Try another seed, a bigger area, or rules with more ways to join.");
        }

        /// <summary>
        /// Rooms laid before the collapse: the rules' tiles covering more than one cell, at random places and
        /// turns, a cell clear of the area's edge and of each other so a corridor can reach every door. Left
        /// to the collapse alone a big tile rarely wins -- every one of its cells has to agree at once -- and a
        /// dungeon comes out as corridors. The collapse then builds the rest round them.
        /// </summary>
        private static List<Fixed> PlaceRooms(
            GridMapWfcRules rules, int width, int depth, int count, IReadOnlyList<Fixed> keep, Random random, IReadOnlyList<Room> bigRooms)
        {
            var rooms = new List<Fixed>();
            var candidates = Enumerable.Range(0, rules.Tiles.Count)
                .Where(t => rules.Tiles[t].Asset != null && rules.Tiles[t].Width * rules.Tiles[t].Depth > 1 && rules.Tiles[t].Weight > 0.0f)
                .ToList();
            if (count <= 0 || candidates.Count == 0)
            {
                return rooms;
            }

            // What is taken, with the margin round it: kept tiles first, then each room as it goes in.
            var taken = new bool[width, depth];
            foreach (var fix in keep)
            {
                var tile = rules.Tiles[fix.Tile];
                var (w, d) = fix.Orientation % 2 == 0 ? (tile.Width, tile.Depth) : (tile.Depth, tile.Width);
                Mark(fix.X, fix.Z, w, d, margin: 0);
            }

            foreach (var room in bigRooms)
            {
                Mark(room.X, room.Z, room.Width, room.Depth, margin: 1);
            }

            var totalWeight = candidates.Sum(t => rules.Tiles[t].Weight);
            for (var tries = 0; rooms.Count < count && tries < count * 40; tries++)
            {
                // By weight among the rooms, so a rules file can still say large rooms are rarer.
                var roll = random.NextDouble() * totalWeight;
                var pick = candidates[^1];
                foreach (var t in candidates)
                {
                    roll -= rules.Tiles[t].Weight;
                    if (roll <= 0.0)
                    {
                        pick = t;
                        break;
                    }
                }

                var turns = Turns(rules.Tiles[pick].Rotations).ToList();
                var turn = turns[random.Next(turns.Count)];
                var (tw, td) = turn % 2 == 0 ? (rules.Tiles[pick].Width, rules.Tiles[pick].Depth) : (rules.Tiles[pick].Depth, rules.Tiles[pick].Width);
                if (tw + 2 > width || td + 2 > depth)
                {
                    continue;
                }

                var x = 1 + random.Next(width - tw - 1);
                var z = 1 + random.Next(depth - td - 1);
                if (Free(x - 1, z - 1, tw + 2, td + 2))
                {
                    rooms.Add(new Fixed(x, z, pick, turn));
                    Mark(x, z, tw, td, margin: 1);
                }
            }

            return rooms;

            bool Free(int x, int z, int w, int d)
            {
                for (var i = x; i < x + w; i++)
                {
                    for (var j = z; j < z + d; j++)
                    {
                        if (i >= 0 && j >= 0 && i < width && j < depth && taken[i, j])
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            void Mark(int x, int z, int w, int d, int margin)
            {
                for (var i = x - margin; i < x + w + margin; i++)
                {
                    for (var j = z - margin; j < z + d + margin; j++)
                    {
                        if (i >= 0 && j >= 0 && i < width && j < depth)
                        {
                            taken[i, j] = true;
                        }
                    }
                }
            }
        }

        private static (List<GridMapWfcPlacement> Placements, List<Room> Rooms) Place(
            Model model, GridMapWfcRules rules, int[] parts, int width, int depth, bool connectedOnly, string[]?[] blocked, IReadOnlyList<Room> bigRooms)
        {
            var keep = connectedOnly ? LargestJoined(model, rules, parts, width, depth, blocked) : null;
            var placements = new List<GridMapWfcPlacement>();
            for (var z = 0; z < depth; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (parts[x + z * width] < 0)
                    {
                        continue;
                    }

                    var part = model.Parts[parts[x + z * width]];
                    if (part.X == 0 && part.Z == 0 && rules.Tiles[part.Tile].Asset != null && (keep == null || keep[x + z * width]))
                    {
                        placements.Add(new GridMapWfcPlacement(part.Tile, part.Orientation, x, z));
                    }
                }
            }

            var rooms = bigRooms.Where(room => keep == null || keep[room.X + room.Z * width]).ToList();
            return (placements, rooms);
        }

        /// <summary>
        /// Big rooms at random sizes and places, a cell clear of the area's edge and of each other, with a door in
        /// the middle stretch of each side. Kept tiles are left room round too.
        /// </summary>
        private static List<Room> PlaceBigRooms(GridMapWfcRules rules, int width, int depth, int count, IReadOnlyList<Fixed> keep, Random random)
        {
            var rooms = new List<Room>();
            if (count <= 0 || rules.BigRooms is not { IsUsable: true } kit)
            {
                return rooms;
            }

            var min = Math.Max(3, Math.Min(kit.MinSize, kit.MaxSize));
            var max = Math.Max(min, kit.MaxSize);
            var taken = new bool[width, depth];
            foreach (var fix in keep)
            {
                var tile = rules.Tiles[fix.Tile];
                var (w, d) = fix.Orientation % 2 == 0 ? (tile.Width, tile.Depth) : (tile.Depth, tile.Width);
                for (var i = Math.Max(0, fix.X - 1); i < Math.Min(width, fix.X + w + 1); i++)
                {
                    for (var j = Math.Max(0, fix.Z - 1); j < Math.Min(depth, fix.Z + d + 1); j++)
                    {
                        taken[i, j] = true;
                    }
                }
            }

            for (var tries = 0; rooms.Count < count && tries < count * 40; tries++)
            {
                var w = random.Next(min, max + 1);
                var d = random.Next(min, max + 1);
                if (w + 2 > width || d + 2 > depth)
                {
                    continue;
                }

                var x = 1 + random.Next(width - w - 1);
                var z = 1 + random.Next(depth - d - 1);
                var free = true;
                for (var i = x - 1; i <= x + w && free; i++)
                {
                    for (var j = z - 1; j <= z + d && free; j++)
                    {
                        free = !taken[i, j];
                    }
                }

                if (!free)
                {
                    continue;
                }

                for (var i = x - 1; i <= x + w; i++)
                {
                    for (var j = z - 1; j <= z + d; j++)
                    {
                        taken[i, j] = true;
                    }
                }

                // One door a side, somewhere along it but never in a corner, which has no straight wall to open.
                var doors = new List<(int X, int Z, int Side)>
                {
                    (x + w - 1, z + random.Next(1, d - 1), 0),
                    (x + random.Next(1, w - 1), z, 1),
                    (x, z + random.Next(1, d - 1), 2),
                    (x + random.Next(1, w - 1), z + d - 1, 3)
                };
                rooms.Add(new Room(x, z, w, d, doors));
            }

            return rooms;
        }

        /// <summary>
        /// The big rooms' cells, each with the labels its sides show: its own room inside (a label nothing else
        /// has), wall on the outside, and the joining label at a door -- so the collapse has to meet every door
        /// with a corridor and may put its back to the walls.
        /// </summary>
        private static string[]?[] BlockedCells(GridMapWfcRules rules, IReadOnlyList<Room> rooms, int width, int depth)
        {
            var blocked = new string[]?[width * depth];
            var join = string.IsNullOrWhiteSpace(rules.Connects) ? "open" : rules.Connects.Trim();
            var wall = string.IsNullOrWhiteSpace(rules.Boundary) ? DefaultSocket : rules.Boundary.Trim();
            for (var r = 0; r < rooms.Count; r++)
            {
                var room = rooms[r];
                for (var x = room.X; x < room.X + room.Width; x++)
                {
                    for (var z = room.Z; z < room.Z + room.Depth; z++)
                    {
                        var sockets = new string[4];
                        for (var side = 0; side < 4; side++)
                        {
                            var nx = x + Steps[side].X;
                            var nz = z + Steps[side].Z;
                            var inside = nx >= room.X && nx < room.X + room.Width && nz >= room.Z && nz < room.Z + room.Depth;
                            sockets[side] = inside ? $"#room{r}"
                                : room.Doors.Contains((x, z, side)) ? join
                                : wall;
                        }

                        blocked[x + z * width] = sockets;
                    }
                }
            }

            return blocked;
        }

        /// <summary>The cells of the biggest group joined through the connecting label or through a tile's own parts.</summary>
        private static bool[]? LargestJoined(Model model, GridMapWfcRules rules, int[] parts, int width, int depth, string[]?[] blocked)
        {
            var join = rules.Connects?.Trim();
            if (string.IsNullOrEmpty(join))
            {
                return null;
            }

            var group = new int[width * depth];
            Array.Fill(group, -1);
            var sizes = new List<int>();
            var queue = new Queue<int>();
            for (var start = 0; start < group.Length; start++)
            {
                if (group[start] >= 0 || (blocked[start] == null && rules.Tiles[model.Parts[parts[start]].Tile].Asset == null))
                {
                    continue;
                }

                var id = sizes.Count;
                sizes.Add(0);
                group[start] = id;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    sizes[id]++;
                    // A big room's cells carry their labels themselves; a tile's come from its part.
                    var sockets = blocked[cell] ?? model.Parts[parts[cell]].Sockets;
                    for (var side = 0; side < 4; side++)
                    {
                        var label = sockets[side];
                        if (label != join && !label.StartsWith('#'))
                        {
                            continue;
                        }

                        var nx = cell % width + Steps[side].X;
                        var nz = cell / width + Steps[side].Z;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                        {
                            continue;
                        }

                        var next = nx + nz * width;
                        if (group[next] < 0)
                        {
                            group[next] = id;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (sizes.Count == 0)
            {
                return null;
            }

            var largest = sizes.IndexOf(sizes.Max());
            return group.Select(id => id == largest).ToArray();
        }

        /// <summary>Where a modelled cell of a tile lands, and its footprint, after some quarter turns.</summary>
        public static (int X, int Z) TurnCell(int x, int z, int width, int depth, int turns)
        {
            // One turn takes +X to -Z (as ManaGridMapNode turns tiles), so a cell (x, z) of a w-by-d tile
            // lands at (z, w-1-x) of the d-by-w one.
            for (var i = 0; i < ManaGridMapNode.WrapOrientation(turns); i++)
            {
                (x, z, width, depth) = (z, width - 1 - x, depth, width);
            }

            return (x, z);
        }

        public static IEnumerable<int> Turns(GridMapWfcRotations rotations) => rotations switch
        {
            GridMapWfcRotations.None => [0],
            GridMapWfcRotations.Half => [0, 2],
            _ => [0, 1, 2, 3]
        };

        /// <summary>One cell of one tile at one turn: what WFC actually places.</summary>
        internal sealed record Part(int Tile, int Orientation, int X, int Z, string[] Sockets, double Weight);

        internal sealed class Model
        {
            public List<Part> Parts { get; } = new();

            /// <summary>Per side, per part: the parts that may stand across that side.</summary>
            public ulong[][][] Allowed { get; private set; } = [];

            public int Words => (Parts.Count + 63) / 64;

            public static Model Build(GridMapWfcRules rules)
            {
                var model = new Model();
                var variant = 0;
                for (var t = 0; t < rules.Tiles.Count; t++)
                {
                    var tile = rules.Tiles[t];
                    var turns = Turns(tile.Rotations).ToList();
                    var weight = Math.Max(0.0001, tile.Weight) / turns.Count;
                    foreach (var turn in turns)
                    {
                        var parts = new Dictionary<(int, int), int>();
                        for (var z = 0; z < tile.Depth; z++)
                        {
                            for (var x = 0; x < tile.Width; x++)
                            {
                                parts[(x, z)] = parts.Count;
                            }
                        }

                        for (var z = 0; z < tile.Depth; z++)
                        {
                            for (var x = 0; x < tile.Width; x++)
                            {
                                var sockets = new string[4];
                                for (var side = 0; side < 4; side++)
                                {
                                    // The side this one faced as modelled, before the turn moved it.
                                    var modelled = ((side - turn) % 4 + 4) % 4;
                                    var across = (x + Steps[modelled].X, z + Steps[modelled].Z);
                                    if (parts.TryGetValue(across, out var neighbour))
                                    {
                                        var self = parts[(x, z)];
                                        sockets[side] = $"#{variant}:{Math.Min(self, neighbour)}-{Math.Max(self, neighbour)}";
                                    }
                                    else
                                    {
                                        sockets[side] = tile.Socket(modelled, modelled is 0 or 2 ? z : x);
                                    }
                                }

                                var (tx, tz) = TurnCell(x, z, tile.Width, tile.Depth, turn);
                                model.Parts.Add(new Part(t, turn, tx, tz, sockets, weight));
                            }
                        }

                        variant++;
                    }
                }

                var words = model.Words;
                model.Allowed = new ulong[4][][];
                for (var side = 0; side < 4; side++)
                {
                    var opposite = (side + 2) % 4;
                    var byLabel = model.Parts
                        .Select((part, index) => (part.Sockets[opposite], index))
                        .GroupBy(pair => pair.Item1, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Select(pair => pair.index).ToList(), StringComparer.Ordinal);
                    model.Allowed[side] = new ulong[model.Parts.Count][];
                    for (var p = 0; p < model.Parts.Count; p++)
                    {
                        var bits = new ulong[words];
                        var label = model.Parts[p].Sockets[side];

                        // Its own label, and any the rules pair it with; a tile's inner labels pair with nothing.
                        foreach (var (other, matches) in byLabel)
                        {
                            if ((other == label && !label.StartsWith('>')) || (!label.StartsWith('#') && rules.Joins(label, other)))
                            {
                                foreach (var q in matches)
                                {
                                    bits[q >> 6] |= 1UL << (q & 63);
                                }
                            }
                        }

                        model.Allowed[side][p] = bits;
                    }
                }

                return model;
            }
        }

        private sealed class Solver
        {
            private readonly Model _model;
            private readonly GridMapWfcRules _rules;
            private readonly int _width;
            private readonly int _depth;
            private readonly Random _random;
            private readonly int _words;
            private readonly ulong[] _domains;
            private readonly int[] _counts;
            private readonly Stack<int> _pending = new();

            // Each cell's weighted choice, kept up to date as its choices narrow, and a fixed tie-break per cell.
            // Recomputing every cell's on every step made the search grow with the fourth power of the side:
            // 13 seconds for 60x60.
            private readonly double[] _entropy;
            private readonly double[] _noise;
            private readonly double[] _weights;
            private readonly double[] _weightLogs;

            // Big rooms' cells: out of the collapse, but their labels still say what may stand beside them.
            private readonly string[]?[] _blocked;

            // For a prop pass: what each cell of the dungeon is, and where its walls are.
            private readonly IReadOnlyList<Cell>? _context;

            public Solver(Model model, GridMapWfcRules rules, int width, int depth, Random random, string[]?[] blocked, IReadOnlyList<Cell>? context)
            {
                _context = context;
                _blocked = blocked;
                _model = model;
                _rules = rules;
                _width = width;
                _depth = depth;
                _random = random;
                _words = model.Words;
                _domains = new ulong[width * depth * _words];
                _counts = new int[width * depth];
                _entropy = new double[width * depth];
                _noise = new double[width * depth];
                for (var cell = 0; cell < _noise.Length; cell++)
                {
                    _noise[cell] = random.NextDouble() * 1e-6;
                }

                _weights = model.Parts.Select(part => part.Weight).ToArray();
                _weightLogs = _weights.Select(weight => weight * Math.Log(weight)).ToArray();
            }

            public int[]? Run(IReadOnlyList<Fixed> keep)
            {
                var boundary = _rules.Boundary?.Trim() ?? string.Empty;
                for (var cell = 0; cell < _counts.Length; cell++)
                {
                    if (_blocked[cell] != null)
                    {
                        // Settled already: counted as one choice, so it is never picked or run out of choices.
                        _counts[cell] = 1;
                        continue;
                    }

                    var x = cell % _width;
                    var z = cell / _width;
                    for (var p = 0; p < _model.Parts.Count; p++)
                    {
                        if (FacesOutOk(_model.Parts[p], x, z, boundary) && FitsBlocked(_model.Parts[p], x, z) && FitsContext(_model.Parts[p], cell))
                        {
                            _domains[cell * _words + (p >> 6)] |= 1UL << (p & 63);
                        }
                    }

                    Refresh(cell);
                    if (_counts[cell] == 0)
                    {
                        return null;
                    }

                    _pending.Push(cell);
                }

                foreach (var fix in keep)
                {
                    if (!Pin(fix) || !Propagate())
                    {
                        return null;
                    }
                }

                if (!Propagate())
                {
                    return null;
                }

                while (true)
                {
                    var cell = LeastChoice();
                    if (cell < 0)
                    {
                        break;
                    }

                    Collapse(cell);
                    if (!Propagate())
                    {
                        return null;
                    }
                }

                var result = new int[_counts.Length];
                for (var cell = 0; cell < result.Length; cell++)
                {
                    result[cell] = _blocked[cell] != null ? -1 : First(cell);
                }

                return result;
            }

            // On a finished dungeon: only where its tile may stand, and every side with a wall behind it able to
            // face a wall -- which is what keeps a chair from facing a wall, and a banner off open floor.
            private bool FitsContext(Part part, int cell)
            {
                if (_context == null)
                {
                    return true;
                }

                var here = _context[cell];
                var on = _rules.Tiles[part.Tile].On;
                if (on is { Length: > 0 } && !on.Contains(here.Tag, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                var wall = string.IsNullOrWhiteSpace(_rules.Boundary) ? DefaultSocket : _rules.Boundary.Trim();
                for (var side = 0; side < 4; side++)
                {
                    if (Walled(cell, side) && (part.Sockets[side].StartsWith('#') || !_rules.Joins(part.Sockets[side], wall)))
                    {
                        return false;
                    }
                }

                return true;
            }

            // A wall between a cell and its neighbour across a side, from either one's side of it.
            private bool Walled(int cell, int side)
            {
                if (_context == null)
                {
                    return false;
                }

                var mask = GridMapAutoTile.Neighbours[side].Side;
                if (_context[cell].Walls.HasFlag(mask))
                {
                    return true;
                }

                var nx = cell % _width + Steps[side].X;
                var nz = cell / _width + Steps[side].Z;
                return nx >= 0 && nz >= 0 && nx < _width && nz < _depth
                    && _context[nx + nz * _width].Walls.HasFlag(GridMapAutoTile.Neighbours[(side + 2) % 4].Side);
            }

            // Beside a big room, a part has to show the room the label the room shows it.
            private bool FitsBlocked(Part part, int x, int z)
            {
                for (var side = 0; side < 4; side++)
                {
                    var nx = x + Steps[side].X;
                    var nz = z + Steps[side].Z;
                    if (nx >= 0 && nz >= 0 && nx < _width && nz < _depth && _blocked[nx + nz * _width] is { } room
                        && room[(side + 2) % 4] != part.Sockets[side] && !_rules.Joins(room[(side + 2) % 4], part.Sockets[side]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // A side facing out of the area may never be half of a tile, and must carry the boundary label if
            // there is one: a room cannot hang off the edge, and a corridor cannot lead out of the dungeon.
            private bool FacesOutOk(Part part, int x, int z, string boundary)
            {
                for (var side = 0; side < 4; side++)
                {
                    var nx = x + Steps[side].X;
                    var nz = z + Steps[side].Z;
                    if (nx >= 0 && nz >= 0 && nx < _width && nz < _depth)
                    {
                        continue;
                    }

                    var label = part.Sockets[side];
                    if (label.StartsWith('#') || (boundary.Length > 0 && !_rules.Joins(label, boundary)))
                    {
                        return false;
                    }
                }

                return true;
            }

            // Every cell of a kept tile is held to its own part of it.
            private bool Pin(Fixed fix)
            {
                var tile = fix.Tile < _rules.Tiles.Count ? _rules.Tiles[fix.Tile] : null;
                if (tile == null)
                {
                    return true;
                }

                var turn = ManaGridMapNode.WrapOrientation(fix.Orientation);
                var (w, d) = turn % 2 == 0 ? (tile.Width, tile.Depth) : (tile.Depth, tile.Width);
                for (var p = 0; p < _model.Parts.Count; p++)
                {
                    var part = _model.Parts[p];
                    if (part.Tile != fix.Tile || part.Orientation != turn)
                    {
                        continue;
                    }

                    var x = fix.X + part.X;
                    var z = fix.Z + part.Z;
                    if (x < 0 || z < 0 || x >= _width || z >= _depth || part.X >= w || part.Z >= d)
                    {
                        continue;
                    }

                    var cell = x + z * _width;
                    var span = _domains.AsSpan(cell * _words, _words);
                    var had = (span[p >> 6] >> (p & 63)) & 1;
                    span.Clear();
                    if (had == 0)
                    {
                        _counts[cell] = 0;
                        return false;
                    }

                    span[p >> 6] = 1UL << (p & 63);
                    _counts[cell] = 1;
                    _pending.Push(cell);
                }

                return true;
            }

            private bool Propagate()
            {
                var allowed = new ulong[_words];
                while (_pending.Count > 0)
                {
                    var cell = _pending.Pop();
                    var x = cell % _width;
                    var z = cell / _width;
                    for (var side = 0; side < 4; side++)
                    {
                        var nx = x + Steps[side].X;
                        var nz = z + Steps[side].Z;
                        if (nx < 0 || nz < 0 || nx >= _width || nz >= _depth)
                        {
                            continue;
                        }

                        Array.Clear(allowed);
                        var domain = _domains.AsSpan(cell * _words, _words);
                        for (var word = 0; word < _words; word++)
                        {
                            var bits = domain[word];
                            while (bits != 0)
                            {
                                var p = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                                bits &= bits - 1;
                                var from = _model.Allowed[side][p];
                                for (var w = 0; w < _words; w++)
                                {
                                    allowed[w] |= from[w];
                                }
                            }
                        }

                        var next = nx + nz * _width;
                        if (_blocked[next] != null || Walled(cell, side))
                        {
                            continue;
                        }

                        var target = _domains.AsSpan(next * _words, _words);
                        var changed = false;
                        for (var w = 0; w < _words; w++)
                        {
                            var narrowed = target[w] & allowed[w];
                            if (narrowed != target[w])
                            {
                                target[w] = narrowed;
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            Refresh(next);
                            if (_counts[next] == 0)
                            {
                                return false;
                            }

                            _pending.Push(next);
                        }
                    }
                }

                return true;
            }

            // The undecided cell with the least weighted choice left, ties broken at random.
            private int LeastChoice()
            {
                var best = -1;
                var bestEntropy = double.MaxValue;
                for (var cell = 0; cell < _counts.Length; cell++)
                {
                    if (_counts[cell] <= 1)
                    {
                        continue;
                    }

                    var entropy = _entropy[cell] + _noise[cell];
                    if (entropy < bestEntropy)
                    {
                        bestEntropy = entropy;
                        best = cell;
                    }
                }

                return best;
            }

            /// <summary>Re-reads a cell's count and weighted choice after its choices changed.</summary>
            private void Refresh(int cell)
            {
                var count = 0;
                double sum = 0.0;
                double sumLog = 0.0;
                for (var word = 0; word < _words; word++)
                {
                    var bits = _domains[cell * _words + word];
                    while (bits != 0)
                    {
                        var p = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        count++;
                        sum += _weights[p];
                        sumLog += _weightLogs[p];
                    }
                }

                _counts[cell] = count;
                _entropy[cell] = count > 1 ? Math.Log(sum) - sumLog / sum : 0.0;
            }

            private void Collapse(int cell)
            {
                var members = Members(cell).ToList();
                var total = members.Sum(p => _model.Parts[p].Weight);
                var roll = _random.NextDouble() * total;
                var chosen = members[^1];
                foreach (var p in members)
                {
                    roll -= _model.Parts[p].Weight;
                    if (roll <= 0.0)
                    {
                        chosen = p;
                        break;
                    }
                }

                var span = _domains.AsSpan(cell * _words, _words);
                span.Clear();
                span[chosen >> 6] = 1UL << (chosen & 63);
                _counts[cell] = 1;
                _pending.Push(cell);
            }

            private IEnumerable<int> Members(int cell)
            {
                for (var word = 0; word < _words; word++)
                {
                    var bits = _domains[cell * _words + word];
                    while (bits != 0)
                    {
                        yield return (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                    }
                }
            }

            private int First(int cell) => Members(cell).First();

            private int Count(int cell)
            {
                var count = 0;
                for (var word = 0; word < _words; word++)
                {
                    count += System.Numerics.BitOperations.PopCount(_domains[cell * _words + word]);
                }

                return count;
            }
        }
    }
}
