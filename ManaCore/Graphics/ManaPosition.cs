using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>A position or offset in double precision. Mesh vertices and transform bases still use floats.</summary>
    public readonly record struct ManaPosition(double X, double Y, double Z)
    {
        public ManaPosition(Vector3 value) : this(value.X, value.Y, value.Z) { }

        public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
        public ManaPosition Normalized() => this * (1.0 / Length());
        public static double Dot(ManaPosition a, ManaPosition b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static ManaPosition operator +(ManaPosition a, ManaPosition b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static ManaPosition operator -(ManaPosition a, ManaPosition b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static ManaPosition operator *(ManaPosition position, double scale) => new(position.X * scale, position.Y * scale, position.Z * scale);

        public ManaPosition TransformOffset(Matrix4x4 basis) => new(
            X * basis.M11 + Y * basis.M21 + Z * basis.M31,
            X * basis.M12 + Y * basis.M22 + Z * basis.M32,
            X * basis.M13 + Y * basis.M23 + Z * basis.M33);
    }
}
