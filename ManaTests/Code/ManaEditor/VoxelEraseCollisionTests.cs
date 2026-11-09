using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Every committed voxel gets its own kinematic collision body (ManaVoxelsNode.Shape_OnVoxelAdded), which
/// is what the placement raycast picks against. Before ManaVoxelShape.EraseRadius existed there was no such
/// thing as removing a voxel, so nothing ever removed the matching body either -- an erased voxel's
/// collider stayed behind, invisible but still solid, blocking the very raycast a second erase needs to
/// reach whatever is underneath. This exercises the fix (ManaVoxelShape.OnVoxelRemoved ->
/// ManaVoxelsNode.Shape_OnVoxelRemoved -> Context.Physics.RemoveBody) against a real headless physics
/// simulation -- no window or GPU needed, Bepu runs on the CPU either way.
/// </summary>
public static class VoxelEraseCollisionChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        context.Physics = new ManaPhysics(context);

        var node = new ManaVoxelsNode(context, Matrix4x4.Identity);
        node.UpdateTransform();

        var keptPosition = new Vector3(0.5f, 0.5f, 0.5f);
        var erasedPosition = new Vector3(3.5f, 0.5f, 0.5f);
        node.VoxelShape.AddVoxel(keptPosition, Colors.CornflowerBlue);
        node.VoxelShape.AddVoxel(erasedPosition, Colors.CornflowerBlue);
        node.VoxelShape.CommitData();

        Check("each committed voxel gets its own collision body",
            node.Components.OfType<ManaCollisionComponent>().Count() == 2,
            $"{node.Components.OfType<ManaCollisionComponent>().Count()}");

        bool RaycastHits(Vector3 targetPosition)
        {
            var origin = targetPosition + new Vector3(0.0f, 20.0f, 0.0f);
            var direction = Vector3.Normalize(targetPosition - origin);
            var hitHandler = new SingleHitHandler();
            context.Physics.Simulation.RayCast(origin, direction, 40.0f, context.Physics.BufferPool, ref hitHandler, 0);
            return hitHandler.ClosestHit.HasValue;
        }

        Check("a ray finds the voxel about to be erased", RaycastHits(erasedPosition));

        var erasedCount = node.VoxelShape.EraseRadius(erasedPosition, 0.6f);
        Check("erase actually removed the voxel", erasedCount == 1, $"{erasedCount}");

        Check("its collision body is removed along with it",
            node.Components.OfType<ManaCollisionComponent>().Count() == 1,
            $"{node.Components.OfType<ManaCollisionComponent>().Count()}");

        Check("a ray no longer finds anything where the erased voxel was",
            !RaycastHits(erasedPosition));

        Check("the untouched voxel's collider is still there",
            RaycastHits(keptPosition));
    }
}
