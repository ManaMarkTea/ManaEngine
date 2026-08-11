namespace ManaVoxel.Code.UI
{
    public interface IVoxelEditorCommands
    {
        void BakeToPhysics();
        void LockBakedVoxelsForEditing();
        void BeginReassembleBakedVoxels();
        void ExplodeBakedVoxels();
        void ExplodeBakedVoxelsAt(Vector3 position);
        void AddVoxelAt(Vector3 position);
        void ApplyRuntimeSettings();
        void SpawnCharacter();
        void RemoveAllCharacters();
        void SpawnPlatformCourse();
        void RemovePlatformCourse();
    }
}
