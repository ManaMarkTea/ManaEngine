using ManaCore.Graphics;

namespace Mana.Code.Graphics
{
    public interface IOpenGlGpuMesh : IManaGpuMesh
    {
        void Draw();
        void UpdateDynamicData(Vector3[] positions, Vector3[] normals, Vector4[] colors);
    }
}
