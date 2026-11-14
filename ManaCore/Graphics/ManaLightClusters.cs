using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>
    /// Clustered lighting: the view cut into cells -- tiles across the screen, slices in depth -- and for each the placed
    /// lights whose range reaches into it. A pixel then shades only the few lights near it rather than every light in
    /// the scene, so a level can hold thousands of lamps and torches.
    /// <para>
    /// The binning runs on the GPU (<c>light_clusters.wgsl</c>, one workgroup a cell); <see cref="Prepare"/> works out what
    /// it needs each frame. <see cref="Build"/> is the same binning on the CPU, step for step, for the tests -- keep the
    /// two alike. The shaders find a pixel's cell as <see cref="CellOf"/> does (<c>include/point_lights.wgsl</c>).
    /// </para>
    /// </summary>
    public sealed class ManaLightClusters
    {
        public const int TilesX = 16;
        public const int TilesY = 9;
        public const int Slices = 24;
        public const int CellCount = TilesX * TilesY * Slices;

        /// <summary>Light indices across all cells; a frame with more pairs than this leaves the last cells short.</summary>
        public const int MaxIndices = 1 << 19;

        /// <summary>The most lights one cell lists; more than this reaching one spot is far past what looks distinct.</summary>
        public const int MaxLightsPerCell = 256;

        // Depth slices are spaced exponentially, so near cells are thin and far ones deep; beyond this all share the last.
        private const float FarthestSlice = 1000.0f;

        /// <summary>Two per light: xyz position, w range; rgb colour (times intensity).</summary>
        public Vector4[] Lights { get; } = new Vector4[ManaRenderScene.MaxLocalLights * 2];

        /// <summary>Two per cell: where its lights start in <see cref="Indices"/>, and how many there are (CPU build only).</summary>
        public uint[] Cells { get; } = new uint[CellCount * 2];

        public uint[] Indices { get; } = new uint[MaxIndices];

        public int LightCount { get; private set; }

        public int IndexCount { get; private set; }

        /// <summary>The camera's look direction, the first depth slice's start, and slices per log-unit of depth.</summary>
        public Vector3 Forward { get; private set; } = -Vector3.UnitZ;

        public float Near { get; private set; } = 0.1f;

        public float SliceScale { get; private set; } = 1.0f;

        public Vector2 Viewport { get; private set; }

        /// <summary>World to view space, and clip back to view space -- what the GPU binning reads.</summary>
        public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;

        public Matrix4x4 InverseProjection { get; private set; } = Matrix4x4.Identity;

        /// <summary>The frame's slicing and lights, without binning them: the GPU does that.</summary>
        public void Prepare(IReadOnlyList<(Vector4 Position, Vector4 Color)> lights, ManaCameraData camera, Vector2 viewport)
        {
            Viewport = viewport;
            View = camera.View;
            Matrix4x4.Invert(camera.Projection, out var inverse);
            InverseProjection = inverse;
            Forward = Vector3.Normalize(new Vector3(-camera.View.M13, -camera.View.M23, -camera.View.M33));
            Near = MathF.Max(camera.NearClipPlane, 0.05f);
            // Not Math.Clamp: a planet-sized near plane can pass FarthestSlice, which Clamp throws on.
            var far = MathF.Max(MathF.Min(camera.FarClipPlane, FarthestSlice), Near * 2.0f);
            SliceScale = Slices / MathF.Log(far / Near);

            LightCount = Math.Min(lights.Count, ManaRenderScene.MaxLocalLights);
            for (var i = 0; i < LightCount; i++)
            {
                var (position, color) = lights[i];
                Lights[i * 2] = position;
                Lights[i * 2 + 1] = color;
            }
        }

        /// <summary><see cref="Prepare"/>, then the GPU's binning on the CPU: each cell's box against each light's sphere.</summary>
        public void Build(IReadOnlyList<(Vector4 Position, Vector4 Color)> lights, ManaCameraData camera, Vector2 viewport)
        {
            Prepare(lights, camera, viewport);
            var centres = new Vector3[LightCount];
            for (var i = 0; i < LightCount; i++)
            {
                var p = Lights[i * 2];
                centres[i] = Vector3.Transform(new Vector3(p.X, p.Y, p.Z), View);
            }

            var offset = 0;
            for (var cell = 0; cell < CellCount; cell++)
            {
                var (min, max) = CellBounds(cell % TilesX, cell / TilesX % TilesY, cell / (TilesX * TilesY));
                var count = 0;
                for (var i = 0; i < LightCount && count < MaxLightsPerCell && offset + count < MaxIndices; i++)
                {
                    var radius = Lights[i * 2].W;
                    if (radius > 0.0f && Vector3.DistanceSquared(Vector3.Clamp(centres[i], min, max), centres[i]) <= radius * radius)
                    {
                        Indices[offset + count] = (uint)i;
                        count++;
                    }
                }
                Cells[cell * 2] = (uint)offset;
                Cells[cell * 2 + 1] = (uint)count;
                offset += count;
            }
            IndexCount = offset;
        }

        /// <summary>
        /// A cell's box in view space (the camera looking down -z): its tile's four corner rays cut at the slice's near and
        /// far depths. Through the inverse projection, so orthographic views work too.
        /// </summary>
        public (Vector3 Min, Vector3 Max) CellBounds(int x, int y, int z)
        {
            var depth0 = 0.0f;
            if (z > 0)
            {
                depth0 = Near * MathF.Exp(z / SliceScale);
            }
            // The last slice holds everything beyond it.
            var depth1 = 1e7f;
            if (z < Slices - 1)
            {
                depth1 = Near * MathF.Exp((z + 1) / SliceScale);
            }

            // Tiles run left to right and top to bottom; NDC y is up.
            var left = -1.0f + 2.0f * x / TilesX;
            var right = -1.0f + 2.0f * (x + 1) / TilesX;
            var top = 1.0f - 2.0f * y / TilesY;
            var bottom = 1.0f - 2.0f * (y + 1) / TilesY;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var ndc in new[] { new Vector2(left, top), new Vector2(right, top), new Vector2(left, bottom), new Vector2(right, bottom) })
            {
                foreach (var depth in new[] { depth0, depth1 })
                {
                    var point = ViewPoint(ndc, depth);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }
            return (min, max);
        }

        // The view-space point at a depth along a pixel's ray: the line through two points the ray passes, cut at z = -depth.
        private Vector3 ViewPoint(Vector2 ndc, float depth)
        {
            var a4 = Vector4.Transform(new Vector4(ndc, 0.25f, 1.0f), InverseProjection);
            var b4 = Vector4.Transform(new Vector4(ndc, 0.75f, 1.0f), InverseProjection);
            var a = new Vector3(a4.X, a4.Y, a4.Z) / a4.W;
            var b = new Vector3(b4.X, b4.Y, b4.Z) / b4.W;
            var t = (-depth - a.Z) / (b.Z - a.Z);
            return a + (b - a) * t;
        }

        /// <summary>The cell a pixel (from the top left, in framebuffer pixels) at a view depth falls in -- as the shader works it out.</summary>
        public int CellOf(Vector2 pixel, float depth)
        {
            var x = Math.Clamp((int)MathF.Floor(pixel.X / Viewport.X * TilesX), 0, TilesX - 1);
            var y = Math.Clamp((int)MathF.Floor(pixel.Y / Viewport.Y * TilesY), 0, TilesY - 1);
            return x + y * TilesX + Slice(depth) * TilesX * TilesY;
        }

        /// <summary>The lights of a cell, as indices into <see cref="Lights"/> (two entries each).</summary>
        public ReadOnlySpan<uint> LightsOf(int cell) => Indices.AsSpan((int)Cells[cell * 2], (int)Cells[cell * 2 + 1]);

        private int Slice(float depth) =>
            Math.Clamp((int)MathF.Floor(MathF.Log(MathF.Max(depth, Near) / Near) * SliceScale), 0, Slices - 1);
    }
}
