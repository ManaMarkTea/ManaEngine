using BepuPhysics;
using BepuPhysics.Collidables;
using Mana.Code.Nodes;

namespace Mana.Code.Components;

/// <summary>An authored box collider. Size and center are in the owner's local space.
/// The collider follows its node; it does not make the node a dynamic rigid body.</summary>
[EditorComponent(CanAdd = true, AllowMultiple = true)]
public sealed class ManaBoxColliderComponent : ManaCollisionComponent
{
    private Vector3 _size = Vector3.One;
    private Vector3 _physicsSize;

    public ManaBoxColliderComponent(ManaSceneNode owner) : base(owner)
    {
        Name = "Box Collider";
        ShowDebug = true;
        RestorePhysics();
    }

    public Vector3 Size
    {
        get => _size;
        set
        {
            if (!Finite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _size = Vector3.Max(value, new Vector3(0.001f));
            UpdateRegisteredShape();
        }
    }

    public Vector3 Center
    {
        get => Position;
        set
        {
            if (!Finite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Position = value;
            UpdateRegisteredShape();
        }
    }

    /// <summary>Recreates physics after undo. Does not attach to the owner's component list.</summary>
    public void RestorePhysics()
    {
        var physics = Context.Physics;
        if (physics == null) return;
        lock (Context.World.SceneLock)
        {
            if (physics.PhysicsMap.TryGetValue(Body, out var existing) && ReferenceEquals(existing, this)) return;
            _physicsSize = WorldSize();
            var box = new Box(_physicsSize.X, _physicsSize.Y, _physicsSize.Z);
            ShapeIndex = physics.Simulation.Shapes.Add(box);
            Shape = box;
            Body = physics.Simulation.Bodies.Add(BodyDescription.CreateKinematic(
                CreateBodyPose(Owner, Center), new CollidableDescription(ShapeIndex), new BodyActivityDescription()));
            physics.PhysicsMap[Body] = this;
        }
    }

    public bool TryGetVisualBounds(out Vector3 size, out Vector3 center)
    {
        size = _size;
        if (!ManaCollisionBounds.TryGetVisualBox(Owner, out var worldSize, out center)) return false;
        var scale = WorldScale();
        if (scale.X < 0.0001f || scale.Y < 0.0001f || scale.Z < 0.0001f) return false;
        size = Vector3.Max(worldSize / scale, new Vector3(0.001f));
        return true;
    }

    public override void UpdateComponent()
    {
        RestorePhysics();
        UpdateRegisteredShape();
    }

    private void UpdateRegisteredShape()
    {
        var physics = Context.Physics;
        if (physics == null) return;
        lock (Context.World.SceneLock)
        {
            // Property edits while detached must not silently resurrect a removed collider.
            if (!physics.PhysicsMap.TryGetValue(Body, out var existing) || !ReferenceEquals(existing, this)) return;
            var size = WorldSize();
            var body = physics.Simulation.Bodies[Body];
            if (size != _physicsSize)
            {
                var box = new Box(size.X, size.Y, size.Z);
                var next = physics.Simulation.Shapes.Add(box);
                body.SetShape(next);
                physics.Simulation.Shapes.RemoveAndDispose(ShapeIndex, physics.BufferPool);
                ShapeIndex = next;
                Shape = box;
                _physicsSize = size;
            }
            body.Pose = CreateBodyPose(Owner, Center);
            body.UpdateBounds();
        }
    }

    private Vector3 WorldScale()
    {
        Matrix4x4.Decompose(Owner.WorldTransform, out var scale, out _, out _);
        return Vector3.Abs(scale);
    }

    private Vector3 WorldSize() => Vector3.Max(_size * WorldScale(), new Vector3(0.001f));

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
