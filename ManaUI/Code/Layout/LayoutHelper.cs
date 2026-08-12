namespace ManaUI
{
    public static class LayoutHelper
    {
        /// <summary>An unconstrained measure axis. Elements must return a finite desired size regardless.</summary>
        public const float Infinity = float.PositiveInfinity;

        public static readonly Vector2 InfiniteSize = new Vector2(Infinity, Infinity);

        public static bool IsAuto(float value)
        {
            return float.IsNaN(value);
        }

        public static float Finite(float value, float fallback = 0.0f)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        public static Vector2 Finite(Vector2 value)
        {
            return new Vector2(Finite(value.X), Finite(value.Y));
        }

        public static Vector2 Max(Vector2 a, Vector2 b)
        {
            return new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        }

        public static Vector2 Min(Vector2 a, Vector2 b)
        {
            return new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        }

        /// <summary>Subtracts a thickness from an available size without letting either axis go negative.</summary>
        public static Vector2 Deflate(Vector2 size, Thickness thickness)
        {
            return new Vector2(
                MathF.Max(0.0f, size.X - thickness.Horizontal),
                MathF.Max(0.0f, size.Y - thickness.Vertical));
        }

        public static Vector2 Inflate(Vector2 size, Thickness thickness)
        {
            return new Vector2(size.X + thickness.Horizontal, size.Y + thickness.Vertical);
        }

        public static bool AreClose(float a, float b)
        {
            if (float.IsInfinity(a) || float.IsInfinity(b))
            {
                return a.Equals(b);
            }

            return MathF.Abs(a - b) < 0.0001f;
        }

        public static bool AreClose(Vector2 a, Vector2 b)
        {
            return AreClose(a.X, b.X) && AreClose(a.Y, b.Y);
        }
    }
}
