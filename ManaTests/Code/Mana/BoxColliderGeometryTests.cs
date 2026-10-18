using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

public static class BoxColliderGeometryChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        context.Physics = new ManaPhysics(context);
        var physics = context.Physics;
        try
        {
            var node = new ManaSceneNode(context, Matrix4x4.Identity) { Scale = new Vector3(2, 3, 4) };
            node.UpdateTransform();
            var box = new ManaBoxColliderComponent(node) { Size = new Vector3(3, 4, 5) };
            node.Components.Add(box);
            box.Center = new Vector3(1, 2, 3);
            node.Position = new Vector3(7, 8, 9);
            node.UpdateTransform();
            Check("collider center follows the node transform", physics.Simulation.Bodies[box.Body].Pose.Position
                == Vector3.Transform(box.Center, node.WorldTransform));
            node.Scale = new Vector3(-3, 2, 1);
            node.UpdateTransform();
            Check("rescaling a node updates its collider without negative dimensions", ShapeSize(box) == new Vector3(9, 8, 5));

            var meshNode = new ManaSceneNode(context, Matrix4x4.Identity) { Scale = new Vector3(2, 3, 4) };
            meshNode.Components.Add(new MeshRendererComponent(meshNode, new ManaCube(context, Vector4.One, new Vector3(2, 4, 6))));
            meshNode.UpdateTransform();
            var fitted = new ManaBoxColliderComponent(meshNode);
            meshNode.Components.Add(fitted);
            Check("fit visual bounds converts dimensions back into local coordinates", fitted.TryGetVisualBounds(out var fitSize, out _)
                && Vector3.Distance(fitSize, new Vector3(2, 4, 6)) < 0.001f);
            fitted.Size = new Vector3(-1, 0, 2);
            Check("collider dimensions remain positive", fitted.Size.X > 0 && fitted.Size.Y > 0);
        }
        finally
        {
            physics.Simulation.Dispose();
            physics.ThreadDispatcher.Dispose();
            physics.BufferPool.Clear();
        }
    }

    private static Vector3 ShapeSize(ManaBoxColliderComponent box) => box.Shape is Box shape
        ? new Vector3(shape.Width, shape.Height, shape.Length) : Vector3.Zero;
}
