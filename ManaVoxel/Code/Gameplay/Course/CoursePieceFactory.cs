using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// Ready-made course pieces for the editor's Add menu: a shape to see, the component that gives it
    /// meaning, and a trigger volume sized to match. Built the way <see cref="ManaShapeFactory"/> builds
    /// everything else, so they save into a level and load back unchanged.
    /// </summary>
    public static class CoursePieceFactory
    {
        public static readonly (string Label, Func<ManaShapeFactory, Vector3, ManaSceneNode> Build)[] Pieces =
        {
            ("Coin", CreateCoin),
            ("Checkpoint", CreateCheckpoint),
            ("Hazard", CreateHazard),
            ("Goal", CreateGoal),
            ("Moving Platform", CreateMovingPlatform),
            ("Enemy", CreateEnemy),
            ("Spring Pad", CreateSpringPad),
            ("Boost Pad", CreateBoostPad)
        };

        public static ManaSceneNode CreateCoin(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddIcosahedron(position + new Vector3(0.0f, 1.0f, 0.0f), new Vector4(1.0f, 0.82f, 0.15f, 1.0f), 0.6f);
            node.Name = "Coin";
            node.Components.Add(new CollectibleComponent(node)
            {
                Offset = Vector3.Zero,
                Size = new Vector3(1.5f, 1.5f, 1.5f)
            });
            return node;
        }

        public static ManaSceneNode CreateCheckpoint(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddCube(position + new Vector3(0.0f, 1.0f, 0.0f), new Vector4(0.3f, 0.6f, 1.0f, 1.0f), new Vector3(0.1f, 1.0f, 0.1f));
            node.Name = "Checkpoint";
            node.Components.Add(new CheckpointComponent(node));
            return node;
        }

        public static ManaSceneNode CreateHazard(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddCube(position + new Vector3(0.0f, 0.1f, 0.0f), new Vector4(0.85f, 0.15f, 0.15f, 1.0f), new Vector3(1.5f, 0.1f, 1.5f));
            node.Name = "Hazard";
            node.Components.Add(new HazardComponent(node)
            {
                Offset = new Vector3(0.0f, 0.5f, 0.0f),
                Size = new Vector3(3.0f, 1.0f, 3.0f)
            });
            return node;
        }

        public static ManaSceneNode CreateMovingPlatform(ManaShapeFactory shapes, Vector3 position)
        {
            var half = new Vector3(1.5f, 0.25f, 1.5f);
            var node = shapes.AddCube(position + new Vector3(0.0f, half.Y, 0.0f), new Vector4(0.75f, 0.4f, 1.0f, 1.0f), half);
            node.Name = "Moving Platform";

            // A solid box for the capsule to stand on; absent in a headless world, which has no physics.
            node.Context.Physics?.AddBoxBody(node, half * 2.0f, Vector3.Zero);
            node.Components.Add(new MovingPlatformComponent(node));
            return node;
        }

        public static ManaSceneNode CreateEnemy(ManaShapeFactory shapes, Vector3 position)
        {
            var half = new Vector3(0.6f, 0.6f, 0.6f);
            var node = shapes.AddCube(position + new Vector3(0.0f, half.Y, 0.0f), new Vector4(0.9f, 0.3f, 0.15f, 1.0f), half);
            node.Name = "Enemy";
            node.Components.Add(new EnemyComponent(node));
            return node;
        }

        public static ManaSceneNode CreateSpringPad(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddDisk(position + new Vector3(0.0f, 0.15f, 0.0f), new Vector4(0.2f, 0.85f, 0.35f, 1.0f), new Vector3(1.2f, 0.15f, 1.2f));
            node.Name = "Spring Pad";
            node.Components.Add(new SpringPadComponent(node)
            {
                // The pad is a low disk, so its volume sits from the floor up rather than around the node.
                Offset = new Vector3(0.0f, 0.4f, 0.0f)
            });
            return node;
        }

        public static ManaSceneNode CreateBoostPad(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddCube(position + new Vector3(0.0f, 0.05f, 0.0f), new Vector4(1.0f, 0.82f, 0.1f, 1.0f), new Vector3(1.4f, 0.05f, 1.6f));
            node.Name = "Boost Pad";
            node.Components.Add(new BoostPadComponent(node)
            {
                Offset = new Vector3(0.0f, 0.55f, 0.0f)
            });
            return node;
        }

        public static ManaSceneNode CreateGoal(ManaShapeFactory shapes, Vector3 position)
        {
            var node = shapes.AddCube(position + new Vector3(0.0f, 1.5f, 0.0f), new Vector4(0.2f, 0.9f, 0.4f, 1.0f), new Vector3(0.1f, 1.5f, 0.1f));
            node.Name = "Goal";
            node.Components.Add(new GoalComponent(node));
            return node;
        }
    }
}
