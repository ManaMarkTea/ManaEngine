using ManaEditor.Code.Levels;

namespace ManaTests;

/// <summary>
/// Walks a level's node tree the way the loader does: groups position their children, so a node's place in the world is
/// its own position plus every group above it. Sample Course is organised as Section > Category > item, and a check that
/// looks at "the level's nodes" has to see through that.
/// </summary>
internal static class LevelWalk
{
    /// <summary>
    /// Every node in the tree, groups included, as a copy whose <see cref="ManaLevelNode.Position"/> is its world
    /// position. Groups here have no rotation or scale (the loader would apply them; nothing authored uses them), so
    /// adding the positions is the whole job.
    /// </summary>
    public static List<ManaLevelNode> Flatten(IEnumerable<ManaLevelNode> nodes)
    {
        var flat = new List<ManaLevelNode>();
        Visit(nodes, new float[3], flat);
        return flat;
    }

    private static void Visit(IEnumerable<ManaLevelNode> nodes, float[] origin, List<ManaLevelNode> output)
    {
        foreach (var node in nodes)
        {
            var world = new[]
            {
                origin[0] + node.Position[0],
                origin[1] + node.Position[1],
                origin[2] + node.Position[2]
            };

            output.Add(new ManaLevelNode
            {
                Name = node.Name,
                Kind = node.Kind,
                Position = world,
                Rotation = node.Rotation,
                Scale = node.Scale,
                Shape = node.Shape,
                Asset = node.Asset,
                Color = node.Color,
                Size = node.Size,
                Collision = node.Collision,
                Water = node.Water,
                Character = node.Character,
                Scripts = node.Scripts
            });

            if (node.Children != null)
            {
                Visit(node.Children, world, output);
            }
        }
    }
}
