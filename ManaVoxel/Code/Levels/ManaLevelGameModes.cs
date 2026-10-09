using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Mana.Code.GameModes;

namespace ManaVoxel.Code.Levels
{
    /// <summary>One game mode as a level file holds it: which mode, and its settings by name.</summary>
    public sealed class ManaLevelGameModeEntry
    {
        public string? Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> Properties { get; set; } = new();
    }

    /// <summary>
    /// Every level game mode there is, by the name a level file uses, and the code that turns a file's entry into a
    /// running mode and back. The engine's own (Character) are listed here; a game's own registers itself with
    /// <see cref="Register"/> when its assembly loads (see TrainYardWarsMode), so this file builds for the browser too.
    /// A mode's public settings save and show in the Inspector with no more work.
    /// </summary>
    public static class ManaLevelGameModes
    {
        public sealed class Definition
        {
            public required string Type { get; init; }
            public required string DisplayName { get; init; }
            public required string Description { get; init; }

            // Kept by the trimmer: a mode's public properties are its saved settings.
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
            public required Type ModeType { get; init; }

            public required Func<ManaLevelGameMode> Create { get; init; }
        }

        private static readonly List<Definition> All = new()
        {
            new Definition
            {
                Type = ManaCharacterGameMode.ModeType,
                DisplayName = "Character",
                Description = "Spawns the character at the Player Start with the follow camera and the coins/health HUD. Without it, play is a free debug camera.",
                ModeType = typeof(ManaCharacterGameMode),
                Create = () => new ManaCharacterGameMode()
            }
        };

        public static IReadOnlyList<Definition> Available => All;

        /// <summary>Adds (or replaces) a mode by its type name.</summary>
        public static void Register(Definition definition)
        {
            lock (All)
            {
                All.RemoveAll(d => string.Equals(d.Type, definition.Type, StringComparison.OrdinalIgnoreCase));
                All.Add(definition);
            }
        }

        public static Definition? Find(string type) =>
            All.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));

        public static string DisplayName(ManaLevelGameMode mode) => Find(mode.Type)?.DisplayName ?? mode.Type;

        /// <summary>A fresh mode of <paramref name="type"/> with its default settings, or null if there is no such mode.</summary>
        public static ManaLevelGameMode? Create(string type)
        {
            var definition = Find(type);
            if (definition == null)
            {
                return null;
            }

            var mode = definition.Create();
            mode.Type = definition.Type;
            return mode;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        public static ManaLevelGameMode? Load(ManaLevelGameModeEntry entry, ICollection<string> warnings)
        {
            var definition = Find(entry.Type);
            if (definition == null)
            {
                warnings.Add($"Unknown game mode '{entry.Type}' was skipped.");
                return null;
            }

            var mode = Create(definition.Type)!;
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                mode.Id = entry.Id;
            }

            foreach (var property in Settings(definition.ModeType))
            {
                var value = entry.Properties.FirstOrDefault(p => string.Equals(p.Key, property.Name, StringComparison.OrdinalIgnoreCase));
                if (value.Key == null)
                {
                    continue;
                }

                try
                {
                    property.SetValue(mode, value.Value.Deserialize(property.PropertyType, ManaLevel.SerializerOptions));
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
                {
                    warnings.Add($"Game mode '{entry.Type}' setting '{value.Key}' could not be read: {exception.Message}");
                }
            }

            return mode;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Mode types are registered with their public properties kept.")]
        public static ManaLevelGameModeEntry Capture(ManaLevelGameMode mode)
        {
            var entry = new ManaLevelGameModeEntry { Id = mode.Id, Type = mode.Type };
            var type = Find(mode.Type)?.ModeType ?? mode.GetType();
            foreach (var property in Settings(type))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                entry.Properties[name] = JsonSerializer.SerializeToElement(property.GetValue(mode), property.PropertyType, ManaLevel.SerializerOptions);
            }

            return entry;
        }

        /// <summary>A mode's settings: its own public read-write properties, not the base class's plumbing.</summary>
        public static IEnumerable<PropertyInfo> Settings(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.DeclaringType != typeof(ManaLevelGameMode))
                .OrderBy(p => p.MetadataToken);
    }
}
