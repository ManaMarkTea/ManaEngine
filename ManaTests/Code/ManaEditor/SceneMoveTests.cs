using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Which scene things land in, and moving them between scenes afterwards.
/// <para>
/// The bug this guards: every model dragged in used to arrive in a scene of its own, because the glTF loader
/// wraps each file in one and the editor added it to the world as it was. A dozen props meant a dozen scenes.
/// </para>
/// </summary>
public static class SceneMoveChecks
{
    // The smallest glTF there is: one named node, no mesh, nothing to fetch. Enough to exercise where the
    // loader puts what it reads without needing a GPU or an asset on disk.
    private const string MinimalGltf =
        "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"Thing\"}]}";

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

        static (ManaWorld World, ManaContext Context) NewWorld()
        {
            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            world.Context = context;
            world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
            world.Scenes.Add(world.DefaultScene);
            return (world, context);
        }

        var file = Path.Combine(Path.GetTempPath(), $"mana-scene-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(file, MinimalGltf);

        try
        {
            // ------------------------------------------------------------ where a load lands
            var (world, _) = NewWorld();

            var loose = world.LoadFromGltf(file);
            Check("with no target, a load still gets the wrapper scene it always did (characters and thumbnails rely on it)",
                world.Scenes.Count == 2 && world.Scenes[1].Children.Contains(loose), $"{world.Scenes.Count} scenes");

            var (target, _) = NewWorld();
            var placed = target.LoadFromGltf(file, into: target.DefaultScene);
            Check("given a scene, it lands there and adds no scene of its own",
                target.Scenes.Count == 1 && target.DefaultScene.Children.Contains(placed),
                $"{target.Scenes.Count} scenes");
            Check("and it is the node the caller was handed back", placed.Name == "Thing", placed.Name);

            var many = target.LoadFromGltf(file, into: target.DefaultScene);
            Check("a second one joins the same scene rather than opening another",
                target.Scenes.Count == 1 && target.DefaultScene.Children.Count == 2 && !ReferenceEquals(placed, many),
                $"{target.Scenes.Count} scenes, {target.DefaultScene.Children.Count} nodes");

            var fromMemory = target.LoadFromGltfMemory(
                System.Text.Encoding.UTF8.GetBytes(MinimalGltf), "thing.gltf", into: target.DefaultScene);
            Check("the in-memory route does the same",
                target.Scenes.Count == 1 && target.DefaultScene.Children.Contains(fromMemory));

            // ------------------------------------------------------------ undo and redo of a placement
            var (undoWorld, undoContext) = NewWorld();
            var meshStart = undoWorld.Meshes.Count;
            var materialStart = undoWorld.Materials.Count;
            var sceneStart = undoWorld.Scenes.Count;
            var node = undoWorld.LoadFromGltf(file, into: undoWorld.DefaultScene);

            var insertion = EditorWorldInsertion.Capture(undoWorld, sceneStart, meshStart, materialStart, node);
            Check("a placement into an existing scene is still something undo has to take back", !insertion.IsEmpty);

            var history = new EditorUndoHistory();
            var before = EditorSelectionSnapshot.Capture(undoWorld.Selection);
            history.RecordWorldInsertion(insertion, node, before, "Place Thing", undoWorld.Selection);

            history.Undo();
            Check("undo takes the node back out of the scene, leaving the scene",
                undoWorld.Scenes.Count == 1 && !undoWorld.DefaultScene.Children.Contains(node));

            history.Redo();
            Check("redo puts it back in the same scene",
                undoWorld.Scenes.Count == 1 && undoWorld.DefaultScene.Children.Contains(node));

            // ------------------------------------------------------------ moving between scenes
            var (moveWorld, moveContext) = NewWorld();
            var other = new ManaScene(moveContext) { Name = "Level" };
            moveWorld.Scenes.Add(other);

            var mover = new ManaSceneNode(moveContext, Matrix4x4.CreateTranslation(3, 0, 4)) { Name = "Mover" };
            moveWorld.DefaultScene.Children.Add(mover);
            mover.UpdateTransform();

            Check("a node cannot be moved into the scene it is already in",
                !SceneEditorOperations.CanMoveNodeToScene(moveWorld, mover, moveWorld.DefaultScene));
            Check("but can be moved into another",
                SceneEditorOperations.CanMoveNodeToScene(moveWorld, mover, other));
            Check("and not into a scene the world does not have",
                !SceneEditorOperations.CanMoveNodeToScene(moveWorld, mover, new ManaScene(moveContext)));

            moveWorld.Selection.SelectedNode = mover;
            var moved = SceneEditorOperations.MoveNodeToScene(moveWorld, moveWorld.Selection, mover, other);
            Check("moving reports success and the node changes scene",
                moved && other.Children.Contains(mover) && !moveWorld.DefaultScene.Children.Contains(mover));
            Check("it is still selected", ReferenceEquals(moveWorld.Selection.SelectedNode, mover));
            Check("and stays exactly where it was",
                Near(mover.Position.X, 3.0f) && Near(mover.Position.Z, 4.0f), mover.Position.ToString());

            Check("moving it again to where it already is does nothing",
                !SceneEditorOperations.MoveNodeToScene(moveWorld, moveWorld.Selection, mover, other));

            // A child keeps its place on screen when it leaves its parent, not its place relative to it.
            var parent = new ManaSceneNode(moveContext, Matrix4x4.CreateTranslation(10, 0, 0)) { Name = "Parent" };
            var child = new ManaSceneNode(moveContext, Matrix4x4.CreateTranslation(1, 2, 0)) { Name = "Child" };
            parent.AddChild(child);
            moveWorld.DefaultScene.Children.Add(parent);
            parent.UpdateTransform();
            var worldBefore = child.WorldTransform.Translation;

            SceneEditorOperations.MoveNodeToScene(moveWorld, moveWorld.Selection, child, other);
            child.UpdateTransform();
            Check("a nested node leaves its parent and joins the scene's top level",
                child.Parent == null && other.Children.Contains(child) && !parent.Children.Contains(child));
            Check("without moving on screen",
                Near(child.WorldTransform.Translation.X, worldBefore.X)
                && Near(child.WorldTransform.Translation.Y, worldBefore.Y),
                $"{worldBefore} -> {child.WorldTransform.Translation}");

            // ------------------------------------------------------------ creating a scene
            var (createWorld, _) = NewWorld();
            var first = SceneEditorOperations.CreateScene(createWorld, createWorld.Selection);
            Check("a new scene is added to the world, empty, and named",
                createWorld.Scenes.Contains(first) && first.Children.Count == 0 && first.Name == "New Scene",
                first.Name);
            Check("and selected, so the outliner shows where it went",
                ReferenceEquals(createWorld.Selection.SelectedScene, first) && createWorld.Selection.SelectedNode == null);

            var second = SceneEditorOperations.CreateScene(createWorld, createWorld.Selection);
            var third = SceneEditorOperations.CreateScene(createWorld, null, "  Props ");
            var fourth = SceneEditorOperations.CreateScene(createWorld, null, "Props");
            Check("two are never given the same name",
                second.Name == "New Scene 2" && third.Name == "Props" && fourth.Name == "Props 2",
                $"{second.Name} / {third.Name} / {fourth.Name}");
            Check("creating one without a selection to update is fine", createWorld.Scenes.Count == 5);

            var placedInNew = new ManaSceneNode(createWorld.Context, Matrix4x4.Identity) { Name = "Prop" };
            createWorld.DefaultScene.Children.Add(placedInNew);
            Check("what was just created is somewhere a node can be moved to",
                SceneEditorOperations.MoveNodeToScene(createWorld, createWorld.Selection, placedInNew, first)
                && first.Children.Contains(placedInNew));

            // The button itself: present in the outliner, and it does the same thing.
            var (buttonWorld, _) = NewWorld();
            var outliner = SceneOutlinerManaView.Build(new UiRoot(fonts).Theme, buttonWorld, buttonWorld.Selection);
            var newSceneButton = FindAll<Button>(outliner).FirstOrDefault(b => b.Tooltip?.StartsWith("New Scene") == true);
            Check("the outliner has a New Scene button", newSceneButton != null);

            if (newSceneButton != null)
            {
                typeof(Button).GetMethod("Invoke", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(newSceneButton, null);
                Check("pressing it adds a scene",
                    buttonWorld.Scenes.Count == 2 && buttonWorld.Scenes[1].Name == "New Scene",
                    $"{buttonWorld.Scenes.Count} scenes");
            }

            // ------------------------------------------------------------ the menu
            var menu = SceneOutlinerManaView.BuildMoveToSceneMenu(moveWorld, moveWorld.Selection, parent);
            var rows = menu.Items.ToList();

            Check("the menu has a row for every scene", rows.Count == moveWorld.Scenes.Count,
                string.Join(", ", rows.Select(r => r.Text)));
            Check("named after them", rows.Any(r => r.Text == "Level") && rows.Any(r => r.Text == "Default Scene"));

            var here = rows.First(r => r.Text == "Default Scene");
            var there = rows.First(r => r.Text == "Level");
            Check("the scene the node is already in is greyed out", !here.ResolveEnabled() && there.ResolveEnabled());

            there.Invoke();
            Check("choosing another moves the node there",
                other.Children.Contains(parent) && !moveWorld.DefaultScene.Children.Contains(parent));
            Check("and the rows follow: the old scene is now the one that is available",
                here.ResolveEnabled() && !there.ResolveEnabled());
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static List<T> FindAll<T>(UiElement? element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;
    }

    private static void Walk<T>(UiElement? element, List<T> found) where T : UiElement
    {
        if (element == null)
        {
            return;
        }

        if (element is T match)
        {
            found.Add(match);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Walk(element.GetVisualChild(i), found);
        }
    }
}
