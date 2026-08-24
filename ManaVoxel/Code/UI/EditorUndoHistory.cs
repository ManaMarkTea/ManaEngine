using Mana.Code.Nodes;
using Mana.Code.UI;

namespace ManaVoxel.Code.UI
{
    public readonly record struct SceneNodeTransformState(
        Vector3 Position,
        Vector3 Rotation,
        Vector3 Scale)
    {
        public static SceneNodeTransformState Capture(ManaSceneNode node) =>
            new(node.Position, node.Rotation, node.Scale);

        public void Apply(ManaSceneNode node)
        {
            node.Position = Position;
            node.Rotation = Rotation;
            node.Scale = Scale;
            node.UpdateTransform();
        }
    }

    public readonly record struct EditorSelectionSnapshot(
        global::Mana.ManaScene? Scene,
        ManaSceneNode? Node,
        global::Mana.ManaComponent? Component)
    {
        public static EditorSelectionSnapshot Capture(SceneSelectionState selection) =>
            new(selection.SelectedScene, selection.SelectedNode, selection.SelectedComponent);

        public void Apply(SceneSelectionState selection, bool sceneGraphChanged = false)
        {
            selection.SelectedScene = Scene;
            selection.SelectedNode = Node;
            selection.SelectedComponent = Component;
            if (sceneGraphChanged)
            {
                selection.NotifySceneGraphChanged();
            }
        }

        public static void SelectNode(
            SceneSelectionState selection,
            ManaSceneNode node,
            bool sceneGraphChanged = false)
        {
            selection.SelectedScene = null;
            selection.SelectedNode = node;
            selection.SelectedComponent = null;
            if (sceneGraphChanged)
            {
                selection.NotifySceneGraphChanged();
            }
        }
    }

    /// <summary>Bounded command history shared by editor tools, inspectors, and shortcuts.</summary>
    public sealed class EditorUndoHistory
    {
        private const int Capacity = 128;

        private readonly Stack<Entry> _undo = new();
        private readonly Stack<Entry> _redo = new();
        private bool _isApplying;

        private sealed record Entry(string Description, Action Undo, Action Redo);

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string UndoDescription => CanUndo ? _undo.Peek().Description : string.Empty;
        public string RedoDescription => CanRedo ? _redo.Peek().Description : string.Empty;

        public void Record(string description, Action undo, Action redo)
        {
            ArgumentNullException.ThrowIfNull(undo);
            ArgumentNullException.ThrowIfNull(redo);

            if (_isApplying)
            {
                return;
            }

            _undo.Push(new Entry(description, undo, redo));
            _redo.Clear();

            if (_undo.Count > Capacity)
            {
                var newestFirst = _undo.ToArray();
                _undo.Clear();
                for (var index = Math.Min(Capacity, newestFirst.Length) - 1; index >= 0; index--)
                {
                    _undo.Push(newestFirst[index]);
                }
            }
        }

        public void RecordTransform(
            ManaSceneNode node,
            SceneNodeTransformState before,
            SceneNodeTransformState after,
            string description,
            SceneSelectionState? selection = null)
        {
            if (before == after)
            {
                return;
            }

            Record(
                description,
                () => RestoreTransform(node, before, selection),
                () => RestoreTransform(node, after, selection));
        }

        public void RecordWorldInsertion(
            EditorWorldInsertion insertion,
            ManaSceneNode selectedNode,
            EditorSelectionSnapshot selectionBefore,
            string description,
            SceneSelectionState selection)
        {
            ArgumentNullException.ThrowIfNull(insertion);
            ArgumentNullException.ThrowIfNull(selectedNode);
            ArgumentNullException.ThrowIfNull(selection);

            if (insertion.IsEmpty)
            {
                return;
            }

            Record(
                description,
                () =>
                {
                    insertion.Remove();
                    selectionBefore.Apply(selection, sceneGraphChanged: true);
                },
                () =>
                {
                    insertion.Restore();
                    EditorSelectionSnapshot.SelectNode(selection, selectedNode, sceneGraphChanged: true);
                });
        }

        public bool Undo() => Apply(_undo, _redo, undo: true);

        public bool Redo() => Apply(_redo, _undo, undo: false);

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private bool Apply(Stack<Entry> source, Stack<Entry> destination, bool undo)
        {
            if (source.Count == 0 || _isApplying)
            {
                return false;
            }

            var entry = source.Pop();
            _isApplying = true;
            try
            {
                if (undo)
                {
                    entry.Undo();
                }
                else
                {
                    entry.Redo();
                }

                destination.Push(entry);
                return true;
            }
            catch
            {
                source.Push(entry);
                throw;
            }
            finally
            {
                _isApplying = false;
            }
        }

        private static void RestoreTransform(
            ManaSceneNode node,
            SceneNodeTransformState state,
            SceneSelectionState? selection)
        {
            state.Apply(node);
            if (selection == null)
            {
                return;
            }

            EditorSelectionSnapshot.SelectNode(selection, node);
        }
    }
}
