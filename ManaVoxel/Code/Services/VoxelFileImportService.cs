using ManaVoxel.Code.Loaders;
using ManaVoxel.Code.UI;
using Mana;

namespace ManaVoxel.Code.Services
{
    public class VoxelFileImportService
    {
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
                if (Path.GetExtension(file).Equals(".vox", StringComparison.OrdinalIgnoreCase))
                {
                    ImportVoxFile(file, viewModel);
                    continue;
                }

                ImportModelFile(file, world, viewModel);
            }
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
            model.Position = _lastImportPos + viewModel.ImportPositionOffset;
            model.Rotation = new Vector3(
                viewModel.ImportRotationDegrees.X.ToRadians(),
                viewModel.ImportRotationDegrees.Y.ToRadians(),
                viewModel.ImportRotationDegrees.Z.ToRadians());
            model.Scale = new Vector3(
                MathF.Max(0.01f, viewModel.ImportScale.X),
                MathF.Max(0.01f, viewModel.ImportScale.Y),
                MathF.Max(0.01f, viewModel.ImportScale.Z));

            _lastImportPos.X += 2.0f;
            if (_lastImportPos.X > _startImportPos.X + 30)
            {
                _lastImportPos.X = _startImportPos.X;
                _lastImportPos.Z += 2.0f;
            }
        }
    }
}
