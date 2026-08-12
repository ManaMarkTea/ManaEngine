namespace ManaUI
{
    /// <summary>
    /// An axis-aligned rectangle in logical UI pixels, stored as origin + size. Y grows downward.
    /// </summary>
    public readonly struct UiRect : IEquatable<UiRect>
    {
        public static readonly UiRect Empty = default;

        /// <summary>The clip rect meaning "no clipping" -- large enough to never reject a real pixel.</summary>
        public static readonly UiRect Infinite = new UiRect(-1e7f, -1e7f, 2e7f, 2e7f);

        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public UiRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = MathF.Max(0.0f, width);
            Height = MathF.Max(0.0f, height);
        }

        public UiRect(Vector2 position, Vector2 size)
            : this(position.X, position.Y, size.X, size.Y)
        {
        }

        public static UiRect FromEdges(float left, float top, float right, float bottom)
        {
            return new UiRect(left, top, right - left, bottom - top);
        }

        public float Left => X;
        public float Top => Y;
        public float Right => X + Width;
        public float Bottom => Y + Height;

        public Vector2 Position => new Vector2(X, Y);
        public Vector2 Size => new Vector2(Width, Height);
        public Vector2 Center => new Vector2(X + Width * 0.5f, Y + Height * 0.5f);
        public bool IsEmpty => Width <= 0.0f || Height <= 0.0f;

        public bool Contains(Vector2 point)
        {
            return point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
        }

        public bool IntersectsWith(UiRect other)
        {
            return other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;
        }

        /// <summary>Shrinks by a thickness on each edge, clamping to zero rather than going negative.</summary>
        public UiRect Deflate(Thickness thickness)
        {
            return new UiRect(
                X + thickness.Left,
                Y + thickness.Top,
                Width - thickness.Horizontal,
                Height - thickness.Vertical);
        }

        public UiRect Inflate(Thickness thickness)
        {
            return new UiRect(
                X - thickness.Left,
                Y - thickness.Top,
                Width + thickness.Horizontal,
                Height + thickness.Vertical);
        }

        public UiRect Translate(Vector2 offset)
        {
            return new UiRect(X + offset.X, Y + offset.Y, Width, Height);
        }

        /// <summary>The overlap of two rects, or Empty when they do not touch. This is how nested clips compose.</summary>
        public UiRect Intersect(UiRect other)
        {
            var left = MathF.Max(Left, other.Left);
            var top = MathF.Max(Top, other.Top);
            var right = MathF.Min(Right, other.Right);
            var bottom = MathF.Min(Bottom, other.Bottom);

            if (right <= left || bottom <= top)
            {
                return Empty;
            }

            return FromEdges(left, top, right, bottom);
        }

        public UiRect Union(UiRect other)
        {
            if (IsEmpty)
            {
                return other;
            }

            if (other.IsEmpty)
            {
                return this;
            }

            return FromEdges(
                MathF.Min(Left, other.Left),
                MathF.Min(Top, other.Top),
                MathF.Max(Right, other.Right),
                MathF.Max(Bottom, other.Bottom));
        }

        public Vector4 ToEdgeVector()
        {
            return new Vector4(Left, Top, Right, Bottom);
        }

        public bool Equals(UiRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object? obj)
        {
            return obj is UiRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        public static bool operator ==(UiRect a, UiRect b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(UiRect a, UiRect b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"({X}, {Y}) {Width}x{Height}";
        }
    }
}
