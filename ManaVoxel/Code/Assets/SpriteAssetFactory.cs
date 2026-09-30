using Mana.Code.Content;

namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// Sprite assets made from pictures picked in the Import panel: a sheet with them as its frames, or a 9-slice
    /// with each picture put where its name says (Kenney's terrain tiles are named top_left, top, left, center...).
    /// </summary>
    public static class SpriteAssetFactory
    {
        /// <summary>A sheet of the pictures in order, with an idle animation on the first and one playing them all.</summary>
        public static ManaSpriteSheet Sheet(IReadOnlyList<string> pictures)
        {
            return new ManaSpriteSheet
            {
                Name = NameFor(pictures, "Sprite Sheet"),
                Frames = [.. pictures],
                Animations =
                [
                    new ManaSpriteAnimation { Name = "idle", Frames = [0] },
                    new ManaSpriteAnimation { Name = "play", Frames = Enumerable.Range(0, pictures.Count).ToList() }
                ]
            };
        }

        // Name endings and the piece each fills, longest first so "top_left" is not taken for "left". A one-row
        // platform's ends and middle make the top row.
        private static readonly (string Ending, Action<ManaSpriteSlice, string> Place)[] Endings =
        [
            ("horizontal_left", (s, p) => s.TopLeft = p), ("horizontal_right", (s, p) => s.TopRight = p), ("horizontal_middle", (s, p) => s.Top = p),
            ("bottom_right", (s, p) => s.BottomRight = p), ("bottom_left", (s, p) => s.BottomLeft = p),
            ("top_right", (s, p) => s.TopRight = p), ("top_left", (s, p) => s.TopLeft = p),
            ("bottom", (s, p) => s.Bottom = p), ("center", (s, p) => s.Center = p), ("centre", (s, p) => s.Center = p), ("middle", (s, p) => s.Center = p),
            ("right", (s, p) => s.Right = p), ("left", (s, p) => s.Left = p), ("top", (s, p) => s.Top = p)
        ];

        /// <summary>A 9-slice with each picture placed by its name; one it cannot place goes in the centre if that is free.</summary>
        public static ManaSpriteSlice Slice(IReadOnlyList<string> pictures)
        {
            var slice = new ManaSpriteSlice { Name = NameFor(pictures, "9-Slice") };
            foreach (var picture in pictures)
            {
                var name = Path.GetFileNameWithoutExtension(picture).ToLowerInvariant().Replace('-', '_');
                var match = Endings.FirstOrDefault(e => name == e.Ending || name.EndsWith("_" + e.Ending, StringComparison.Ordinal));
                if (match.Place != null)
                {
                    match.Place(slice, picture);
                }
                else if (slice.Center.Length == 0)
                {
                    slice.Center = picture;
                }
            }
            return slice;
        }

        /// <summary>Saves the asset under a free name in Content/Sprites and returns its file.</summary>
        public static string Save(object asset, string name, string extension)
        {
            var folder = Path.Combine(ManaContentLibrary.Root, "Sprites");
            var candidate = name;
            for (var suffix = 2; File.Exists(Path.Combine(folder, candidate + extension)); suffix++)
            {
                candidate = $"{name} {suffix}";
            }
            var path = ManaContentLibrary.NewPath(candidate, "Sprites", extension);
            ManaContentAssets.Save(asset, path);
            return path;
        }

        /// <summary>
        /// A name from what the pictures' file names share, words capitalised: character_green_idle and
        /// character_green_walk_a make "Character Green".
        /// </summary>
        public static string NameFor(IReadOnlyList<string> pictures, string fallback)
        {
            var names = pictures.Select(p => Path.GetFileNameWithoutExtension(p).Replace('-', '_')).ToList();
            if (names.Count == 0)
            {
                return fallback;
            }
            var shared = names[0];
            foreach (var name in names.Skip(1))
            {
                var length = 0;
                while (length < shared.Length && length < name.Length && shared[length] == name[length])
                {
                    length++;
                }
                shared = shared[..length];
            }
            // Only whole words: character_green_w from walk_a/walk_b would otherwise leave a stray letter.
            if (names.Count > 1 && shared.Length < names.Min(n => n.Length) && shared.LastIndexOf('_') is var cut and >= 0)
            {
                shared = shared[..cut];
            }
            var words = shared.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word is not ("block" or "horizontal"))
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
            var result = string.Join(" ", words).Trim();
            return result.Length > 0 ? result : fallback;
        }
    }
}
