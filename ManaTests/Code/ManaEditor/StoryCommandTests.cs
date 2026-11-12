using ManaEditor.Code.Automation;
using ManaUI;

namespace ManaTests;

/// <summary>The story commands' turning maths: a Kenney model faces +Z, and yaw turns it round +Y.</summary>
public static class StoryCommandChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;
        var origin = new Vector3(48, 0, 48);

        Check("facing +Z is yaw 0", Near(StoryCommands.YawToward(origin, origin + Vector3.UnitZ, 1.0f), 0.0f));
        var east = StoryCommands.YawToward(origin, origin + Vector3.UnitX * 3, 0.0f);
        Check("facing +X is a quarter turn", Near(east, MathF.PI / 2), $"{east}");
        // The yaw must turn the model's own +Z onto the way it faces.
        var turned = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(east, 0, 0));
        Check("that yaw points the model's front at the target", Vector3.Distance(turned, Vector3.UnitX) < 1e-4f, $"{turned}");
        Check("a target straight above keeps the old heading", Near(StoryCommands.YawToward(origin, origin + Vector3.UnitY, 0.7f), 0.7f));
    }
}
