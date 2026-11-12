using Mana.Code.Nodes;
using Mana.Code.UI;

namespace ManaEditor.Code.UI
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

        private sealed record Entry(string Description, Action Undo, Action Redo)
        {
            // Set for an edit later ones can fold into (a slider dragged): what it edits, and when it last did.
            public object? MergeKey { get; init; }
            public long MergedAt { get; init; }
        }

        /// <summary>How long after one change to a value a further change still folds into the same undo step.</summary>
        public const int MergeWindowMilliseconds = 800;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string UndoDescription => CanUndo ? _undo.Peek().Description : string.Empty;
        public string RedoDescription => CanRedo ? _redo.Peek().Description : string.Empty;

        public void Record(string description, Action undo, Action redo)
        {
            Levels.SceneDirtyCache.Touch();
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

        /// <summary>
        /// Records a change to one value, folded into the last step when that was the same value (<paramref name="key"/>)
        /// changed under <see cref="MergeWindowMilliseconds"/> ago: a slider dragged, or a number nudged, is one undo
        /// step back to where it started rather than one per frame.
        /// </summary>
        public void RecordMerging(object key, string description, Action undo, Action redo)
        {
            var now = Environment.TickCount64;
            if (!_isApplying && _undo.TryPeek(out var top) && top.MergeKey != null && top.MergeKey.Equals(key)
                && now - top.MergedAt < MergeWindowMilliseconds)
            {
                Levels.SceneDirtyCache.Touch();
                _undo.Pop();
                _undo.Push(top with { Redo = redo, MergedAt = now });
                _redo.Clear();
                return;
            }
            Record(description, undo, redo);
            if (!_isApplying && _undo.TryPeek(out var recorded) && ReferenceEquals(recorded.Undo, undo))
            {
                _undo.Pop();
                _undo.Push(recorded with { MergeKey = key, MergedAt = now });
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

        public bool Undo()
        {
            Levels.SceneDirtyCache.Touch();
            return Apply(_undo, _redo, undo: true);
        }

        /// <summary>Removes an inspector component while retaining its instance and settings for undo.
        /// Authored box colliders recreate their physics registration when restored.</summary>
        public bool RemoveComponent(global::Mana.ManaComponent component, SceneSelectionState? selection)
        {
            if (!SceneEditorOperations.CanRemoveComponent(component)
                || component is global::Mana.Code.Components.ManaCollisionComponent
                    and not global::Mana.Code.Components.ManaBoxColliderComponent)
            {
                return false;
            }

            var owner = component.Owner;
            var world = owner.Context.World;
            var before = selection == null ? (EditorSelectionSnapshot?)null : EditorSelectionSnapshot.Capture(selection);
            int index;
            lock (world.SceneLock)
            {
                index = owner.Components.IndexOf(component);
                if (index < 0 || !SceneEditorOperations.RemoveComponent(selection, component, preserveForUndo: true))
                {
                    return false;
                }
            }

            Record($"Remove {component.GetType().Name}",
                () =>
                {
                    lock (world.SceneLock)
                    {
                        if (!owner.Components.Contains(component))
                        {
                            owner.Components.Insert(Math.Min(index, owner.Components.Count), component);
                            if (component is global::Mana.Code.Components.ManaBoxColliderComponent box) box.RestorePhysics();
                            if (world.IsPlaying) component.BeginPlay();
                        }
                    }
                    if (selection != null) before!.Value.Apply(selection, sceneGraphChanged: true);
                },
                () => SceneEditorOperations.RemoveComponent(selection, component, preserveForUndo: true));
            return true;
        }

        public void RecordComponentInsertion(global::Mana.ManaComponent component,
            SceneSelectionState? selection, EditorSelectionSnapshot? before)
        {
            var owner = component.Owner;
            var world = owner.Context.World;
            var index = owner.Components.IndexOf(component);
            Record($"Add {component.GetType().Name}",
                () =>
                {
                    SceneEditorOperations.RemoveComponent(selection, component, preserveForUndo: true);
                    if (selection != null && before.HasValue) before.Value.Apply(selection, sceneGraphChanged: true);
                },
                () =>
                {
                    lock (world.SceneLock)
                    {
                        if (!owner.Components.Contains(component))
                        {
                            owner.Components.Insert(Math.Clamp(index, 0, owner.Components.Count), component);
                            if (component is global::Mana.Code.Components.ManaBoxColliderComponent box) box.RestorePhysics();
                            if (world.IsPlaying) component.BeginPlay();
                        }
                    }
                    if (selection != null)
                    {
                        selection.SelectedScene = null;
                        selection.SelectedNode = owner;
                        selection.SelectedComponent = component;
                        selection.NotifySceneGraphChanged();
                    }
                });
        }

        public bool Redo()
        {
            Levels.SceneDirtyCache.Touch();
            return Apply(_redo, _undo, undo: false);
        }

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
