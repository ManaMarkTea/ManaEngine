using Mana;
using ManaUI;

namespace ManaTests;

public static class RotationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        CheckAxisRoundTrip("X pitch", new Vector3(45.0f.ToRadians(), 0.0f, 0.0f), check);
        CheckAxisRoundTrip("Y yaw", new Vector3(0.0f, 45.0f.ToRadians(), 0.0f), check);
        CheckAxisRoundTrip("Z roll", new Vector3(0.0f, 0.0f, 45.0f.ToRadians()), check);

        // A single-axis round trip can't catch an axis-order bug (e.g. XYZ vs ZYX composition) -- only a
        // combined rotation exercises the order the axes are applied in.
        CheckAxisRoundTrip("combined pitch+yaw+roll",
            new Vector3(30.0f.ToRadians(), 40.0f.ToRadians(), 50.0f.ToRadians()), check);
    }

    private static void CheckAxisRoundTrip(string name, Vector3 euler, Action<string, bool, string> check)
    {
        var quaternion = euler.ToQuaternionFromEuler();
        var roundTrip = quaternion.ToEuler().ToQuaternionFromEuler();

        var expected = Matrix4x4.CreateFromQuaternion(quaternion);
        var actual = Matrix4x4.CreateFromQuaternion(roundTrip);
        check($"quaternion/euler round-trip preserves {name}", NearlyEqual(expected, actual), FormatAxes(actual));
    }

    private static bool NearlyEqual(Matrix4x4 a, Matrix4x4 b)
    {
        return MathF.Abs(a.M11 - b.M11) < 0.001f
            && MathF.Abs(a.M12 - b.M12) < 0.001f
            && MathF.Abs(a.M13 - b.M13) < 0.001f
            && MathF.Abs(a.M21 - b.M21) < 0.001f
            && MathF.Abs(a.M22 - b.M22) < 0.001f
            && MathF.Abs(a.M23 - b.M23) < 0.001f
            && MathF.Abs(a.M31 - b.M31) < 0.001f
            && MathF.Abs(a.M32 - b.M32) < 0.001f
            && MathF.Abs(a.M33 - b.M33) < 0.001f;
    }

    private static string FormatAxes(Matrix4x4 matrix)
    {
        var x = Vector3.TransformNormal(Vector3.UnitX, matrix);
        var y = Vector3.TransformNormal(Vector3.UnitY, matrix);
        var z = Vector3.TransformNormal(Vector3.UnitZ, matrix);
        return $"X {Format(x)}, Y {Format(y)}, Z {Format(z)}";
    }

    private static string Format(Vector3 value) =>
        $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";
}
