using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

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
            node.UpdateTransform();
            Check("collider center follows the node transform", physics.Simulation.Bodies[box.Body].Pose.Position
                == Vector3.Transform(box.Center, node.WorldTransform));
            node.Scale = new Vector3(-3, 2, 1);
            node.UpdateTransform();
            Check("rescaling a node updates its collider without negative dimensions", ShapeSize(box) == new Vector3(9, 8, 5));

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

            var saved = ManaLevelCapture.CaptureScene(world, scene, scene.Name);
            Check("multiple colliders are serialized once each", saved.Nodes.Single().Scripts?.Count(s => s.Type == "BoxCollider") == 2
                && saved.Nodes.Single().Collision == null);
            var roundTrip = ManaLevel.FromJson(saved.ToJson());
            var loadedScene = new ManaScene(context) { Name = "Loaded colliders" };
            world.Scenes.Add(loadedScene);
            var result = ManaLevelLoader.Load(roundTrip, context, loadedScene, replaceAuthoredNodes: false);
            var loaded = result.Nodes.Single().Components.OfType<ManaBoxColliderComponent>().ToArray();
            Check("save and reload restores multiple physics colliders", result.Warnings.Count == 0 && loaded.Length == 2
                && loaded[0].Size == box.Size && loaded[0].Center == box.Center && physics.PhysicsMap.Count == 4);

            var meshNode = new ManaSceneNode(context, Matrix4x4.Identity) { Scale = new Vector3(2, 3, 4) };
            meshNode.Components.Add(new MeshRendererComponent(meshNode, new ManaCube(context, Vector4.One, new Vector3(2, 4, 6))));
            meshNode.UpdateTransform();
            var fitted = new ManaBoxColliderComponent(meshNode);
            meshNode.Components.Add(fitted);
            Check("fit visual bounds converts dimensions back into local coordinates", fitted.TryGetVisualBounds(out var fitSize, out _)
                && Vector3.Distance(fitSize, new Vector3(2, 4, 6)) < 0.001f);
            fitted.Size = new Vector3(-1, 0, 2);
            Check("collider dimensions remain positive", fitted.Size.X > 0 && fitted.Size.Y > 0);

            var assetPath = Path.Combine(Path.GetTempPath(), $"mana-collider-{Guid.NewGuid():N}.gltf");
            try
            {
                File.WriteAllText(assetPath, """
                    {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],
                     "nodes":[{"name":"Model","children":[1]},{"name":"ChildMesh"}]}
                    """);
                var assetScene = new ManaScene(context) { Name = "Asset collision" };
                world.Scenes.Add(assetScene);
                var assetLevel = new ManaLevel
                {
                    Nodes = [new ManaLevelNode { Kind = ManaLevelSourceKind.Mesh, Name = "Model", Asset = assetPath }]
                };
                var assetRoot = ManaLevelLoader.Load(assetLevel, context, assetScene, replaceAuthoredNodes: false).Nodes.Single();
                ManaSceneNode FindChild(ManaSceneNode parent) => parent.Name == "ChildMesh" ? parent
                    : parent.Children.SelectMany(Descendants).First(child => child.Name == "ChildMesh");
                var assetChild = FindChild(assetRoot);
                assetChild.Components.Add(new ManaBoxColliderComponent(assetChild)
                    { Size = new Vector3(2, 3, 4), Center = new Vector3(0, 1, 0) });
                var capturedAsset = ManaLevel.FromJson(ManaLevelCapture.CaptureScene(world, assetScene, assetScene.Name).ToJson());
                Check("colliders on imported children are saved as scene overrides",
                    capturedAsset.Nodes.Single().ChildColliders?.Single().Boxes.Single().Size == new Vector3(2, 3, 4));
                var restoredScene = new ManaScene(context);
                world.Scenes.Add(restoredScene);
                var restoredAsset = ManaLevelLoader.Load(capturedAsset, context, restoredScene, replaceAuthoredNodes: false);
                var restoredBox = FindChild(restoredAsset.Nodes.Single()).Components.OfType<ManaBoxColliderComponent>().Single();
                Check("reloading a model restores its child collider", restoredAsset.Warnings.Count == 0
                    && restoredBox.Size == new Vector3(2, 3, 4) && restoredBox.Center == new Vector3(0, 1, 0));
                capturedAsset.Nodes.Single().ChildColliders![0].Names[^1] = "No longer exists";
                var changedHierarchy = ManaLevelLoader.Load(capturedAsset, context, new ManaScene(context), replaceAuthoredNodes: false);
                Check("changed mesh hierarchies report unmatched colliders instead of assigning them elsewhere",
                    changedHierarchy.Warnings.Any(warning => warning.Contains("hierarchy has changed")));
            }
            finally { File.Delete(assetPath); }
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

    private static IEnumerable<ManaSceneNode> Descendants(ManaSceneNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static IEnumerable<T> All<T>(UiElement element) where T : UiElement
    {
        if (element is T match) yield return match;
        for (var i = 0; i < element.VisualChildCount; i++)
            foreach (var child in All<T>(element.GetVisualChild(i))) yield return child;
    }
}
