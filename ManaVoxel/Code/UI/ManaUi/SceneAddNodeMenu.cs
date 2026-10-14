using Mana;
using ManaVoxel.Code.Gameplay.Course;
using ManaVoxel.Code.GridMaps;
using Mana.Code;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// "Add Node" -- an empty group node or a primitive shape, the same construction <see cref="ManaShapeFactory"/> and
    /// <see cref="ManaLevelLoader"/> already use, so whatever gets created here saves and reloads correctly. Shared by the
    /// Outliner (under a node, or into a scene) and the viewport's right-click menu (at the point under the pointer).
    /// </summary>
    public static class SceneAddNodeMenu
    {
        private static readonly (string Label, Func<ManaShapeFactory, Vector3, ManaSceneNode> Build)[] ShapeBuilders =
        {
            ("Cube", (shapes, position) => shapes.AddCube(position, Colors.White, 1.0f)),
            ("Sphere", (shapes, position) => shapes.AddSphere(position, Colors.White, 1.0f)),
            ("Pyramid", (shapes, position) => shapes.AddPyramid(position, Colors.White, 1.0f)),
            ("Torus", (shapes, position) => shapes.AddTorus(position, Colors.White, 1.0f))
        };

        /// <summary>
        /// The menu's entries: added under <paramref name="parent"/> when there is one, or straight into
        /// <paramref name="scene"/>, at <paramref name="position"/> (world). With a <paramref name="surfaceNormal"/> the
        /// point is on a surface, and a shape is lifted half its size off it rather than sunk halfway in.
        /// </summary>
        public static List<MenuItem> Items(
            SceneSelectionState selection,
            ManaScene scene,
            ManaSceneNode? parent,
            Vector3 position,
            Vector3? surfaceNormal = null)
        {
            var items = new List<MenuItem>();
            var shapePosition = surfaceNormal is { } normal ? position + Vector3.Normalize(normal) * 0.5f : position;

            items.Add(new MenuItem("Empty Node", () =>
            {
                var node = new ManaSceneNode(scene.Context, Matrix4x4.CreateTranslation(position))
                {
                    Name = "Node",
                    LevelSource = ManaLevelSource.Group
                };
                Add(selection, scene, parent, node);
            }).WithTooltip("An empty node that can own children -- nothing to render on its own."));

            items.Add(MenuItem.Separator());

            foreach (var (label, build) in ShapeBuilders)
            {
                items.Add(new MenuItem(label, () => Built(selection, scene, parent, build(new ManaShapeFactory(scene.Context, scene), shapePosition))));
            }

            items.Add(MenuItem.Separator());

            items.Add(new MenuItem("Water", () =>
            {
                // 16 x 16 at a modest resolution: enough to see it ripple, small enough to drag around. Size and
                // resolution are editable in the inspector afterwards.
                var water = new ManaWaterNode(scene.Context, new Vector2(16.0f, 16.0f), 64)
                {
                    LevelSource = ManaLevelSource.ForWater(new Vector2(16.0f, 16.0f), 64)
                };
                water.Position = position;
                water.UpdateTransform();
                Add(selection, scene, parent, water);
            }).WithTooltip("A rippling water surface. Add a Hazard component to make it deadly, and set its size to match."));

            items.Add(new MenuItem("Grid Map", () =>
            {
                var gridMap = new ManaGridMapNode(scene.Context) { Position = position };
                gridMap.UpdateTransform();
                Add(selection, scene, parent, gridMap);
                GridMapEditing.Begin(gridMap);
            }).WithTooltip("A grid of tiles painted from a palette of models -- roads, dungeon rooms. Select it to paint."));

            foreach (var (label, build) in CoursePieceFactory.Pieces)
            {
                items.Add(new MenuItem($"Course: {label}", () => Built(selection, scene, parent, build(new ManaShapeFactory(scene.Context, scene), position)))
                    .WithTooltip("A ready-made play piece: a shape, its behaviour and a trigger volume."));
            }

            return items;
        }

        public static MenuItem Submenu(SceneSelectionState selection, ManaScene scene, ManaSceneNode? parent, Vector3 position)
        {
            var addNode = new MenuItem("Add Node");
            addNode.Items.AddRange(Items(selection, scene, parent, position));
            return addNode;
        }

        // The shape factory adds straight into the scene; a node asked for under a parent moves there.
        private static void Built(SceneSelectionState selection, ManaScene scene, ManaSceneNode? parent, ManaSceneNode node)
        {
            if (parent != null)
            {
                scene.Children.Remove(node);
                parent.AddChild(node);
            }

            Select(selection, node);
        }

        private static void Add(SceneSelectionState selection, ManaScene scene, ManaSceneNode? parent, ManaSceneNode node)
        {
            if (parent != null)
            {
                parent.AddChild(node);
            }
            else
            {
                scene.Children.Add(node);
            }

            Select(selection, node);
        }

        private static void Select(SceneSelectionState selection, ManaSceneNode node)
        {
            selection.SelectedScene = null;
            selection.SelectedNode = node;
            selection.SelectedComponent = null;
            selection.NotifySceneGraphChanged();
        }
    }
}
