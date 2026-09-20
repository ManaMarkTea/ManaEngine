using System.Text.Json;
using System.Text.Json.Serialization;
using Mana.Code.Levels;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// A saved level: the things somebody placed, and where the camera was looking when they saved.
    /// <para>
    /// Deliberately not a snapshot of the whole world. Editor infrastructure such as construction planes,
    /// cursor markers, and transient diagnostics has no <see cref="ManaLevelSource"/> and is never captured.
    /// Models, voxel assets, and streamed planets are authored nodes and therefore live in level files.
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
            Converters = { new JsonStringEnumConverter(), new ManaLevelVector3Converter() }
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
    /// <see cref="Voxels"/> or an external asset, a planet has <see cref="Planet"/>, water has
    /// <see cref="Water"/>, a group has <see cref="Children"/>, and a player start has none of them.
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
        /// Visual size passed to the shape factory; see the note on <see cref="ManaLevelSource"/>.
        /// </summary>
        public float[]? Size { get; set; }

        public ManaLevelCollision? Collision { get; set; }

        public ManaLevelVoxels? Voxels { get; set; }

        public ManaLevelPlanet? Planet { get; set; }

        public ManaLevelWater? Water { get; set; }

        /// <summary>On a Player Start: which character the player is, when the level cares.</summary>
        public ManaLevelCharacter? Character { get; set; }

        public List<ManaLevelScript>? Scripts { get; set; }

        /// <summary>Scene-local box colliders on descendants recreated from a mesh asset.</summary>
        public List<ManaLevelChildColliders>? ChildColliders { get; set; }

        public List<ManaLevelNode>? Children { get; set; }
    }

    public sealed class ManaLevelChildColliders
    {
        public int[] Path { get; set; } = [];
        public string[] Names { get; set; } = [];
        public List<ManaLevelBoxCollider> Boxes { get; set; } = [];
    }

    public sealed class ManaLevelBoxCollider
    {
        public Vector3 Size { get; set; } = Vector3.One;
        public Vector3 Center { get; set; }
    }

    /// <summary>
    /// The character a level wants its player to be: a model by pack and name, and how tall to make it.
    /// Kenney's packs differ a lot in scale (a mini-dungeon hero is under a metre), and a course is built for
    /// a particular height, so the level says what height it was built for.
    /// </summary>
    public sealed class ManaLevelCharacter
    {
        /// <summary>The asset pack, as the character catalog names it -- the zip's file name without its extension.</summary>
        public string Pack { get; set; } = string.Empty;

        /// <summary>The model's name within the pack, e.g. <c>character-human</c>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Height to scale the model to, in world units; zero keeps its own size.</summary>
        public float Height { get; set; }
    }

    /// <summary>A kinematic collider, so authored terrain is still solid after loading.</summary>
    public sealed class ManaLevelCollision
    {
        /// <summary>
        /// When true, the box is rebuilt from the rendered mesh bounds instead of a separate size.
        /// This keeps platform-style collision authored as part of the visual object.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool UseVisualBounds { get; set; }

        /// <summary>When true, collision is rebuilt as a convex hull of the rendered mesh.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool UseVisualHull { get; set; }

        /// <summary>Full extent, as Bepu's Box takes it. Explicit colliders require this.</summary>
        public float[]? Size { get; set; }

        /// <summary>Local offset. Omitted visual-bounds colliders use the visual bounds center.</summary>
        public float[]? Offset { get; set; }
    }

    /// <summary>Scene-node behavior data, like a tiny Godot-style script attachment.</summary>
    public sealed class ManaLevelScript
    {
        public string Type { get; set; } = string.Empty;

        public Dictionary<string, JsonElement> Properties { get; set; } = new();
    }

    /// <summary>Construction settings for a streamed cube-sphere planet.</summary>
    public sealed class ManaLevelPlanet
    {
        public int PatchesPerFaceEdge { get; set; } = 6;

        public float Radius { get; set; } = 40.0f;

        public int Resolution { get; set; } = 24;

        public float HeightExaggeration { get; set; } = 1.0f;

        public int OceanResolution { get; set; } = 97;

        public bool AutoLodEnabled { get; set; }

        public bool NearOceanEnabled { get; set; } = true;
    }

    /// <summary>Construction settings for a ripple-capable water surface.</summary>
    public sealed class ManaLevelWater
    {
        public float[] Size { get; set; } = new[] { 16.0f, 16.0f };

        public int Resolution { get; set; } = 64;
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
