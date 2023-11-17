using CSharpColorSpaceConverter;
using ImGuiNET;
using Silk.NET.Maths;
using System.Text;

namespace Mana
{
    public static class ImGuiExt
    {
        public static void InputMatrix4(string label, ref Matrix4x4 matrix)
        {
            Vector4 row1 = new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
            Vector4 row2 = new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
            Vector4 row3 = new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
            Vector4 row4 = new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44);

            ImGui.InputFloat4(label, ref row1, "%.1f");
            ImGui.InputFloat4("", ref row2, "%.1f");
            ImGui.InputFloat4("", ref row3, "%.1f");
            ImGui.InputFloat4("", ref row4, "%.1f");
        }
    }

    public static class Extensions
    {


        public static Matrix4x4 LookAt(Vector3 position, Vector3 target, Vector3 up)
        {
            Vector3 zaxis = Vector3.Normalize(target - position);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

            Matrix4x4 result = Matrix4x4.Identity;

            result.M11 = xaxis.X;
            result.M12 = yaxis.X;
            result.M13 = zaxis.X;

            result.M21 = xaxis.Y;
            result.M22 = yaxis.Y;
            result.M23 = zaxis.Y;

            result.M31 = xaxis.Z;
            result.M32 = yaxis.Z;
            result.M33 = zaxis.Z;

            result.M41 = -Vector3.Dot(xaxis, position);
            result.M42 = -Vector3.Dot(yaxis, position);
            result.M43 = -Vector3.Dot(zaxis, position);

            return result;
        }

        public static Vector4 ToColor4(this int value)
        {
            byte byte1 = (byte)((value >> 24) & 0xFF);
            byte byte2 = (byte)((value >> 16) & 0xFF);
            byte byte3 = (byte)((value >> 8) & 0xFF);
            byte byte4 = (byte)(value & 0xFF);
            return new Vector4(byte1 / 255.0f, byte2 / 255.0f, byte3 / 255.0f, byte4 / 255.0f);
        }

        public static Vector4 ToColor4(this uint value)
        {
            byte byte1 = (byte)((value >> 24) & 0xFF);
            byte byte2 = (byte)((value >> 16) & 0xFF);
            byte byte3 = (byte)((value >> 8) & 0xFF);
            byte byte4 = (byte)(value & 0xFF);
            return new Vector4(byte1 / 255.0f, byte2 / 255.0f, byte3 / 255.0f, byte4 / 255.0f);
        }

        public const float SmallValue = 0.0000001f;

        public static bool AlmostEquals(this Vector3 a, Vector3 b)
        {
            if (Vector3.DistanceSquared(a,b) < SmallValue)
            {
                return true;
            }

            return false;
        }

        public static bool AlmostEquals(this Vector4 a, Vector4 b)
        {
            if (Vector4.DistanceSquared(a, b) < SmallValue)
            {
                return true;
            }

            return false;
        }

        public static bool Contains(this string text, IEnumerable<string> possibleTexts)
        {
            foreach (var possibleText in possibleTexts)
            {
                if (text.Contains(possibleText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string ToHex(this byte[] bytes, bool upperCase)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
            }
            return result.ToString();
        }

        public static float Clamp(this float value, float min, float max)
        {
            return MathF.Min(MathF.Max(value, min), max);
        }

        public static float ToRadians(this float value)
        {
            return Scalar.DegreesToRadians<float>(value);
        }

        public static float ToDegrees(this float value)
        {
            return Scalar.RadiansToDegrees<float>(value);
        }

        public static Vector3 ToEulerDegrees(this Vector3 vector)
        {
            return new Vector3(vector.X.ToDegrees(), vector.Y.ToDegrees(), vector.Z.ToDegrees());
        }

        public static Vector3 ToEuler(this Quaternion r)
        {
            float yaw = MathF.Atan2(2.0f * (r.Y * r.W + r.X * r.Z), 1.0f - 2.0f * (r.X * r.X + r.Y * r.Y));
            float pitch = MathF.Asin(2.0f * (r.X * r.W - r.Y * r.Z));
            float roll = MathF.Atan2(2.0f * (r.X * r.Y + r.Z * r.W), 1.0f - 2.0f * (r.X * r.X + r.Z * r.Z));
            return new Vector3(yaw, pitch, roll);
        }

        public static Quaternion ToQuaternionFromEuler(this Vector3 eulerAngles)
        {
            //return Quaternion.CreateFromYawPitchRoll(eulerAngles.Y, eulerAngles.X, eulerAngles.Z);
            return Quaternion.CreateFromYawPitchRoll(eulerAngles.X, eulerAngles.Y, eulerAngles.Z);
        }

        public static Vector3 Transform(this Vector3 value, Matrix4x4 transform)
        {
            return Vector3.Transform(value, transform);
        }

        public static Vector3 TransformNormal(this Vector3 value, Matrix4x4 transform)
        {
            return Vector3.TransformNormal(value, transform);
        }

        public static Vector3 Normalized(this Vector3 value)
        {
            return Vector3.Normalize(value);
        }

        public static Vector3 ToVector3(this Vector4 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        public static Vector4D<float> ToVector4D(this Vector4 value)
        {
            return new Vector4D<float>(value.X, value.Y, value.Z, value.W);
        }

        public static Vector4 ToRgbFromHsl(this Vector4 hsv)
        {
            int[] rgb = ColorSpaceConverter.HSLToRGB(hsv.X / 360, hsv.Y, hsv.Z);
            return new Vector4(rgb[0] / 255.0f, rgb[1] / 255.0f, rgb[2] / 255.0f, hsv.W);
        }

        public static Matrix3X3<float> ToMatrix3(this Matrix4x4 matrix)
        {
            Matrix3X3<float> output = new Matrix3X3<float>
            (
                matrix.M11, matrix.M12, matrix.M13,
                matrix.M21, matrix.M22, matrix.M23,
                matrix.M31, matrix.M32, matrix.M33
            );

            return output;
        }

        public static TimeSpan Average(this IEnumerable<TimeSpan> spans) => TimeSpan.FromSeconds(spans.Select(s => s.TotalSeconds).Average());

        public static ReadOnlySpan<float> ToSpan(this Matrix4x4 matrix)
        {
            float[] floats = new float[]
            {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44
            };
            ReadOnlySpan<float> output = new ReadOnlySpan<float>(floats);
            return output;
        }

        public static ReadOnlySpan<float> ToSpan(this Matrix3X3<float> matrix)
        {
            float[] floats = new float[]
            {
                matrix.M11, matrix.M12, matrix.M13,
                matrix.M21, matrix.M22, matrix.M23,
                matrix.M31, matrix.M32, matrix.M33
            };
            ReadOnlySpan<float> output = new ReadOnlySpan<float>(floats);
            return output;
        }

        public static string ToPrettyMatrixString(this Matrix4x4 matrix)
        {
            return
$@"{matrix.M11.ToString("0.0")}, {matrix.M12.ToString("0.0")}, {matrix.M13.ToString("0.0")}, {matrix.M14.ToString("0.0")},
{matrix.M21.ToString("0.0")}, {matrix.M22.ToString("0.0")}, {matrix.M23.ToString("0.0")}, {matrix.M24.ToString("0.0")},
{matrix.M31.ToString("0.0")}, {matrix.M32.ToString("0.0")}, {matrix.M33.ToString("0.0")}, {matrix.M34.ToString("0.0")},
{matrix.M41.ToString("0.0")}, {matrix.M42.ToString("0.0")}, {matrix.M43.ToString("0.0")}, {matrix.M44.ToString("0.0")}";
        }
    }
}
