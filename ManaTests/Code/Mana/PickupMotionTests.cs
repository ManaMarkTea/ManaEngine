using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

public static class PickupMotionChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        var context = new ManaContext(null!, null!, new ManaWorld());
        var donut = new ManaSceneNode(context, Matrix4x4.Identity);
        var motion = new ManaPickupMotionComponent(donut);
        // Bobbing moves the node, and the node is also editable in the Transform section above. Whichever
        // moved it last wins, so dragging a pickup somewhere else has to stick rather than being yanked
        // back to wherever it was first dropped.
        motion.Bobs = true;
        motion.UpdateComponent();
        donut.Position = new Vector3(10.0f, 5.0f, 10.0f);
        motion.UpdateComponent();
        motion.UpdateComponent();

        Check("moving a bobbing node re-homes it rather than snapping back",
            MathF.Abs(donut.Position.X - 10.0f) < 0.001f
                && MathF.Abs(donut.Position.Z - 10.0f) < 0.001f
                && MathF.Abs(donut.Position.Y - 5.0f) <= motion.BobHeight + 0.001f,
            donut.Position.ToString());

    }
}
