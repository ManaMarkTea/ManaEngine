using Mana;
using ManaUI;

namespace ManaTests;

public static class OrthographicNavigationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var camera = new Vector3(18, 12, 18);
        var forward = Vector3.Normalize(-camera);
        var distance = CameraComponent.OrthographicDepthDistance(80, 0.1f, 1000);
        var retreated = -forward * distance;
        var projection = Matrix4x4.CreateOrthographic(136, 80, 0.1f, 1000);
        var before = Matrix4x4.CreateLookAt(camera, Vector3.Zero, Vector3.UnitY) * projection;
        var after = Matrix4x4.CreateLookAt(retreated, Vector3.Zero, Vector3.UnitY) * projection;
        var point = new Vector4(40, 0, 40, 1);
        var oldClip = Vector4.Transform(point, before);
        var newClip = Vector4.Transform(point, after);
        check("near floor is clipped by a close orthographic camera", oldClip.Z < 0, oldClip.ToString());
        check("retreat puts the floor inside the depth range", newClip.Z > 0 && newClip.Z < 1, newClip.ToString());
        check("retreat preserves the orthographic framing", MathF.Abs(oldClip.X - newClip.X) < 0.0001f
            && MathF.Abs(oldClip.Y - newClip.Y) < 0.0001f, $"{oldClip} -> {newClip}");
        check("large ortho views keep the pivot within the far plane",
            CameraComponent.OrthographicDepthDistance(1000, 0.1f, 1000) < 1000, "far plane bounds the retreat");
    }
}
