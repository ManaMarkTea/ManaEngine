using System.Numerics;

namespace ManaCore.Graphics
{
    public readonly record struct ManaCameraData(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Vector3 Position,
        float NearClipPlane,
        float FarClipPlane);
}
