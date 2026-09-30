using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// A collider is saved as it was meant: one sized to fit its model as "use visual bounds", one with a size of
/// its own as that size -- even when the two happen to match, which is what made a freshly opened level read
/// as changed.
/// </summary>
public static class ColliderIntentChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };
        context.World.Context = context;
        context.Physics = new ManaPhysics(context);
        context.World.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        context.World.Scenes.Add(context.World.DefaultScene);

        // Two cubes: one fitted to its visual bounds, one given exactly the same box as an explicit size.
        var level = new ManaLevel
        {
            Name = "Colliders",
            Nodes =
            [
                new ManaLevelNode
                {
                    Id = Guid.NewGuid().ToString("N"), Name = "Fitted", Kind = ManaLevelSourceKind.Shape, Shape = "Cube",
                    Position = [0, 0, 0], Rotation = [0, 0, 0], Scale = [1, 1, 1], Size = [1, 1, 1],
                    Collision = new ManaLevelCollision { UseVisualBounds = true }
                },
                new ManaLevelNode
                {
                    Id = Guid.NewGuid().ToString("N"), Name = "Sized", Kind = ManaLevelSourceKind.Shape, Shape = "Cube",
                    Position = [3, 0, 0], Rotation = [0, 0, 0], Scale = [1, 1, 1], Size = [1, 1, 1],
                    Collision = new ManaLevelCollision { Size = [1, 1, 1] }
                }
            ]
        };
        var result = ManaLevelLoader.Load(level, context, context.World.DefaultScene, replaceAuthoredNodes: false);
        var fitted = result.Nodes.First(n => n.Name == "Fitted").GetComponent<ManaCollisionComponent>();
        var sized = result.Nodes.First(n => n.Name == "Sized").GetComponent<ManaCollisionComponent>();
        check("a collider loaded to fit its model follows it", fitted?.FollowsVisualBounds == true, $"{fitted?.FollowsVisualBounds}");
        check("and one loaded with a size keeps it", sized?.FollowsVisualBounds == false, $"{sized?.FollowsVisualBounds}");

        var saved = ManaLevelCapture.Capture(context.World, "Colliders");
        var fittedSaved = saved.Nodes.First(n => n.Name == "Fitted").Collision;
        var sizedSaved = saved.Nodes.First(n => n.Name == "Sized").Collision;
        check("saved again, the fitted one still says use visual bounds", fittedSaved?.UseVisualBounds == true && fittedSaved.Size == null, "");
        check("and the sized one still has its size, though it matches the model", sizedSaved?.UseVisualBounds == false && sizedSaved.Size != null, "");

        // A group can be solid: a moving sprite platform stands the player on its box. It used to be dropped on load
        // (the player fell straight through) and again on save.
        var groupLevel = new ManaLevel
        {
            Name = "Group Collider",
            Nodes =
            [
                new ManaLevelNode
                {
                    Id = Guid.NewGuid().ToString("N"), Name = "Mover", Kind = ManaLevelSourceKind.Group,
                    Position = [10, 2, 0], Rotation = [0, 0, 0], Scale = [1, 1, 1],
                    Collision = new ManaLevelCollision { Size = [3, 1, 2], Offset = [0, 0.5f, 0] }
                }
            ]
        };
        var mover = ManaLevelLoader.Load(groupLevel, context, context.World.DefaultScene, replaceAuthoredNodes: false).Nodes.First(n => n.Name == "Mover");
        var moverBox = mover.GetComponent<ManaCollisionComponent>();
        check("a group's collision box is built when it loads", moverBox?.Shape is BepuPhysics.Collidables.Box { Width: 3, Height: 1, Length: 2 }, $"{moverBox?.Shape}");
        var moverSaved = ManaLevelCapture.Capture(context.World, "Group Collider").Nodes.First(n => n.Name == "Mover").Collision;
        check("and is still there when the level is saved", moverSaved?.Size is [3, 1, 2] && moverSaved.Offset is [0, 0.5f, 0],
            moverSaved?.Size == null ? "missing" : string.Join(",", moverSaved.Size));

        // A model that grows -- the rest of it finishing loading -- takes a following collider with it.
        var grown = result.Nodes.First(n => n.Name == "Fitted");
        grown.Scale = new Vector3(2, 2, 2);
        grown.UpdateTransform();
        ManaCollisionBounds.RefitAll(context.World);
        var refitted = grown.GetComponent<ManaCollisionComponent>();
        check("a collider that follows its model is refitted when the model grows",
            refitted?.Shape is BepuPhysics.Collidables.Box box && box.Width > 1.5f && refitted.FollowsVisualBounds == true,
            refitted?.Shape is BepuPhysics.Collidables.Box b ? $"{b.Width}" : "no box");
    }
}
