using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// A collision box follows its node even after the physics put it to sleep. A sleeping body's bounds are never
/// refreshed, so a lift that dozed off at the bottom stayed solid there while it was drawn rising -- the player
/// stood on nothing visible, then fell through when it woke inside them.
/// </summary>
public static class MovingColliderChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        var scaled = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Scaled hull", Scale = new Vector3(2, 3, 4), Position = new Vector3(10, 0, 0)
        };
        context.World.DefaultScene.Children.Add(scaled);
        scaled.UpdateTransform();
        Vector3[] points = [new(-2, 0, -4), new(2, 0, -4), new(-2, 6, -4), new(2, 6, -4),
            new(-2, 0, 4), new(2, 0, 4), new(-2, 6, 4), new(2, 6, 4)];
        var hull = context.Physics.AddConvexHullBody(scaled, points, new Vector3(0, 0.5f, 0));
        var hullBody = context.Physics.Simulation.Bodies[hull.Body];
        check("scaled hull center is stored in owner-local coordinates", Vector3.Distance(hull.Position, new Vector3(0, 1.5f, 0)) < 0.001f);
        check("scaled hull center is scaled only once in physics", Vector3.Distance(hullBody.Pose.Position, new Vector3(10, 4.5f, 0)) < 0.001f);
        scaled.Position = new Vector3(12, 2, 0);
        scaled.UpdateTransform();
        hull.UpdateComponent();
        check("scaled hull stays aligned after node synchronization", Vector3.Distance(hullBody.Pose.Position, new Vector3(12, 6.5f, 0)) < 0.001f);

        var lift = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Lift" };
        context.World.DefaultScene.Children.Add(lift);
        var box = context.Physics.AddBoxBody(lift, new Vector3(2, 1, 2), Vector3.Zero);
        var simulation = context.Physics.Simulation;

        for (var step = 0; step < 240 && simulation.Bodies[box.Body].Awake; step++)
        {
            simulation.Timestep(1.0f / 60.0f);
        }
        check("an untouched box falls asleep", !simulation.Bodies[box.Body].Awake);

        lift.Position = new Vector3(0, 3, 0);
        lift.UpdateTransform();
        box.UpdateComponent();
        simulation.Timestep(1.0f / 60.0f);

        var bounds = simulation.Bodies[box.Body].BoundingBox;
        check("moving its node wakes it", simulation.Bodies[box.Body].Awake);
        check("and its bounds -- what the player collides with -- go with it", bounds.Min.Y > 2.0f && bounds.Max.Y < 4.0f,
            $"{bounds.Min.Y:F2}..{bounds.Max.Y:F2}");
    }
}
