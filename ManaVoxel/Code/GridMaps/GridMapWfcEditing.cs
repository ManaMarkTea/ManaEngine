namespace ManaVoxel.Code.GridMaps
{
    /// <summary>
    /// The generator window's state: the rules being edited, where they came from, and the area and seed to
    /// generate with. Static like <see cref="GridMapEditing"/> -- the window edits it and the viewport outlines
    /// the area from it, and there is one generator window at a time.
    /// </summary>
    public static class GridMapWfcEditing
    {
        private static GridMapWfcRules? _rules;

        /// <summary>Whether the generator window is up. It stays up until closed, whatever is selected.</summary>
        public static bool WindowOpen { get; set; }

        private static WeakReference<ManaGridMapNode>? _lastMap;

        /// <summary>
        /// The map the generator works on: the selected grid map, or when something else is selected, the last
        /// one it worked on -- so clicking about the scene does not leave the window with nothing to do.
        /// </summary>
        public static ManaGridMapNode? MapFor(Mana.Code.Nodes.ManaSceneNode? selected, Func<ManaGridMapNode, bool> isInWorld)
        {
            if (selected is ManaGridMapNode map)
            {
                _lastMap = new WeakReference<ManaGridMapNode>(map);
                return map;
            }

            return _lastMap != null && _lastMap.TryGetTarget(out var last) && isInWorld(last) ? last : null;
        }

        /// <summary>The rules being edited: the first file in Generators, or the dungeon preset when there is none.</summary>
        public static GridMapWfcRules Rules
        {
            get
            {
                if (_rules == null)
                {
                    var first = GridMapWfcGenerator.List().FirstOrDefault();
                    if (first != null && TryOpen(first, out _))
                    {
                        return _rules!;
                    }

                    Replace(GridMapWfcPresets.Dungeon(), null);
                }

                return _rules!;
            }
        }

        /// <summary>The file the rules were opened from or last saved to; null for rules not saved yet.</summary>
        public static string? Path { get; private set; }

        /// <summary>Set by any edit, cleared by a save or an open.</summary>
        public static bool Dirty { get; set; }

        /// <summary>Replaced when the list of tiles changes shape -- a view rebuilds its tile rows on a new one.</summary>
        public static object RulesToken { get; private set; } = new();

        public static int X { get; set; }

        public static int Z { get; set; }

        public static int Width { get; set; } = 16;

        public static int Depth { get; set; } = 16;

        public static int Seed { get; set; } = 1;

        public static bool KeepPainted { get; set; } = true;

        public static bool ConnectedOnly { get; set; } = true;

        /// <summary>How many rooms to lay before the rest is generated round them; see GridMapWfc.Solve.</summary>
        public static int Rooms { get; set; } = 4;

        /// <summary>What the last generate did, for the window to say.</summary>
        public static string Status { get; set; } = string.Empty;

        public static void Replace(GridMapWfcRules rules, string? path)
        {
            _rules = rules;
            Path = path;
            RulesToken = new object();
            Dirty = false;
        }

        public static bool TryOpen(string path, out string? error)
        {
            try
            {
                Replace(GridMapWfcGenerator.Load(path), path);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                error = exception.Message;
                return false;
            }
        }

        public static void Save()
        {
            Path = GridMapWfcGenerator.Save(Rules, Path);
            Dirty = false;
        }

        /// <summary>The tile list changed shape: rows are rebuilt.</summary>
        public static void Changed()
        {
            RulesToken = new object();
            Dirty = true;
        }
    }
}
