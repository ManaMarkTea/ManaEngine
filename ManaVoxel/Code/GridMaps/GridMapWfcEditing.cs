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

        /// <summary>Whether the generator window is wanted. It shows while this is set and a grid map is selected.</summary>
        public static bool WindowOpen { get; set; }

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
