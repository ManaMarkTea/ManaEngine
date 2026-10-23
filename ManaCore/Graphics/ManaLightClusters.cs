using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>
    /// Clustered lighting: the view cut into cells -- tiles across the screen, slices in depth -- and for each the placed
    /// lights whose range reaches into it. A pixel then shades only the few lights near it rather than every light in
    /// the scene, so a level can hold thousands of lamps and torches. Built on the CPU each frame from the lights'
    /// spheres (cheap: each light touches only the cells its sphere covers); the shader finds its cell the same way
    /// (<c>include/point_lights.wgsl</c>, <see cref="CellOf"/>).
    /// </summary>
    public sealed class ManaLightClusters
    {
        public const int TilesX = 16;
        public const int TilesY = 9;
        public const int Slices = 24;
        public const int CellCount = TilesX * TilesY * Slices;

        /// <summary>Light indices across all cells; past it, a crowded frame drops what does not fit (and says so).</summary>
        public const int MaxIndices = 1 << 19;

        // Depth slices are spaced exponentially, so near cells are thin and far ones deep; beyond this all share the last.
        private const float FarthestSlice = 1000.0f;

        private readonly int[] _counts = new int[CellCount];
        private readonly int[] _cursors = new int[CellCount];
        private readonly List<(int X0, int X1, int Y0, int Y1, int Z0, int Z1)> _ranges = new();

        /// <summary>Two per light: xyz position, w range; rgb colour (times intensity).</summary>
        public Vector4[] Lights { get; } = new Vector4[ManaRenderScene.MaxLocalLights * 2];

        /// <summary>Two per cell: where its lights start in <see cref="Indices"/>, and how many there are.</summary>
        public uint[] Cells { get; } = new uint[CellCount * 2];

        public uint[] Indices { get; } = new uint[MaxIndices];

        public int LightCount { get; private set; }

        public int IndexCount { get; private set; }

        /// <summary>Whether this frame had more light-in-cell pairs than <see cref="MaxIndices"/>; the rest were dropped.</summary>
        public bool Overflowed { get; private set; }

        /// <summary>The camera's look direction, the first depth slice's start, and slices per log-unit of depth.</summary>
        public Vector3 Forward { get; private set; } = -Vector3.UnitZ;

        public float Near { get; private set; } = 0.1f;

        public float SliceScale { get; private set; } = 1.0f;

        public Vector2 Viewport { get; private set; }

        public void Build(IReadOnlyList<(Vector4 Position, Vector4 Color)> lights, ManaCameraData camera, Vector2 viewport)
        {
            Viewport = viewport;
            Forward = Vector3.Normalize(new Vector3(-camera.View.M13, -camera.View.M23, -camera.View.M33));
            Near = MathF.Max(camera.NearClipPlane, 0.05f);
            var far = Math.Clamp(camera.FarClipPlane, Near * 2.0f, FarthestSlice);
            SliceScale = Slices / MathF.Log(far / Near);
            var viewProjection = camera.View * camera.Projection;

            Array.Clear(_counts);
            _ranges.Clear();
            LightCount = Math.Min(lights.Count, ManaRenderScene.MaxLocalLights);
            for (var i = 0; i < LightCount; i++)
            {
                var (position, color) = lights[i];
                Lights[i * 2] = position;
                Lights[i * 2 + 1] = color;
                var range = Range(new Vector3(position.X, position.Y, position.Z), position.W, camera.Position, viewProjection);
                _ranges.Add(range);
                for (var z = range.Z0; z <= range.Z1; z++)
                {
                    for (var y = range.Y0; y <= range.Y1; y++)
                    {
                        for (var x = range.X0; x <= range.X1; x++)
                        {
                            _counts[Cell(x, y, z)]++;
                        }
                    }
                }
            }

            // Each cell's lights sit together; a frame with more pairs than there is room for keeps what fits.
            var offset = 0;
            Overflowed = false;
            for (var cell = 0; cell < CellCount; cell++)
            {
                var count = Math.Min(_counts[cell], MaxIndices - offset);
                if (count < _counts[cell])
                {
                    Overflowed = true;
                }
                Cells[cell * 2] = (uint)offset;
                Cells[cell * 2 + 1] = (uint)count;
                _cursors[cell] = 0;
                offset += count;
            }
            IndexCount = offset;

            for (var i = 0; i < _ranges.Count; i++)
            {
                var range = _ranges[i];
                for (var z = range.Z0; z <= range.Z1; z++)
                {
                    for (var y = range.Y0; y <= range.Y1; y++)
                    {
                        for (var x = range.X0; x <= range.X1; x++)
                        {
                            var cell = Cell(x, y, z);
                            if (_cursors[cell] < Cells[cell * 2 + 1])
                            {
                                Indices[Cells[cell * 2] + _cursors[cell]] = (uint)i;
                                _cursors[cell]++;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>The cell a pixel (from the top left, in framebuffer pixels) at a view depth falls in -- as the shader works it out.</summary>
        public int CellOf(Vector2 pixel, float depth)
        {
            var x = Math.Clamp((int)MathF.Floor(pixel.X / Viewport.X * TilesX), 0, TilesX - 1);
            var y = Math.Clamp((int)MathF.Floor(pixel.Y / Viewport.Y * TilesY), 0, TilesY - 1);
            return Cell(x, y, Slice(depth));
        }

        /// <summary>The lights of a cell, as indices into <see cref="Lights"/> (two entries each).</summary>
        public ReadOnlySpan<uint> LightsOf(int cell) => Indices.AsSpan((int)Cells[cell * 2], (int)Cells[cell * 2 + 1]);

        private static int Cell(int x, int y, int z) => x + y * TilesX + z * TilesX * TilesY;

        private int Slice(float depth) =>
            Math.Clamp((int)MathF.Floor(MathF.Log(MathF.Max(depth, Near) / Near) * SliceScale), 0, Slices - 1);

        // The cells a light's sphere can touch: the depth slices it spans, and the tiles its box covers on screen. An empty
        // range (X0 > X1) for a light wholly behind the camera or off screen.
        private (int X0, int X1, int Y0, int Y1, int Z0, int Z1) Range(Vector3 center, float radius, Vector3 eye, Matrix4x4 viewProjection)
        {
            var depth = Vector3.Dot(center - eye, Forward);
            if (radius <= 0.0f || depth + radius < Near)
            {
                return (1, 0, 1, 0, 1, 0);
            }

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            var whole = false;
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -radius : radius, (corner & 2) == 0 ? -radius : radius, (corner & 4) == 0 ? -radius : radius);
                var clip = Vector4.Transform(new Vector4(center + offset, 1.0f), viewProjection);
                // A corner at or behind the eye has no place on screen: the light may reach any tile.
                if (clip.W <= 1e-4f)
                {
                    whole = true;
                    break;
                }
                var ndc = new Vector2(clip.X, clip.Y) / clip.W;
                min = Vector2.Min(min, ndc);
                max = Vector2.Max(max, ndc);
            }

            int x0 = 0, x1 = TilesX - 1, y0 = 0, y1 = TilesY - 1;
            if (!whole)
            {
                if (min.X > 1.0f || max.X < -1.0f || min.Y > 1.0f || max.Y < -1.0f)
                {
                    return (1, 0, 1, 0, 1, 0);
                }
                // Tiles run left to right and top to bottom; screen y is up.
                x0 = Math.Clamp((int)MathF.Floor((min.X * 0.5f + 0.5f) * TilesX), 0, TilesX - 1);
                x1 = Math.Clamp((int)MathF.Floor((max.X * 0.5f + 0.5f) * TilesX), 0, TilesX - 1);
                y0 = Math.Clamp((int)MathF.Floor((0.5f - max.Y * 0.5f) * TilesY), 0, TilesY - 1);
                y1 = Math.Clamp((int)MathF.Floor((0.5f - min.Y * 0.5f) * TilesY), 0, TilesY - 1);
            }
            return (x0, x1, y0, y1, Slice(depth - radius), Slice(depth + radius));
        }
    }
}
