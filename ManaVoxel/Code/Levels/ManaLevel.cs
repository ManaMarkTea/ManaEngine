using System.Text.Json;
using System.Text.Json.Serialization;
using Mana.Code.Levels;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// A saved level: the things somebody placed, and where the camera was looking when they saved.
    /// <para>
    /// Deliberately not a snapshot of the whole world. The demo scenery the game mode builds in code --
    /// grid planes, the streamed planet, border overlays -- has no <see cref="ManaLevelSource"/> and is
    /// never captured, because none of it could be rebuilt from a file anyway; it comes back the same way
    /// it arrived the first time, from the code that generates it.
    /// </para>
    /// </summary>
    public sealed class ManaLevel
    {
        /// <summary>Stamped into every file so a wrong-format JSON fails with a sentence, not a null ref.</summary>
        public const string FormatName = "mana-level";

        /// <summary>Bump when an older file can no longer be read as-is.</summary>
        public const int CurrentVersion = 1;

        public const string FileExtension = ".manalevel";

        public string Format { get; set; } = FormatName;

        public int Version { get; set; } = CurrentVersion;

        public string Name { get; set; } = "Untitled";

        /// <summary>Where the editor camera was. Restored on load so a level opens looking at itself.</summary>
        public ManaLevelCamera? Camera { get; set; }

        public List<ManaLevelNode> Nodes { get; set; } = new();

        public ManaLevelNode? PlayerStart =>
            FindPlayerStart(Nodes);

        private static ManaLevelNode? FindPlayerStart(IEnumerable<ManaLevelNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == ManaLevelSourceKind.PlayerStart)
                {
                    return node;
                }

                if (node.Children is { Count: > 0 })
                {
                    var childStart = FindPlayerStart(node.Children);
                    if (childStart != null)
                    {
                        return childStart;
                    }
                }
            }

            return null;
        }

        // ---- File I/O ---------------------------------------------------------------------------------

        internal static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

        public static ManaLevel FromJson(string json)
        {
            var level = JsonSerializer.Deserialize<ManaLevel>(json, SerializerOptions)
                ?? throw new InvalidDataException("Level file is empty.");

            if (!string.Equals(level.Format, FormatName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Not a Mana level file (format '{level.Format}', expected '{FormatName}').");
            }

            if (level.Version > CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Level was saved by a newer build (version {level.Version}, this build reads {CurrentVersion}).");
            }

            return level;
        }

        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Written beside the target and moved into place, so a crash mid-write leaves the previous
            // level intact rather than a half-file that will not parse.
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, ToJson());
            File.Move(temporaryPath, path, overwrite: true);
        }

        public static ManaLevel Load(string path) => FromJson(File.ReadAllText(path));
    }

    public sealed class ManaLevelCamera
    {
        public float[] Position { get; set; } = new float[3];

        /// <summary>Euler angles in radians, matching ManaSceneNode.Rotation.</summary>
        public float[] Rotation { get; set; } = new float[3];
    }

    /// <summary>
    /// One placed thing. <see cref="Kind"/> decides which of the optional members below carry meaning --
    /// a mesh has an <see cref="Asset"/>, a primitive has a <see cref="Shape"/>, a voxel node has
    /// <see cref="Voxels"/>, a group has <see cref="Children"/>, and a player start has none of them.
    /// </summary>
    public sealed class ManaLevelNode
    {
        public string Name { get; set; } = string.Empty;

        public ManaLevelSourceKind Kind { get; set; }

        public float[] Position { get; set; } = new float[3];

        /// <summary>Euler angles in radians.</summary>
        public float[] Rotation { get; set; } = new float[3];

        public float[] Scale { get; set; } = { 1.0f, 1.0f, 1.0f };

        public string? Shape { get; set; }

        public string? Asset { get; set; }

        public float[]? Color { get; set; }

        /// <summary>
        /// The size the shape factory was called with, which is not the same as <see cref="Scale"/> --
        /// see the note on <see cref="ManaLevelSource"/>.
        /// </summary>
        public float[]? Size { get; set; }

        public ManaLevelCollision? Collision { get; set; }

        public ManaLevelVoxels? Voxels { get; set; }

        public List<ManaLevelScript>? Scripts { get; set; }

        public List<ManaLevelNode>? Children { get; set; }
    }

    /// <summary>A kinematic box collider, so a platform you jumped on before is still solid after loading.</summary>
    public sealed class ManaLevelCollision
    {
        /// <summary>Full extent, as Bepu's Box takes it -- twice a ManaCube's half-extent.</summary>
        public float[] Size { get; set; } = new float[3];

        public float[] Offset { get; set; } = new float[3];
    }

    /// <summary>Scene-node behavior data, like a tiny Godot-style script attachment.</summary>
    public sealed class ManaLevelScript
    {
        public string Type { get; set; } = string.Empty;

        public Dictionary<string, JsonElement> Properties { get; set; } = new();
    }

    /// <summary>
    /// A voxel grid. The colours go in a palette and the voxels in one base64 blob of 8-byte records
    /// (three int16 grid coordinates plus a uint16 palette index), because a model imported from
    /// MagicaVox routinely runs to six figures and as JSON objects that is tens of megabytes of braces.
    /// </summary>
    public sealed class ManaLevelVoxels
    {
        public List<float[]> Palette { get; set; } = new();

        public int Count { get; set; }

        public string Data { get; set; } = string.Empty;
    }
}
