using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaEditor.Code.Levels;
using ManaEditor.Code.UI;
using ManaUI;

namespace ManaTests;

/// <summary>Moving nodes in the scene graph, copy and paste, duplicate and delete -- each undone and redone.</summary>
public static class SceneNodeEditingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        context.Physics = new ManaPhysics(context);
        var scene = new ManaScene(context) { Name = "Editing" };
        world.Scenes.Add(scene);
        var level = new ManaLevel
        {
            Nodes =
            [
                new ManaLevelNode { Id = "a0000000000000000000000000000001", Name = "Holder", Kind = ManaLevelSourceKind.Group,
                    Position = [10, 0, 0], Scale = [2, 2, 2],
                    Children = [new ManaLevelNode { Id = "a0000000000000000000000000000002", Name = "Inner", Kind = ManaLevelSourceKind.Group, Position = [1, 0, 0] }] },
                new ManaLevelNode { Id = "a0000000000000000000000000000003", Name = "Crate", Kind = ManaLevelSourceKind.Group, Position = [0, 0, 5],
                    Scripts = [new ManaLevelScript { Id = "b0000000000000000000000000000001", Type = "BoxCollider" }] }
            ]
        };
        ManaLevelLoader.Load(level, context, scene, replaceAuthoredNodes: false);
        var holder = scene.Children.Single(node => node.Name == "Holder");
        var crate = scene.Children.Single(node => node.Name == "Crate");
        holder.UpdateTransform();
        crate.UpdateTransform();
        var selection = world.Selection;
        var history = new EditorUndoHistory();

        Check("a node cannot go under its own child",
            !SceneEditorOperations.CanMoveNode(world, holder, scene, holder.Children[0]) && !SceneEditorOperations.CanMoveNode(world, holder, scene, holder));

        var crateWorld = crate.WorldTransform.Translation;
        SceneNodeEditing.Move(world, selection, history, crate, scene, holder, 0);
        Check("a dragged node lands under its new parent, where it was in the world",
            crate.Parent == holder && holder.Children[0] == crate && Near(crate.WorldTransform.Translation, crateWorld)
                && Near(crate.Position, new Vector3(-5, 0, 2.5f)), $"{crate.Position}");
        history.Undo();
        Check("undo puts it back at the top, as it was",
            crate.Parent == null && scene.Children.IndexOf(crate) == 1 && Near(crate.Position, new Vector3(0, 0, 5)));
        history.Redo();
        Check("and redo moves it again", crate.Parent == holder && Near(crate.WorldTransform.Translation, crateWorld));
        history.Undo();

        var colliders = context.Physics.PhysicsMap.Count;
        SceneNodeEditing.Delete(world, selection, history, crate);
        Check("delete takes it out, and its collider's body", !scene.Children.Contains(crate) && context.Physics.PhysicsMap.Count == colliders - 1);
        history.Undo();
        Check("undo brings the same node back, collider and all",
            scene.Children.IndexOf(crate) == 1 && context.Physics.PhysicsMap.Count == colliders, $"index {scene.Children.IndexOf(crate)}, bodies {context.Physics.PhysicsMap.Count} of {colliders}");

        var copy = SceneNodeEditing.Duplicate(world, selection, history, crate);
        Check("duplicate puts a copy right after it, selected",
            copy != null && scene.Children.IndexOf(copy) == 2 && selection.SelectedNode == copy && copy.Name == "Crate"
                && copy.Components.OfType<ManaBoxColliderComponent>().Count() == 1);
        Check("with ids of its own", copy != null && copy.AuthoredId != crate.AuthoredId && !string.IsNullOrEmpty(copy.AuthoredId));
        history.Undo();
        Check("undo takes the copy away", copy != null && !scene.Children.Contains(copy) && context.Physics.PhysicsMap.Count == colliders, $"bodies {context.Physics.PhysicsMap.Count} of {colliders}");
        history.Redo();
        Check("redo brings that same copy back", copy != null && scene.Children.IndexOf(copy) == 2);

        var text = SceneNodeEditing.CaptureText(holder);
        selection.SelectedNode = crate;
        var pasted = text == null ? null : SceneNodeEditing.PasteText(world, selection, history, text);
        Check("paste puts a copy of the group, children and all, after the selection",
            pasted != null && scene.Children.IndexOf(pasted) == scene.Children.IndexOf(crate) + 1
                && pasted.Children.Count == 1 && pasted.Children[0].Name == "Inner"
                && pasted.Children[0].AuthoredId != holder.Children[0].AuthoredId);

        var undoCount = 0;
        history.RecordMerging("slider", "Change", () => undoCount--, () => undoCount++);
        history.RecordMerging("slider", "Change", () => undoCount -= 10, () => undoCount++);
        history.Undo();
        Check("a run of changes to one value is one undo step, back to where it started", undoCount == -1 && history.UndoDescription.StartsWith("Paste"));
    }
}
