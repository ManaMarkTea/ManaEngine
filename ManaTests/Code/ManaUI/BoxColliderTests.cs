using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

public static class BoxColliderChecks
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
            var scene = new ManaScene(context) { Name = "Collider test" };
            world.Scenes.Add(scene);
            var node = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = "Boxes", LevelSource = ManaLevelSource.Group,
                Scale = new Vector3(2, 3, 4)
            };
            scene.Children.Add(node);
            node.UpdateTransform();
            world.Selection.SelectedNode = node;
            var history = new EditorUndoHistory();
            var root = new UiRoot(fonts);
            var view = SceneInspectorManaView.Build(root.Theme, world.Selection, context, history: history);
            root.Content = view;
            void Layout() => root.UpdateLayout(new Vector2(460, 900));
            void AddBox()
            {
                var menu = All<ComboBox>(view).First(combo => combo.Placeholder == "Add component");
                menu.SelectedIndex = menu.Items.Select((item, index) => (item, index))
                    .First(pair => pair.item.ToString() == "Box Collider").index;
                Layout();
            }
            Layout();
            AddBox();
            var box = node.Components.OfType<ManaBoxColliderComponent>().Single();
            Check("adding a box creates a registered physics body", physics.PhysicsMap.TryGetValue(box.Body, out var registered)
                && ReferenceEquals(registered, box) && physics.Simulation.Bodies.BodyExists(box.Body));
            Check("a new box follows node scale", ShapeSize(box) == node.Scale);
            Check("authored collider has its own inspector card", All<Expander>(view).Count(e => e.Header == "Box Collider") == 1
                && !All<Expander>(view).Any(e => e.Header == "Collision"));
            history.Undo();
            Layout();
            Check("undo adding a collider removes its body and component", !node.Components.Contains(box) && physics.PhysicsMap.Count == 0);
            history.Redo();
            Layout();
            Check("redo adding a collider restores its registration", node.Components.Contains(box) && physics.PhysicsMap.Count == 1);

            var card = All<Expander>(view).Single(e => e.Header == "Box Collider");
            var sizeEditor = All<VectorBox>(card).First();
            sizeEditor.Value = new Vector3(3, 4, 5);
            sizeEditor.Field(0).Focus();
            root.Input.InjectKeyDown(UiKey.Enter);
            Check("editing collider size updates the actual physics shape", ShapeSize(box) == new Vector3(6, 12, 20));
            history.Undo();
            Check("undo resizing restores physics dimensions", box.Size == Vector3.One && ShapeSize(box) == node.Scale);
            history.Redo();
            Check("redo resizing restores authored dimensions", box.Size == new Vector3(3, 4, 5));
            root.FocusManager.Focus(null);
            box.Center = new Vector3(1, 2, 3);
            node.Position = new Vector3(7, 8, 9);
            node.Scale = new Vector3(-3, 2, 1);
            node.UpdateTransform();

            Layout();
            AddBox();
            Check("a node can carry multiple box colliders", node.Components.OfType<ManaBoxColliderComponent>().Count() == 2
                && physics.PhysicsMap.Count == 2);
            history.RemoveComponent(box, world.Selection);
            Check("removing one collider preserves other bodies", physics.PhysicsMap.Count == 1 && !node.Components.Contains(box));
            history.Undo();
            Check("undo removal restores collider dimensions and center", physics.PhysicsMap.Count == 2
                && node.Components.Contains(box) && box.Center == new Vector3(1, 2, 3) && ShapeSize(box) == new Vector3(9, 8, 5));
            history.Redo();
            history.Undo();
            Check("repeated collider undo does not duplicate bodies", physics.PhysicsMap.Count == 2);
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

    private static IEnumerable<T> All<T>(UiElement element) where T : UiElement
    {
        if (element is T match) yield return match;
        for (var i = 0; i < element.VisualChildCount; i++)
            foreach (var child in All<T>(element.GetVisualChild(i))) yield return child;
    }
}
