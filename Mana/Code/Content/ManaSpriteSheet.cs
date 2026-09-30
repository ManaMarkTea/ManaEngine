namespace Mana.Code.Content
{
    /// <summary>
    /// A character's or an object's pictures, set up once: the frames -- separate pictures, or one picture cut into
    /// a grid -- how big they are in the world, where they stand, and the named animations played from them (idle,
    /// walk, jump). Saved as a .manasheet in Content/; a Sprite Sheet component shows one, and a Platformer2D player
    /// is drawn from one.
    /// </summary>
    public sealed class ManaSpriteSheet
    {
        public const string Extension = ".manasheet";

        public string Format { get; set; } = "mana-sprite-sheet";
        public int Version { get; set; } = 1;
        public string Id { get; set; } = ManaContentAssets.NewId();
        public string Name { get; set; } = "Sprite Sheet";

        /// <summary>One picture per frame, in order -- how packs like Kenney's ship animation. Used over <see cref="Texture"/> when set.</summary>
        public List<string> Frames { get; set; } = [];

        /// <summary>A single picture holding every frame in a grid of <see cref="Columns"/> x <see cref="Rows"/>, counted across then down.</summary>
        public string Texture { get; set; } = string.Empty;
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;

        /// <summary>Optional normal map, laid out the same way as <see cref="Texture"/>.</summary>
        public string NormalMap { get; set; } = string.Empty;

        public float PixelsPerUnit { get; set; } = 64.0f;

        /// <summary>Where the node stands on a frame, 0..1 across and up: [0.5, 0] is the bottom middle -- a character's feet.</summary>
        public float[] Pivot { get; set; } = [0.5f, 0.0f];

        public List<ManaSpriteAnimation> Animations { get; set; } = [];

        public int FrameCount => Frames.Count > 0 ? Frames.Count : Math.Max(1, Columns) * Math.Max(1, Rows);

        public ManaSpriteAnimation? Animation(string name) =>
            Animations.FirstOrDefault(animation => string.Equals(animation.Name, name, StringComparison.OrdinalIgnoreCase));

        public Vector2 PivotPoint => Pivot.Length >= 2 ? new Vector2(Pivot[0], Pivot[1]) : new Vector2(0.5f, 0.0f);
    }

    /// <summary>A named run of a sheet's frames: which frames, how fast, and whether it loops or holds the last.</summary>
    public sealed class ManaSpriteAnimation
    {
        public string Name { get; set; } = "idle";
        public List<int> Frames { get; set; } = [0];
        public float FramesPerSecond { get; set; } = 8.0f;
        public bool Loop { get; set; } = true;

        /// <summary>The sheet frame shown <paramref name="seconds"/> into this animation.</summary>
        public int FrameAt(double seconds)
        {
            if (Frames.Count == 0)
            {
                return 0;
            }
            var step = (int)Math.Floor(Math.Max(seconds, 0.0) * Math.Max(FramesPerSecond, 0.0f));
            return Frames[Loop ? step % Frames.Count : Math.Min(step, Frames.Count - 1)];
        }
    }
}
