using System.Text.Json;

namespace ManaVoxel.Code.Levels
{
    /// <summary>A level offered on a start screen: what the web selector lists, read the same way.</summary>
    public sealed record ManaPlayableLevel(string Path, string Name, string? Description, int Order, string? Picture);

    /// <summary>
    /// The levels a player can pick from: those whose <c>web</c> block says publish (scene Inspector > Publishing),
    /// in their order, each with its picture from the Pictures folder beside it when there is one -- the list
    /// Tools/WebAssets/publish_assets.py writes for the web, read here for the apps that carry their levels.
    /// </summary>
    public static class ManaPlayableLevels
    {
        public static IReadOnlyList<ManaPlayableLevel> List(string levelsFolder = ManaLevelLibrary.FolderName)
        {
            if (!Directory.Exists(levelsFolder))
            {
                return Array.Empty<ManaPlayableLevel>();
            }

            // The main folder's and each game's own (Games/<Name>/Content/Levels).
            var levels = new List<ManaPlayableLevel>();
            foreach (var path in ManaLevelLibrary.Folders(levelsFolder).Where(folder => Directory.Exists(folder.Folder))
                .SelectMany(folder => Directory.EnumerateFiles(folder.Folder, "*.manalevel", SearchOption.TopDirectoryOnly)))
            {
                if (Read(path) is { } level)
                {
                    levels.Add(level);
                }
            }
            return levels.OrderBy(level => level.Order).ThenBy(level => level.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ManaPlayableLevel? Read(string path)
        {
            try
            {
                // Only the web block is wanted; a document read skips building the whole level.
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("web", out var web) || web.ValueKind != JsonValueKind.Object
                    || !web.TryGetProperty("publish", out var publish) || publish.ValueKind != JsonValueKind.True)
                {
                    return null;
                }

                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var description = web.TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
                // Unordered levels follow the ordered ones, as on the web.
                var order = web.TryGetProperty("order", out var number) && number.TryGetInt32(out var value) ? value : int.MaxValue;
                var picture = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? ".", "Pictures", name + ".jpg");
                return new ManaPlayableLevel(path, name, description, order, File.Exists(picture) ? picture : null);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
