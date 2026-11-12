using System.Text.Json;
using System.Text.RegularExpressions;
using Mana;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaEditor.Code.Levels;
using ManaUI;

namespace ManaEditor.Code.UI
{
    /// <summary>
    /// The scene graph edits the outliner and the keyboard share, each one undoable: moving a node (dragged in the
    /// outliner), copy, paste, duplicate and delete. A copy is the node as a level saves it (<see cref="ManaLevelNode"/>,
    /// scripts and prefab links and all), so a paste is built by the level loader exactly as opening it would be.
    /// </summary>
    public static class SceneNodeEditing
    {
        private const string ClipboardFormat = "mana-scene-nodes";

        // The last copy, for when the system clipboard is not ours to read (or someone copied text since).
        private static string? _lastCopy;

        /// <summary>A short line for the status bar about the last copy, paste or failure.</summary>
        public static string? LastMessage { get; private set; }

        public static bool Move(
            ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history,
            ManaSceneNode node, ManaScene scene, ManaSceneNode? parent, int index)
        {
            var beforeTransform = SceneNodeTransformState.Capture(node);
            if (SceneEditorOperations.PlaceOf(world, node) is not { } before
                || !SceneEditorOperations.MoveNode(world, selection, node, scene, parent, index))
            {
                return false;
            }
            // Captured after the move, where it now is, so redo puts it exactly there again.
            var after = SceneEditorOperations.PlaceOf(world, node)!.Value;
            var afterTransform = SceneNodeTransformState.Capture(node);
            EditorSelectionSnapshot.SelectNode(selection, node, sceneGraphChanged: true);
            history?.Record($"Move {node.Name}",
                () =>
                {
                    SceneEditorOperations.PutNode(world, selection, node, before);
                    beforeTransform.Apply(node);
                    EditorSelectionSnapshot.SelectNode(selection, node, sceneGraphChanged: true);
                },
                () =>
                {
                    SceneEditorOperations.PutNode(world, selection, node, after);
                    afterTransform.Apply(node);
                    EditorSelectionSnapshot.SelectNode(selection, node, sceneGraphChanged: true);
                });
            return true;
        }

        /// <summary>Deletes a node so that undo brings the same one back, where it was.</summary>
        public static bool Delete(ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history, ManaSceneNode? node)
        {
            if (node == null || SceneEditorOperations.PlaceOf(world, node) is not { } place)
            {
                return false;
            }
            var selectionBefore = EditorSelectionSnapshot.Capture(selection);
            if (!SceneEditorOperations.DetachNode(world, selection, node))
            {
                return false;
            }
            history?.Record($"Delete {node.Name}",
                () =>
                {
                    SceneEditorOperations.RestoreNode(world, selection, node, place);
                    selectionBefore.Apply(selection, sceneGraphChanged: true);
                },
                () => SceneEditorOperations.DetachNode(world, selection, node));
            return true;
        }

        public static bool Copy(ManaSceneNode? node)
        {
            if (node == null || CaptureText(node) is not { } json)
            {
                return false;
            }
            _lastCopy = json;
            TextBox.Clipboard.Text = json;
            LastMessage = $"Copied {node.Name}.";
            return true;
        }

        public static bool CanPaste => ReadClipboard() != null;

        /// <summary>
        /// Pastes what was copied after the selected node (beside it, under the same parent), or at the top of the selected
        /// scene, and selects it.
        /// </summary>
        public static ManaSceneNode? Paste(ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history)
        {
            if (ReadClipboard() is not { } json)
            {
                return null;
            }
            return PasteText(world, selection, history, json);
        }

        /// <summary><see cref="Paste"/> of a copy's text (<see cref="CaptureText"/>) rather than the clipboard's.</summary>
        public static ManaSceneNode? PasteText(ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history, string json)
        {
            if (PasteTarget(world, selection) is not { } place)
            {
                return null;
            }
            return Build(world, selection, history, json, place, "Paste");
        }

        /// <summary>A copy of the node right after it, selected.</summary>
        public static ManaSceneNode? Duplicate(ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history, ManaSceneNode? node)
        {
            if (node == null || CaptureText(node) is not { } json || SceneEditorOperations.PlaceOf(world, node) is not { } place)
            {
                return null;
            }
            place = OutsidePrefabs(world, place);
            return Build(world, selection, history, json, place with { Index = place.Index + 1 }, "Duplicate");
        }

        /// <summary>The node as copy and paste carry it, or null (with <see cref="LastMessage"/> saying why).</summary>
        public static string? CaptureText(ManaSceneNode node)
        {
            try
            {
                var entry = ManaLevelCapture.CaptureSubtree(node);
                var copy = new ClipboardNodes { Format = ClipboardFormat, Nodes = [entry] };
                return JsonSerializer.Serialize(copy, ManaLevel.SerializerOptions);
            }
            catch (InvalidDataException exception)
            {
                LastMessage = $"Cannot copy {node.Name}: {exception.Message}";
                return null;
            }
        }

        private static string? ReadClipboard()
        {
            var text = TextBox.Clipboard.Text;
            if (!string.IsNullOrEmpty(text) && text.Contains(ClipboardFormat, StringComparison.Ordinal))
            {
                return text;
            }
            return _lastCopy;
        }

        private static SceneNodePlace? PasteTarget(ManaWorld world, SceneSelectionState selection)
        {
            if (selection.SelectedNode is { } selected && SceneEditorOperations.PlaceOf(world, selected) is { } place)
            {
                place = OutsidePrefabs(world, place);
                return place with { Index = place.Index + 1 };
            }
            var scene = selection.SelectedScene
                ?? world.Scenes.LastOrDefault(each => each.LevelPath != null)
                ?? world.DefaultScene;
            if (scene == null)
            {
                return null;
            }
            return new SceneNodePlace(scene, null, scene.Children.Count);
        }

        // Beside a node inside a placed prefab would be inside the prefab, whose insides are its own: beside the prefab.
        private static SceneNodePlace OutsidePrefabs(ManaWorld world, SceneNodePlace place)
        {
            var probe = new ManaSceneNode(world.Context, Matrix4x4.Identity);
            while (place.Parent != null && !SceneEditorOperations.CanMoveNode(world, probe, place.Scene, place.Parent))
            {
                place = SceneEditorOperations.PlaceOf(world, place.Parent)!.Value;
            }
            return place;
        }

        private static ManaSceneNode? Build(
            ManaWorld world, SceneSelectionState selection, EditorUndoHistory? history, string json, SceneNodePlace place, string verb)
        {
            ClipboardNodes? copy;
            try
            {
                copy = JsonSerializer.Deserialize<ClipboardNodes>(WithFreshIds(json), ManaLevel.SerializerOptions);
            }
            catch (JsonException exception)
            {
                LastMessage = $"Nothing to paste: {exception.Message}";
                return null;
            }
            if (copy?.Nodes is not { Count: > 0 } nodes)
            {
                return null;
            }

            var staging = new ManaScene(world.Context);
            var result = ManaLevelLoader.Load(new ManaLevel { Nodes = nodes }, world.Context, staging, replaceAuthoredNodes: false);
            var root = staging.Children.FirstOrDefault();
            if (root == null)
            {
                LastMessage = $"{verb} failed: {string.Join("; ", result.Warnings)}";
                return null;
            }
            staging.Children.Remove(root);

            var selectionBefore = EditorSelectionSnapshot.Capture(selection);
            SceneEditorOperations.PutNode(world, selection, root, place);
            // Where it really landed (the index is clamped), for redo.
            place = SceneEditorOperations.PlaceOf(world, root) ?? place;
            EditorSelectionSnapshot.SelectNode(selection, root, sceneGraphChanged: true);
            LastMessage = $"{verb}d {root.Name}.";
            history?.Record($"{verb} {root.Name}",
                () =>
                {
                    SceneEditorOperations.DetachNode(world, selection, root);
                    selectionBefore.Apply(selection, sceneGraphChanged: true);
                },
                () =>
                {
                    SceneEditorOperations.RestoreNode(world, selection, root, place);
                    EditorSelectionSnapshot.SelectNode(selection, root, sceneGraphChanged: true);
                });
            return root;
        }

        /// <summary>
        /// Gives every node and script in the copy a new id, everywhere it is mentioned (a script naming another), so the
        /// paste is a thing of its own. Ids inside a placed prefab's overrides are the prefab's own and are left alone.
        /// </summary>
        private static string WithFreshIds(string json)
        {
            var copy = JsonSerializer.Deserialize<ClipboardNodes>(json, ManaLevel.SerializerOptions);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Collect(ManaLevelNode node)
            {
                if (!string.IsNullOrEmpty(node.Id))
                {
                    ids.Add(node.Id);
                }
                foreach (var script in node.Scripts ?? [])
                {
                    if (!string.IsNullOrEmpty(script.Id))
                    {
                        ids.Add(script.Id);
                    }
                }
                foreach (var child in node.Children ?? [])
                {
                    Collect(child);
                }
            }
            foreach (var node in copy?.Nodes ?? [])
            {
                Collect(node);
            }
            foreach (var id in ids)
            {
                json = Regex.Replace(json, $"\"{Regex.Escape(id)}\"", $"\"{Guid.NewGuid():N}\"");
            }
            return json;
        }

        private sealed class ClipboardNodes
        {
            public string Format { get; set; } = string.Empty;
            public List<ManaLevelNode> Nodes { get; set; } = new();
        }
    }
}
