using ManaCore.Graphics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// An immutable view of one patch's geometry at the moment it changed, captured by PlanetNode while
    /// it already holds its rebuild lock.
    ///
    /// This exists so overlays that follow the terrain never reach back into PlanetNode's locked state.
    /// Sampling the live quadtree from the main thread meant taking the rebuild lock every frame, and
    /// background up-res holds that lock across a full patch mesh regeneration -- so the main thread
    /// either blocked or spun retrying. A snapshot has no such coupling: CpuData is replaced wholesale
    /// by ManaMesh.SetCpuData rather than mutated in place, so this reference stays valid indefinitely
    /// and can be read from any thread with no synchronization.
    /// </summary>
    public sealed record PatchGeometrySnapshot(
        PatchKey Key,
        Vector3 FaceNormal,
        float MinU,
        float MaxU,
        float MinV,
        float MaxV,
        int Resolution,
        ManaMeshCpuData CpuData)
    {
        public bool Contains(int face, float u, float v)
        {
            return face == Key.Face && u >= MinU && u <= MaxU && v >= MinV && v <= MaxV;
        }

        /// <summary>
        /// Distance from the planet's center to this patch's surface at a face-space (u, v), bilinear
        /// over the four surrounding grid vertices. Grid vertices are the first Resolution^2 entries in
        /// y-outer/x-inner order; skirts are appended after and so never interfere.
        /// </summary>
        public bool TrySampleRadius(float u, float v, out float radius)
        {
            radius = 0f;
            var positions = CpuData.Positions;
            if (positions.Length < Resolution * Resolution)
            {
                return false;
            }

            var spanU = MaxU - MinU;
            var spanV = MaxV - MinV;
            if (spanU <= 0f || spanV <= 0f)
            {
                return false;
            }

            var gridX = Math.Clamp((u - MinU) / spanU, 0f, 1f) * (Resolution - 1);
            var gridY = Math.Clamp((v - MinV) / spanV, 0f, 1f) * (Resolution - 1);
            var x0 = Math.Clamp((int)MathF.Floor(gridX), 0, Resolution - 1);
            var y0 = Math.Clamp((int)MathF.Floor(gridY), 0, Resolution - 1);
            var x1 = Math.Min(x0 + 1, Resolution - 1);
            var y1 = Math.Min(y0 + 1, Resolution - 1);
            var fx = gridX - x0;
            var fy = gridY - y0;

            float RadiusAt(int x, int y) => positions[(y * Resolution) + x].Length();

            var top = float.Lerp(RadiusAt(x0, y0), RadiusAt(x1, y0), fx);
            var bottom = float.Lerp(RadiusAt(x0, y1), RadiusAt(x1, y1), fx);
            radius = float.Lerp(top, bottom, fy);
            return true;
        }
    }
}
