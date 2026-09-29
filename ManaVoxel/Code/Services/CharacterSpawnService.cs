using Mana;
using Mana.Code;
using Mana.Code.Animation;
using Mana.Code.Nodes;
using System.IO.Compression;

namespace ManaVoxel.Code.Services
{
    // One rigged character model, addressed by the zip it lives in plus the entry inside it.
    public sealed record CharacterAsset(string ZipPath, string EntryPath, string PackName, string DisplayName)
    {
        public override string ToString() => $"{PackName} / {DisplayName}";
    }

    // Discovers and spawns rigged characters from the Kenney asset packs, read straight out of the
    // zips (no extraction step -- same approach as VoxelFileImportService's zip import).
    //
    // Discovery is by shape rather than by a hardcoded filename, so dropping another pack into Assets
    // picks it up with no code change, as long as it ships GLBs in a "GLB format" folder.
    public class CharacterSpawnService
    {
        private List<CharacterAsset>? _cachedCatalog;
        private int _nextCharacterIndex;

        public string? LastError { get; private set; }
        public IReadOnlyList<ManaStrideMeasurement> LastStrideMeasurements { get; private set; } = Array.Empty<ManaStrideMeasurement>();

        // Mirrors VoxelEditorPanelsView.GetMeshAssetRoots: the working directory differs between a
        // `dotnet run`, a published build and running the exe from bin/, so the packs are probed for
        // rather than assumed.
        private static IEnumerable<string> GetAssetRoots()
        {
            yield return Path.Combine(Environment.CurrentDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets");
        }

        public IReadOnlyList<CharacterAsset> Catalog => _cachedCatalog ??= BuildCatalog();

        public bool IsAvailable => Catalog.Count > 0;

        public void RefreshCatalog()
        {
            _cachedCatalog = null;
        }

        private static List<CharacterAsset> BuildCatalog()
        {
            var catalog = new List<CharacterAsset>();
            var seenZips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in GetAssetRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var zipPath in Directory.EnumerateFiles(root, "*.zip"))
                {
                    var fullPath = Path.GetFullPath(zipPath);
                    if (!seenZips.Add(fullPath))
                    {
                        continue;
                    }

                    TryAddPack(catalog, fullPath);
                }
            }

            return catalog
                .OrderBy(asset => asset.PackName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(asset => asset.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryAddPack(List<CharacterAsset> catalog, string zipPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var packName = Path.GetFileNameWithoutExtension(zipPath);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // "GLB format" is the Kenney convention; requiring it keeps this from dragging in
                    // the FBX/OBJ copies of the same models that sit alongside it.
                    var directory = Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/') ?? string.Empty;
                    if (!directory.Contains("GLB format", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // A pack can hold props as well as characters (the mini-characters pack ships
                    // canes and wheelchairs next to the people).
                    if (!entry.Name.StartsWith("character", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    catalog.Add(new CharacterAsset(
                        zipPath,
                        entry.FullName,
                        packName,
                        Path.GetFileNameWithoutExtension(entry.Name)));
                }
            }
            catch
            {
                // A locked or malformed archive should not break discovery of the rest.
            }
        }

        // Cycles through every discovered character so repeated spawns produce a varied crowd rather
        // than a stack of identical models.
        public ManaPawnNode? SpawnNextCharacter(
            ManaWorld world,
            Vector3 position,
            ManaPawnController? controller = null,
            bool usePhysics = true,
            float targetHeight = 0.0f)
        {
            var catalog = Catalog;
            if (catalog.Count == 0)
            {
                LastError = "No character packs found in any Assets folder.";
                return null;
            }

            var asset = catalog[_nextCharacterIndex % catalog.Count];
            _nextCharacterIndex++;
            return Spawn(asset, world, position, controller, usePhysics, targetHeight);
        }

        /// <summary>
        /// The character a Player Start asks for, scaled to the height it asks for -- or the next one in the
        /// cycle when it asks for nothing, or for something no pack here provides.
        /// </summary>
        public ManaPawnNode? SpawnFor(ManaPlayerStartNode? start, ManaWorld world, Vector3 position, ManaPawnController? controller = null, bool usePhysics = true)
        {
            // The start's height whichever character it gets, and a metre with no start at all.
            var height = start?.CharacterHeight ?? ManaPlayerStartNode.DefaultCharacterHeight;
            var index = IndexOf(start?.CharacterPack, start?.CharacterName);
            if (index < 0)
            {
                return SpawnNextCharacter(world, position, controller, usePhysics, height);
            }

            return Spawn(Catalog[index], world, position, controller, usePhysics, height);
        }

        // Index of a character in the catalog, or -1. Used to work out where "the next one" is when
        // swapping the character a player is controlling.
        public int IndexOf(string? packName, string? displayName)
        {
            if (packName == null || displayName == null)
            {
                return -1;
            }

            for (var i = 0; i < Catalog.Count; i++)
            {
                if (string.Equals(Catalog[i].PackName, packName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Catalog[i].DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public ManaPawnNode? Spawn(
            CharacterAsset asset,
            ManaWorld world,
            Vector3 position,
            ManaPawnController? controller = null,
            bool usePhysics = true,
            float targetHeight = 0.0f)
        {
            LastError = null;

            try
            {
                using var archive = ZipFile.OpenRead(asset.ZipPath);
                var entry = archive.GetEntry(asset.EntryPath);
                if (entry == null)
                {
                    LastError = $"'{asset.EntryPath}' is no longer inside '{asset.ZipPath}'.";
                    return null;
                }

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);

                var sidecarResolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entry.FullName);
                var loaded = world.LoadFromGltfMemory(
                    memory.ToArray(),
                    entry.FullName,
                    sidecarResolver,
                    (context, localTransform) => new ManaPawnNode(context, localTransform));

                if (loaded is not ManaPawnNode pawn)
                {
                    LastError = $"'{entry.Name}' has no rig, so it cannot be spawned as a character.";
                    return null;
                }

                pawn.Name = asset.DisplayName;
                pawn.SourcePackName = asset.PackName;

                // Kept at the model's own size, so a character in Play is the same size as the same model
                // placed at 1x in the editor. Kenney's run from 0.7 units tall (mini) to 2.7 (blocky), so the
                // speeds and jump are scaled to match rather than left tuned for one height.
                // A level built for a particular height asks for it: without this a model under a metre tall
                // (the mini-dungeon hero) would jump a third of what the course was built for.
                if (targetHeight > 0.0f)
                {
                    pawn.TryNormalizeHeight(targetHeight);
                }

                pawn.ScaleLocomotionToHeight();
                pawn.GroundHeight = position.Y;
                pawn.Position = position;

                // Must run after the height normalisation above: the measured speeds are in world
                // units, so they only mean anything once the pawn's final scale is set.
                //
                // Measured but deliberately not adopted (see ManaPawnNode.ApplyCalibratedSpeeds, which
                // the inspector still offers): the pawn keeps its own WoW-tuned speeds, and these
                // measurements are what MatchAnimationToSpeed retimes the clips against to keep the
                // feet planted at them. Adopting them instead left the character walking at whatever
                // pace its clips happened to be authored at, which is what made it feel slow.
                LastStrideMeasurements = pawn.CalibrateLocomotionClips();

                // Physics last: the capsule is sized from the pawn's final rendered height and spawned
                // at its final position, so both have to be settled first.
                if (usePhysics)
                {
                    pawn.EnablePhysics();
                    pawn.RespawnPoint = position;
                }

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
