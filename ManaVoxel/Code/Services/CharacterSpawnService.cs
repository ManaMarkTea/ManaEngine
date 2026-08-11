using Mana;
using Mana.Code;
using Mana.Code.Animation;
using Mana.Code.Nodes;
using System.IO.Compression;

namespace ManaVoxel.Code.Services
{
    // Spawns rigged Kenney mini-characters as controllable pawns, read straight out of the shipped
    // asset zip (no extraction step -- same approach as VoxelFileImportService's zip import).
    public class CharacterSpawnService
    {
        private const string CharacterZipFileName = "kenney_mini-characters.zip";
        private const string CharacterEntryDirectory = "Models/GLB format";

        // The Kenney characters are 0.67 units tall at native scale, which is smaller than a single
        // voxel. Two units reads as roughly human next to the editor's 1-unit voxel grid.
        private const float DefaultCharacterHeight = 2.0f;

        private int _nextCharacterIndex;

        public string? LastError { get; private set; }
        public IReadOnlyList<ManaStrideMeasurement> LastStrideMeasurements { get; private set; } = Array.Empty<ManaStrideMeasurement>();

        // Mirrors VoxelEditorPanelsView.GetMeshAssetRoots: the working directory differs between a
        // `dotnet run`, a published build and running the exe from bin/, so the pack is probed for
        // rather than assumed.
        public static string? ResolveCharacterZipPath()
        {
            foreach (var root in GetAssetRoots())
            {
                var candidate = Path.Combine(root, CharacterZipFileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        private static IEnumerable<string> GetAssetRoots()
        {
            yield return Path.Combine(Environment.CurrentDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets");
        }

        public bool IsAvailable => ResolveCharacterZipPath() != null;

        // Cycles through the available characters so repeated spawns produce a varied crowd rather
        // than a stack of identical models.
        public ManaPawnNode? SpawnNextCharacter(ManaWorld world, Vector3 position, ManaPawnController? controller = null)
        {
            LastError = null;

            var zipPath = ResolveCharacterZipPath();
            if (zipPath == null)
            {
                LastError = $"Character pack '{CharacterZipFileName}' was not found in any Assets folder.";
                return null;
            }

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var characterEntries = archive.Entries
                    .Where(entry =>
                        entry.FullName.StartsWith(CharacterEntryDirectory, StringComparison.OrdinalIgnoreCase) &&
                        entry.Name.StartsWith("character-", StringComparison.OrdinalIgnoreCase) &&
                        entry.Name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (characterEntries.Count == 0)
                {
                    LastError = $"No character models found inside '{zipPath}'.";
                    return null;
                }

                var entry = characterEntries[_nextCharacterIndex % characterEntries.Count];
                _nextCharacterIndex++;

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);

                var sidecarResolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entry.FullName);
                var loaded = world.LoadFromGltfMemory(
                    memory.ToArray(),
                    entry.Name,
                    sidecarResolver,
                    (context, localTransform) => new ManaPawnNode(context, localTransform));

                if (loaded is not ManaPawnNode pawn)
                {
                    LastError = $"'{entry.Name}' has no skeleton, so it cannot be spawned as a character.";
                    return null;
                }

                pawn.Name = Path.GetFileNameWithoutExtension(entry.Name);
                pawn.TryNormalizeHeight(DefaultCharacterHeight);
                pawn.GroundHeight = position.Y;
                pawn.Position = position;

                // Must run after the height normalisation above: the measured speeds are in world
                // units, so they only mean anything once the pawn's final scale is set. Adopting them
                // as the default speeds is what keeps the feet planted out of the box.
                LastStrideMeasurements = pawn.CalibrateLocomotionClips();
                pawn.ApplyCalibratedSpeeds();

                pawn.UpdateTransform();

                controller?.Register(pawn);
                return pawn;
            }
            catch (Exception exception)
            {
                LastError = $"Failed to spawn character: {exception.Message}";
                return null;
            }
        }
    }
}
