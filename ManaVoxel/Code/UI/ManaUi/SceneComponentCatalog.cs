using Mana;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaVoxel.Code.Gameplay.Trains;

namespace ManaVoxel.Code.UI.ManaUi
{
    internal sealed class SceneComponentDefinition
    {
        public required string Label { get; init; }

        public required string Description { get; init; }

        public required Type ComponentType { get; init; }

        public bool AllowMultiple { get; init; }

        public required Func<ManaSceneNode, ManaComponent> Create { get; init; }

        public bool CanAdd(ManaSceneNode node)
        {
            return AllowMultiple || !node.Components.Any(ComponentType.IsInstanceOfType);
        }

        public override string ToString() => Label;
    }

    internal static class SceneComponentCatalog
    {
        public static IReadOnlyList<SceneComponentDefinition> Definitions { get; } =
            new List<SceneComponentDefinition>
            {
                new()
                {
                    Label = "Train Car",
                    Description = "Moves this node along the train loop during play.",
                    ComponentType = typeof(TrainCarComponent),
                    Create = node => new TrainCarComponent(node)
                },
                new()
                {
                    Label = "Light Source",
                    Description = "Adds an editable scene light to this node.",
                    ComponentType = typeof(ManaLightSourceComponent),
                    AllowMultiple = true,
                    Create = node => new ManaLightSourceComponent(node)
                },
                new()
                {
                    Label = "Pickup Motion",
                    Description = "Adds reflected script settings for spin and bob motion.",
                    ComponentType = typeof(ManaPickupMotionComponent),
                    Create = node => new ManaPickupMotionComponent(node)
                }
            };

        public static ManaComponent? AddToNode(
            ManaSceneNode node,
            SceneComponentDefinition definition,
            SceneSelectionState? selection)
        {
            if (!definition.CanAdd(node))
            {
                return null;
            }

            var component = definition.Create(node);
            var world = node.Context.World;
            lock (world.SceneLock)
            {
                node.Components.Add(component);
            }

            if (selection != null)
            {
                selection.SelectedScene = null;
                selection.SelectedNode = node;
                selection.SelectedComponent = component;
                selection.NotifySceneGraphChanged();
            }

            return component;
        }
    }
}
