using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>ColorCycle and Motion: animate what is shown, never what a level saves.</summary>
public static class StageAnimationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
        var context = new ManaContext(null!, null!, new ManaWorld());

        Check("hue 0 is red, a third green, two thirds blue",
            Near(ManaColorCycleComponent.HueToRgb(0.0f, 1.0f), new Vector3(1, 0, 0))
                && Near(ManaColorCycleComponent.HueToRgb(1.0f / 3.0f, 1.0f), new Vector3(0, 1, 0))
                && Near(ManaColorCycleComponent.HueToRgb(2.0f / 3.0f, 1.0f), new Vector3(0, 0, 1)));
        Check("saturation 0 is white", Near(ManaColorCycleComponent.HueToRgb(0.4f, 0.0f), Vector3.One));

        // A ring: one animator on the group fans its lamps round the wheel; a lamp with its own animator keeps it.
        var ring = new ManaSceneNode(context, Matrix4x4.Identity);
        var cycle = new ManaColorCycleComponent(ring) { Speed = 0.0f, Spread = 1.0f / 3.0f };
        ring.Components.Add(cycle);
        var lamps = new List<ManaGlowComponent>();
        for (var i = 0; i < 3; i++)
        {
            var bulb = new ManaSceneNode(context, Matrix4x4.Identity);
            var glow = new ManaGlowComponent(bulb) { Color = Vector4.One };
            bulb.Components.Add(glow);
            ring.AddChild(bulb);
            lamps.Add(glow);
        }
        var own = new ManaSceneNode(context, Matrix4x4.Identity);
        var ownLight = new ManaLightSourceComponent(own) { Color = Vector4.One };
        own.Components.Add(ownLight);
        own.Components.Add(new ManaColorCycleComponent(own) { Speed = 0.0f, Phase = 0.5f });
        ring.AddChild(own);
        cycle.UpdateComponent();

        Check("spread fans the lamps red, green, blue",
            Near(lamps[0].CurrentColor().AsVector3(), new Vector3(1, 0, 0))
                && Near(lamps[1].CurrentColor().AsVector3(), new Vector3(0, 1, 0))
                && Near(lamps[2].CurrentColor().AsVector3(), new Vector3(0, 0, 1)),
            string.Join(" ", lamps.Select(lamp => lamp.CurrentColor())));
        Check("the saved colour is left alone", lamps.All(lamp => lamp.Color == Vector4.One));
        Check("a lamp with its own animator is not taken over", ownLight.CurrentColor() == Vector4.One);

        var strobe = new ManaColorCycleComponent(ring) { Speed = 0.0f, PulseSpeed = 1.0f, PulseDepth = 1.0f };
        Check("a deep pulse goes dark half a beat in",
            strobe.ColorAt(0.5, 0).AsVector3().Length() < 1e-3f && strobe.ColorAt(0.0, 0).X > 0.99f);

        // Motion lays itself over the placed transform without writing into it.
        var head = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Position = new Vector3(5, 7, 0),
            Rotation = new Vector3(0, MathF.PI / 2.0f, 0),
        };
        var motion = new ManaMotionComponent(head) { MoveAxis = Vector3.UnitX, MoveDistance = 2.0f, MoveSpeed = 0.25f };
        head.Components.Add(motion);
        var swung = motion.LocalAt(1.0);
        Check("a glide follows the node's own turned axis",
            Near(swung.Translation, new Vector3(5, 7, -2)), swung.Translation.ToString());
        Check("motion at rest is the placed transform", Near(motion.LocalAt(0.0).Translation, head.Position));

        var spinner = new ManaSceneNode(context, Matrix4x4.Identity) { Position = new Vector3(1, 2, 3) };
        var spin = new ManaMotionComponent(spinner) { SpinAxis = Vector3.UnitY, SpinSpeed = 90.0f };
        spinner.Components.Add(spin);
        var quarter = spin.LocalAt(1.0);
        Check("a spin turns about its axis in place",
            Near(Vector3.TransformNormal(Vector3.UnitX, quarter), -Vector3.UnitZ) && Near(quarter.Translation, spinner.Position),
            Vector3.TransformNormal(Vector3.UnitX, quarter).ToString());
        spin.UpdateComponent();
        Check("updating leaves the saved position and rotation",
            spinner.Position == new Vector3(1, 2, 3) && spinner.Rotation == Vector3.Zero);
    }
}
