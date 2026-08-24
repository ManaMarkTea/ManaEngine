using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.UI;
using Mana;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using System.IO.Compression;
using System.Text;

namespace ManaVoxel.Code.Services
{
    public partial class VoxelFileImportService
    {
        private const string ZipAssetPrefix = "zipasset:";
        private readonly Vector3 _startImportPos = new Vector3(30.0f, 0, 20);
        private Vector3 _lastImportPos = new Vector3(30.0f, 0, 20);

        public void ImportDroppedFiles(string[] files, Vector3? dropPosition, ManaWorld world, VoxelEditorViewModel viewModel)
        {
            if (dropPosition.HasValue && dropPosition.Value.LengthSquared() != 0)
            {
                _lastImportPos = dropPosition.Value;
            }

            foreach (var file in files)
            {
                if (IsZipAssetReference(file))
                {
                    ImportZipAsset(file, world, viewModel);
                    continue;
                }

                if (Path.GetExtension(file).Equals(".vox", StringComparison.OrdinalIgnoreCase))
                {
                    ImportVoxFile(file, viewModel);
                    continue;
                }

                ImportModelFile(file, world, viewModel);
            }
        }

        public void ImportFile(string file, Vector3? dropPosition, ManaWorld world, VoxelEditorViewModel viewModel)
        {
            ImportDroppedFiles(new[] { file }, dropPosition, world, viewModel);
        }

        private void ImportVoxFile(string file, VoxelEditorViewModel viewModel)
        {
            var voxLoader = new MagicaVoxLoader();
            voxLoader.Load(file);

            var importedVoxels = voxLoader.Voxels
                .Select(voxelData => new ManaVoxelData
                {
                    Position = voxelData.Position + Vector3.One * 0.5f,
                    Color = voxelData.Color
                })
                .ToArray();

            viewModel.CreateVoxelLayer(
                Path.GetFileNameWithoutExtension(file),
                "Import",
                _lastImportPos + viewModel.ImportPositionOffset,
                importedVoxels);

            _lastImportPos.X += 2.0f;
        }

        private void ImportModelFile(string file, ManaWorld world, VoxelEditorViewModel viewModel)
        {
            var model = world.LoadFromGltf(file);
            // Tagged as it lands, so an imported model is part of the level rather than something that
            // has to be re-imported by hand after every open.
            model.LevelSource = ManaLevelSource.ForMesh(file);
            ApplyModelTransform(model, viewModel);
            AdvanceImportCursor();
        }

        private void ImportZipAsset(string assetReference, ManaWorld world, VoxelEditorViewModel viewModel)
        {
            if (!TryDecodeZipAssetReference(assetReference, out var zipPath, out var entryPath))
            {
                throw new InvalidOperationException($"Invalid zip asset reference: {assetReference}");
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new FileNotFoundException($"Entry '{entryPath}' not found in zip '{zipPath}'.", zipPath);
            }

            var extension = Path.GetExtension(entryPath);
            if (extension.Equals(".vox", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Streaming .vox from zip is not yet supported. Import a loose .vox file instead.");
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            var sidecarResolver = CreateZipSidecarResolver(archive, entryPath);

            // No temp extraction: the scene file is imported directly from the zip entry's bytes.
            var model = world.LoadFromGltfMemory(memory.ToArray(), entryPath, sidecarResolver);
            model.LevelSource = ManaLevelSource.ForMesh(assetReference);
            ApplyModelTransform(model, viewModel);
            AdvanceImportCursor();
        }


        private void ApplyModelTransform(ManaSceneNode model, VoxelEditorViewModel viewModel)
        {
            model.Position = _lastImportPos + viewModel.ImportPositionOffset;
            model.Rotation = new Vector3(
                viewModel.ImportRotationDegrees.X.ToRadians(),
                viewModel.ImportRotationDegrees.Y.ToRadians(),
                viewModel.ImportRotationDegrees.Z.ToRadians());
            model.Scale = new Vector3(
                MathF.Max(0.01f, viewModel.ImportScale.X),
                MathF.Max(0.01f, viewModel.ImportScale.Y),
                MathF.Max(0.01f, viewModel.ImportScale.Z));
        }

        private void AdvanceImportCursor()
        {
            _lastImportPos.X += 2.0f;
            if (_lastImportPos.X > _startImportPos.X + 30)
            {
                _lastImportPos.X = _startImportPos.X;
                _lastImportPos.Z += 2.0f;
            }
        }

        private static bool IsZipAssetReference(string pathOrReference)
        {
            return pathOrReference.StartsWith(ZipAssetPrefix, StringComparison.Ordinal);
        }

        private static bool TryDecodeZipAssetReference(string assetReference, out string zipPath, out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;
            if (!assetReference.StartsWith(ZipAssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = assetReference.Substring(ZipAssetPrefix.Length);
            var separator = payload.IndexOf(':');
            if (separator <= 0 || separator >= payload.Length - 1)
            {
                return false;
            }

            try
            {
                zipPath = Encoding.UTF8.GetString(Convert.FromBase64String(payload[..separator]));
                entryPath = Encoding.UTF8.GetString(Convert.FromBase64String(payload[(separator + 1)..]));
                return !string.IsNullOrWhiteSpace(zipPath) && !string.IsNullOrWhiteSpace(entryPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
