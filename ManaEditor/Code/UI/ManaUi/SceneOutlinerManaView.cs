using Mana;
using ManaEditor.Code.Gameplay.Course;
using ManaEditor.Code.GridMaps;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaUI;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// The scene outliner rebuilt in ManaUI, over the live <see cref="ManaWorld"/> and sharing the editor's
    /// <see cref="SceneSelectionState"/> with the ImGui outliner, so selecting in either is reflected in
    /// both and in the viewport.
    /// <para>
    /// The tree mixes three unrelated types -- scenes, nodes and components -- which is why TreeView takes
    /// plain objects and a children selector rather than a node type of its own. Nothing here wraps the
    /// scene graph.
    /// </para>
    /// </summary>
    public static class SceneOutlinerManaView
    {
        private static readonly Vector4 SceneIconColor = new(0.72f, 0.72f, 0.78f, 1.0f);
        private static readonly Vector4 NodeIconColor = new(0.55f, 0.68f, 0.85f, 1.0f);
        private static readonly Vector4 MeshRendererIconColor = new(0.40f, 0.78f, 0.45f, 1.0f);
        private static readonly Vector4 LightSourceIconColor = new(0.95f, 0.80f, 0.25f, 1.0f);
        private static readonly Vector4 ComponentIconColor = new(0.60f, 0.60f, 0.65f, 1.0f);

        public static UiElement Build(
            UiTheme theme,
            ManaWorld world,
            SceneSelectionState selection,
            Action<ManaScene>? saveScene = null,
            Func<ManaScene, bool>? sceneHasChanges = null,
            EditorUndoHistory? history = null)
        {
            var prefabs = ProjectAssetViewModel.For(world);
            var tree = new TreeView
            {
                ItemsSource = world.Scenes,
                // A prefab being edited opens all the way down: it is small, and its insides are what is being edited.
                IsExpandedByDefault = _ => prefabs.IsEditing,
                ChildrenSelector = ChildrenOf,

                // Answered without materialising anything. ChildrenOf has to combine a node's children and
                // its components into one list, and the tree asks every visible row whether it has any --
                // so without this, a planet's worth of rows allocates a list each, every rebuild.
                HasChildrenSelector = HasChildren,

                // The scene graph raises nothing when it changes, so the tree watches a cheap summary
                // instead. Counting the top two levels is enough to notice nodes being added or removed
                // without walking the whole graph every frame.
                RowHeight = MathF.Max(theme.ControlHeight, 28.0f),
                ItemTemplate = item => BuildRow(theme, world, selection, item, saveScene, sceneHasChanges, history),
                StructureSignature = () => StructureSignature(world, selection)
            };

            tree.BindPolled(t => t.RevealedSelectedItem, () => CurrentSelectionItem(selection));
            tree.KeyDown += (_, e) =>
            {
                if (e.Key == UiKey.F2 && e.Modifiers == UiModifiers.None
                    && selection.SelectedNode is { } node && BeginRename(tree, node))
                {
                    e.Handled = true;
                }
            };

            // Followed rather than captured: opening a level or a prefab replaces the list, not its contents.
            tree.BindPolled(t => t.ItemsSource, () => prefabs.EditingScenes ?? (System.Collections.IEnumerable)world.Scenes);
            // The row takes the press to select; a drag of it starts from here (OutlinerActionRow.BeginDrag).
            tree.RowPressed += (_, e) =>
            {
                for (var element = e.Source; element != null; element = element.Parent)
                {
                    if (element is OutlinerActionRow row)
                    {
                        row.BeginPress(e.Position);
                        return;
                    }
                }
            };
            tree.SelectionChanged += (_, item) =>
            {
                if (!tree.IsApplyingBinding)
                {
                    ApplySelection(selection, item);
                }
            };

            // A Grid, not a StackPanel: a vertical stack measures its children with unbounded height, so a
            // scroll viewer inside one grows to fit its content and never scrolls. The star row hands the
            // viewer whatever is left over, which is what gives it something to scroll against.
            // No "OUTLINER" caption row: the dock tab immediately above says exactly that, and a panel that
            // repeats its own tab spends a row of height saying nothing. The tool row below carries the
            // scene count, which is the part that was actually worth reading.
            var prefabBar = new DynamicContent().Showing<object>(() => (object?)prefabs.EditingAsset ?? prefabs,
                _ => prefabs.IsEditing ? BuildPrefabBar(theme, prefabs) : null).GridRow(1);

            return new Grid().Rows("Auto, Auto, *", theme.Spacing).Columns("*").Padding(5.0f).Children(
                prefabBar,
                new StackPanel().Horizontal(theme.Spacing).GridRow(0).Children(
                    new Button().Label("Collapse all").Variant(ButtonVariant.Subtle)
                        .OnClick(tree.CollapseAll),
                    new Button().Label("🆕").Variant(ButtonVariant.Subtle)
                        .WithTooltip("New Scene — adds an empty scene. Move nodes into it from a node's right-click menu.")
                        .OnClick(() => SceneEditorOperations.CreateScene(world, selection)),
                    new TextBlock()
                        .BindTextPolled(() => (prefabs.EditingScenes ?? world.Scenes).Count is var count && count == 1 ? "1 scene" : $"{count} scenes")
                        .Foreground(theme.TextMuted)
                        .CenterV()),

                tree.Scrollable().Configure(viewer => viewer.MinHeight = 120.0f).GridRow(2));
        }

        /// <summary>Says a prefab is open, not the level, and how to get back.</summary>
        private static UiElement BuildPrefabBar(UiTheme theme, ProjectAssetViewModel prefabs)
        {
            return new Border().Fill(theme.Selected).Content(
                new StackPanel().Vertical(4.0f).Padding(6.0f).Children(
                    new TextBlock().Text($"Editing prefab: {prefabs.EditingName}").Foreground(theme.Text),
                    new TextBlock().Text(prefabs.EditingAsset?.Base is { } source
                            ? $"A variant of {Path.GetFileNameWithoutExtension(source.AssetPath)}: only what you change is kept."
                            : "Changes reach every placed copy when saved.")
                        .Wrap().Foreground(theme.TextMuted),
                    new StackPanel().Horizontal(4.0f).Children(
                        new Button().Label("Save").OnClick(() => prefabs.SavePrefab()),
                        new Button().Label("Save & Back").WithTooltip("Save the prefab, update its placed instances, and return to the level.")
                            .OnClick(() => prefabs.SavePrefab(true)),
                        new Button().Label("Discard").Variant(ButtonVariant.Subtle).OnClick(prefabs.DiscardEditing))));
        }

        private static bool HasChildren(object item)
        {
            return item switch
            {
                ManaScene scene => scene.Children.Any(node => !node.IsEditorTransient),
                ManaSceneNode node => node.Children.Any(child => !child.IsEditorTransient),
                _ => false
            };
        }

        private static object? CurrentSelectionItem(SceneSelectionState selection)
        {
            return (object?)selection.SelectedNode ?? selection.SelectedScene;
        }

        /// <summary>
        /// Children of any item in the tree. Components stay in the inspector; the outliner shows them as
        /// compact icons on their owning node instead of spending full hierarchy rows on them.
        /// </summary>
        private static System.Collections.IEnumerable? ChildrenOf(object item)
        {
            switch (item)
            {
                case ManaScene scene:
                    return scene.Children.Where(node => !node.IsEditorTransient);
                case ManaSceneNode node:
                    return node.Children.Where(child => !child.IsEditorTransient);
                default:
                    return null;
            }
        }

        private static UiElement BuildRow(
            UiTheme theme,
            ManaWorld world,
            SceneSelectionState selection,
            object item,
            Action<ManaScene>? saveScene,
            Func<ManaScene, bool>? sceneHasChanges,
            EditorUndoHistory? history)
        {
            var (_, color, muted, icon) = Describe(item);

            var labelRow = new Grid().Columns("Auto, *, Auto", 6.0f).Rows("*");
            labelRow.Children.Add(new OutlinerIcon(icon, color)
                .CenterV()
                .WithTooltip(IconTooltip(item))
                .GridColumn(0));
            var nameLabel = new TextBlock()
                    .BindTextPolled(() => Describe(item).Label)
                    .Foreground(muted ? theme.TextMuted : theme.Text)
                    .Ellipsis()
                    .CenterV();
            labelRow.Children.Add((item is ManaSceneNode node
                    ? new NodeNameEditor(node, selection, history) { Child = nameLabel }
                    : (UiElement)nameLabel)
                .GridColumn(1));

            if (InlineComponentIcons(item, theme) is { } inlineIcons)
            {
                labelRow.Children.Add(inlineIcons.GridColumn(2));
            }

            if (item is not ManaScene scene || saveScene == null || string.IsNullOrWhiteSpace(scene.LevelPath))
            {
                return new OutlinerActionRow(world, selection, item, labelRow, saveScene, history);
            }

            var saveButton = new Button()
                .Label(SaveLabel(scene, sceneHasChanges))
                .Variant(ButtonVariant.Subtle)
                .Height(MathF.Max(20.0f, theme.ControlHeight - 6.0f))
                .Configure(button =>
                {
                    button.Padding = new Thickness(6.0f, 1.0f);
                })
                .WithTooltip("Save this scene to its level JSON.")
                .OnClick(() => saveScene(scene));

            saveButton.BindPolled(button => button.Text, () => SaveLabel(scene, sceneHasChanges));

            var row = new Grid().Columns("*, Auto", theme.Spacing).Rows("*").Children(
                labelRow.GridColumn(0),
                saveButton.GridColumn(1));

            return new OutlinerActionRow(world, selection, item, row, saveScene, history);
        }

        private static bool BeginRename(UiElement element, ManaSceneNode node)
        {
            if (element is NodeNameEditor editor && ReferenceEquals(editor.Node, node))
            {
                editor.BeginRename();
                return true;
            }
            foreach (var child in element.VisualChildren)
            {
                if (BeginRename(child, node)) return true;
            }
            return false;
        }

        private sealed class NodeNameEditor(
            ManaSceneNode node, SceneSelectionState selection, EditorUndoHistory? history) : Decorator
        {
            public ManaSceneNode Node => node;

            public void BeginRename()
            {
                if (Child is TextBox) return;
                var label = Child;
                var before = node.Name;
                var box = new TextBox { Text = before };
                var finished = false;
                void Apply(string name)
                {
                    node.Name = name;
                    selection.NotifySceneGraphChanged();
                }
                void Finish(bool commit, bool returnFocus)
                {
                    if (finished) return;
                    finished = true;
                    if (commit && !string.IsNullOrWhiteSpace(box.Text) && box.Text != before)
                    {
                        var after = box.Text;
                        Apply(after);
                        history?.Record("Rename node", () => Apply(before), () => Apply(after));
                    }
                    // Move focus before removing the field so it cannot keep consuming editor shortcuts.
                    if (returnFocus)
                    {
                        for (var parent = Parent; parent != null; parent = parent.Parent)
                        {
                            if (parent is TreeView tree) { tree.Focus(); break; }
                        }
                    }
                    Child = label;
                }
                box.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key is UiKey.Escape or UiKey.Enter)
                    {
                        Finish(e.Key == UiKey.Enter, true);
                        e.Handled = true;
                    }
                };
                box.TextCommitted += (_, _) => Finish(true, false);
                Child = box;
                box.Focus();
            }
        }

        private static string SaveLabel(ManaScene scene, Func<ManaScene, bool>? sceneHasChanges)
        {
            return sceneHasChanges?.Invoke(scene) == true ? "Save*" : "Save";
        }

        private static (string Label, Vector4 Color, bool Muted, OutlinerIconKind Icon) Describe(object item)
        {
            return item switch
            {
                ManaScene scene => (Fallback(scene.Name, "Scene"), SceneIconColor, false, OutlinerIconKind.Scene),
                ManaSceneNode node => (
                    Fallback(node.Name, node.GetType().Name),
                    NodeColor(node),
                    false,
                    NodeIcon(node)),
                MeshRendererComponent => ("Mesh Renderer", MeshRendererIconColor, true, OutlinerIconKind.MeshRenderer),
                ManaLightSourceComponent => ("Light Source", LightSourceIconColor, true, OutlinerIconKind.Light),
                CameraComponent => ("Camera", ComponentIconColor, true, OutlinerIconKind.Camera),
                ManaComponent component => (ComponentDisplayName.For(component.GetType()), ComponentIconColor, true, OutlinerIconKind.Component),
                _ => (item.ToString() ?? "?", ComponentIconColor, true, OutlinerIconKind.Component)
            };
        }

        private static UiElement? InlineComponentIcons(object item, UiTheme theme)
        {
            if (item is not ManaSceneNode { Components.Count: > 0 } node)
            {
                return null;
            }

            const int maxIcons = 6;
            var count = Math.Min(node.Components.Count, maxIcons);
            var strip = new StackPanel().Horizontal(3.0f).CenterV();

            for (var i = 0; i < count; i++)
            {
                var component = node.Components[i];
                strip.Children.Add(new OutlinerIcon(ComponentIcon(component), ComponentColor(component), 10.0f)
                    .CenterV()
                    .WithTooltip(IconTooltip(component)));
            }

            if (node.Components.Count > maxIcons)
            {
                strip.Children.Add(new TextBlock()
                    .Text($"+{node.Components.Count - maxIcons}")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV()
                    .WithTooltip($"{node.Components.Count - maxIcons} more components"));
            }

            return strip;
        }

        private static OutlinerIconKind ComponentIcon(ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent => OutlinerIconKind.MeshRenderer,
                ManaLightSourceComponent => OutlinerIconKind.Light,
                CameraComponent => OutlinerIconKind.Camera,
                _ => OutlinerIconKind.Component
            };
        }

        private static Vector4 ComponentColor(ManaComponent component)
        {
            return component switch
            {
                MeshRendererComponent => MeshRendererIconColor,
                ManaLightSourceComponent => LightSourceIconColor,
                CameraComponent => ComponentIconColor,
                _ => ComponentIconColor
            };
        }

        private static Vector4 NodeColor(ManaSceneNode node)
        {
            return node.LevelSource?.Kind switch
            {
                ManaLevelSourceKind.Prefab => new Vector4(0.25f, 0.80f, 0.90f, 1.0f),
                ManaLevelSourceKind.Group => SceneIconColor,
                ManaLevelSourceKind.Shape => new Vector4(0.62f, 0.74f, 0.95f, 1.0f),
                ManaLevelSourceKind.Mesh => MeshRendererIconColor,
                ManaLevelSourceKind.Voxels => new Vector4(0.66f, 0.50f, 0.95f, 1.0f),
                ManaLevelSourceKind.PlayerStart => new Vector4(0.95f, 0.42f, 0.34f, 1.0f),
                ManaLevelSourceKind.Terrain => new Vector4(0.45f, 0.75f, 0.35f, 1.0f),
                _ => NodeIconColor
            };
        }

        private static OutlinerIconKind NodeIcon(ManaSceneNode node)
        {
            return node.LevelSource?.Kind switch
            {
                ManaLevelSourceKind.Prefab => OutlinerIconKind.Group,
                ManaLevelSourceKind.Group => OutlinerIconKind.Group,
                ManaLevelSourceKind.Shape => OutlinerIconKind.Shape,
                ManaLevelSourceKind.Mesh => OutlinerIconKind.Mesh,
                ManaLevelSourceKind.Voxels => OutlinerIconKind.Voxels,
                ManaLevelSourceKind.PlayerStart => OutlinerIconKind.PlayerStart,
                ManaLevelSourceKind.Terrain => OutlinerIconKind.Mesh,
                _ => OutlinerIconKind.Node
            };
        }

        private static string IconTooltip(object item)
        {
            return item switch
            {
                ManaScene scene when string.IsNullOrWhiteSpace(scene.LevelPath) => "Runtime scene",
                ManaScene => "Level scene",
                ManaSceneNode node when node.LevelSource != null => $"{node.LevelSource.Kind} node",
                ManaSceneNode => "Runtime node",
                MeshRendererComponent => "Mesh renderer",
                ManaLightSourceComponent => "Light source",
                CameraComponent => "Camera",
                ManaComponent component => component.GetType().Name,
                _ => item.GetType().Name
            };
        }

        private static string Fallback(string? name, string fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        /// <summary>
        /// Mirrors what the ImGui outliner does on click: a component selects itself and its owner, so
        /// node-level consumers keep working unchanged.
        /// </summary>
        private static void ApplySelection(SceneSelectionState selection, object? item)
        {
            switch (item)
            {
                case ManaScene scene:
                    selection.SelectedScene = scene;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = null;
                    break;
                case ManaSceneNode node:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = node;
                    break;
                case ManaComponent component:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = component;
                    selection.SelectedNode = component.Owner;
                    break;
                default:
                    selection.SelectedScene = null;
                    selection.SelectedComponent = null;
                    selection.SelectedNode = null;
                    break;
            }
        }

        /// <summary>
        /// A cheap stand-in for "has the graph changed". Deliberately shallow: walking the whole tree every
        /// frame would cost more than the rebuild it is trying to avoid, and adding or removing anything
        /// visible at the top two levels is what actually needs to be noticed.
        /// </summary>
        private static int StructureSignature(ManaWorld world, SceneSelectionState selection)
        {
            var signature = world.Scenes.Count * 397 + selection.SceneGraphRevision;

            foreach (var scene in world.Scenes)
            {
                signature = signature * 397 + scene.Children.Count(node => !node.IsEditorTransient);

                foreach (var node in scene.Children.Where(node => !node.IsEditorTransient))
                {
                    signature = signature * 397
                        + node.Children.Count(child => !child.IsEditorTransient)
                        + node.Components.Count;
                }
            }

            return signature;
        }

        private sealed class OutlinerActionRow : Decorator
        {
            private readonly ManaWorld _world;
            private readonly SceneSelectionState _selection;
            private readonly object _item;
            private readonly Action<ManaScene>? _saveScene;
            private readonly EditorUndoHistory? _history;

            public OutlinerActionRow(
                ManaWorld world,
                SceneSelectionState selection,
                object item,
                UiElement content,
                Action<ManaScene>? saveScene,
                EditorUndoHistory? history)
            {
                _world = world;
                _selection = selection;
                _item = item;
                _saveScene = saveScene;
                _history = history;
                Child = content;
                IsHitTestVisible = true;
                HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            protected override bool IsSelfHitTestVisible => true;

            // ---- Dragging a node onto another row: the top quarter puts it before, the bottom after, the middle inside.
            // The state is shared by every row: selecting the pressed row rebuilds the tree, so the row that saw the
            // press is gone a moment later, and whichever row has the pointer carries the drag on.

            private enum DropZone { Before, Into, After }

            private const float DragThreshold = 6.0f;

            private static ManaSceneNode? _pressedNode;
            private static Vector2 _pressedAt;
            private static bool _dragging;
            private static UiRoot? _dragRoot;
            private static UiDragGhost? _ghost;

            // The row the pointer is over during a drag, and where on it: drawn by that row, acted on at the release.
            private static OutlinerActionRow? _dropRow;
            private static DropZone _dropZone;
            private static bool _dropAllowed;

            /// <summary>A left press on this row, which the tree selected it for (TreeView.RowPressed).</summary>
            public void BeginPress(Vector2 position)
            {
                EndDrag();
                if (_item is not ManaSceneNode node || !SceneEditorOperations.CanDeleteNode(_world, node))
                {
                    return;
                }
                _pressedNode = node;
                _pressedAt = position;
                _dragRoot = Root;
                CapturePointer();
            }

            protected override void OnRenderOverlay(ManaUiDrawList drawList)
            {
                base.OnRenderOverlay(drawList);
                if (!ReferenceEquals(_dropRow, this) || Root is not { } root)
                {
                    return;
                }
                var colour = _dropAllowed ? root.Theme.Accent : root.Theme.Danger;
                var box = Bounds;
                switch (_dropZone)
                {
                    case DropZone.Before:
                        drawList.FillRect(new UiRect(box.X, box.Y - 1.0f, box.Width, 2.0f), colour);
                        break;
                    case DropZone.After:
                        drawList.FillRect(new UiRect(box.X, box.Bottom - 1.0f, box.Width, 2.0f), colour);
                        break;
                    default:
                        drawList.DrawBorder(box, colour, 1.5f, new CornerRadius(3.0f));
                        break;
                }
            }

            protected override void OnPointerMoved(UiPointerEventArgs e)
            {
                base.OnPointerMoved(e);
                if (_pressedNode is not { } node)
                {
                    return;
                }
                // The row that captured may have been rebuilt away; this one follows the gesture from here.
                if (!HasPointerCapture)
                {
                    CapturePointer();
                }
                if (!_dragging)
                {
                    if (Vector2.Distance(e.Position, _pressedAt) < DragThreshold)
                    {
                        return;
                    }
                    _dragging = true;
                    if (_dragRoot is { } root)
                    {
                        _ghost = new UiDragGhost(root);
                        _ghost.Show(new Border().Fill(root.Theme.SurfaceRaised).Outline(root.Theme.Accent, 1.0f)
                            .Rounded(root.Theme.CornerRadius).PaddingOf(8.0f, 4.0f)
                            .Content(new TextBlock().Text(Fallback(node.Name, node.GetType().Name))), e.Position);
                    }
                }
                _ghost?.Move(e.Position);
                TrackDropTarget(node, e.Position);
            }

            protected override void OnPointerReleased(UiPointerEventArgs e)
            {
                base.OnPointerReleased(e);
                if (_pressedNode is not { } node)
                {
                    return;
                }
                if (_dragging && _dropRow != null && _dropAllowed && _dropRow.DropPlace(node, _dropRow._item, _dropZone) is { } place)
                {
                    SceneNodeEditing.Move(_world, _selection, _history, node, place.Scene, place.Parent, place.Index);
                }
                ReleasePointerCapture();
                EndDrag();
            }

            private static void EndDrag()
            {
                _ghost?.Hide();
                _ghost = null;
                _dropRow?.InvalidateVisual();
                _dropRow = null;
                _pressedNode = null;
                _dragging = false;
                _dragRoot = null;
            }

            // Hit-tested rather than taken from the router, which reports the captured row for the whole gesture.
            private void TrackDropTarget(ManaSceneNode node, Vector2 position)
            {
                OutlinerActionRow? row = null;
                for (var element = _dragRoot?.HitTest(position.X, position.Y); element != null; element = element.Parent)
                {
                    if (element is OutlinerActionRow found)
                    {
                        row = found;
                        break;
                    }
                }
                _dropRow?.InvalidateVisual();
                _dropRow = row;
                if (row == null)
                {
                    return;
                }
                var share = (position.Y - row.Bounds.Y) / MathF.Max(1.0f, row.Bounds.Height);
                _dropZone = DropZone.Into;
                if (row._item is ManaSceneNode && share < 0.25f)
                {
                    _dropZone = DropZone.Before;
                }
                else if (row._item is ManaSceneNode && share > 0.75f)
                {
                    _dropZone = DropZone.After;
                }
                _dropAllowed = !ReferenceEquals(row._item, node) && row.DropPlace(node, row._item, _dropZone) is { } place
                    && SceneEditorOperations.CanMoveNode(_world, node, place.Scene, place.Parent);
                row.InvalidateVisual();
            }

            /// <summary>Where dropping <paramref name="node"/> on a row puts it, before it is taken out of where it is now.</summary>
            private SceneNodePlace? DropPlace(ManaSceneNode node, object target, DropZone zone)
            {
                switch (target)
                {
                    case ManaScene scene:
                        return new SceneNodePlace(scene, null, scene.Children.Count);
                    case ManaSceneNode other when zone == DropZone.Into:
                        if (SceneEditorOperations.FindOwningScene(_world, other) is not { } owner)
                        {
                            return null;
                        }
                        return new SceneNodePlace(owner, other, other.Children.Count);
                    case ManaSceneNode other:
                        if (SceneEditorOperations.PlaceOf(_world, other) is not { } beside)
                        {
                            return null;
                        }
                        var index = beside.Index;
                        if (zone == DropZone.After)
                        {
                            index++;
                        }
                        // Coming out of the same list from in front of the target shifts it back one.
                        var siblings = beside.Parent?.Children ?? beside.Scene.Children;
                        var current = siblings.IndexOf(node);
                        if (current >= 0 && current < index)
                        {
                            index--;
                        }
                        return beside with { Index = index };
                    default:
                        return null;
                }
            }

            protected override void OnPointerPressed(UiPointerEventArgs e)
            {
                if (e.Button != PointerButton.Right)
                {
                    return;
                }

                ApplySelection(_selection, _item);
                if (Root != null)
                {
                    MenuFlyout.ShowAt(Root, e.Position, BuildMenu());
                }

                e.Handled = true;
            }

            private List<MenuItem> BuildMenu()
            {
                return _item switch
                {
                    ManaScene scene => BuildSceneMenu(scene),
                    ManaSceneNode node => BuildNodeMenu(node),
                    _ => new List<MenuItem> { MenuItem.Label("No actions") }
                };
            }

            private List<MenuItem> BuildSceneMenu(ManaScene scene)
            {
                var items = new List<MenuItem>
                {
                    MenuItem.Label(Fallback(scene.Name, "Scene")),
                    MenuItem.Separator(),
                    BuildAddNodeMenu(scene, parent: null),
                    new MenuItem("Paste", () => Paste(scene))
                        .EnabledWhen(() => SceneNodeEditing.CanPaste)
                        .WithTooltip("Paste the copied node at the top of this scene (Ctrl+V).")
                };

                if (_saveScene != null && !string.IsNullOrWhiteSpace(scene.LevelPath))
                {
                    items.Add(MenuItem.Separator());
                    items.Add(new MenuItem("Save", () => _saveScene(scene))
                        .WithTooltip("Save this scene to its level JSON."));
                }

                return items;
            }

            private List<MenuItem> BuildNodeMenu(ManaSceneNode node)
            {
                var addComponent = new MenuItem("Add Component");
                foreach (var definition in SceneComponentCatalog.Definitions)
                {
                    addComponent.Items.Add(new MenuItem(
                            definition.Label,
                            () => SceneComponentCatalog.AddToNode(node, definition, _selection, _history))
                        .EnabledWhen(() => definition.CanAdd(node))
                        .WithTooltip(definition.Description));
                }

                var owningScene = FindOwningScene(node) ?? _world.DefaultScene;

                return new List<MenuItem>
                {
                    MenuItem.Label(Fallback(node.Name, node.GetType().Name)),
                    MenuItem.Separator(),
                    BuildAddNodeMenu(owningScene, node),
                    BuildMoveToSceneMenu(_world, _selection, node),
                    addComponent,
                    new MenuItem("Make Prefab", () => ProjectAssetViewModel.For(_world).CreatePrefab(node))
                        .EnabledWhen(() => ProjectAssetViewModel.For(_world).CanCreatePrefab(node)),
                    MenuItem.Separator(),
                    new MenuItem("Copy", () => SceneNodeEditing.Copy(node)).WithTooltip("Ctrl+C"),
                    new MenuItem("Paste", () => SceneNodeEditing.Paste(_world, _selection, _history))
                        .EnabledWhen(() => SceneNodeEditing.CanPaste)
                        .WithTooltip("Paste the copied node after this one (Ctrl+V)."),
                    new MenuItem("Duplicate", () => SceneNodeEditing.Duplicate(_world, _selection, _history, node))
                        .EnabledWhen(() => SceneEditorOperations.CanDeleteNode(_world, node))
                        .WithTooltip("A copy right after this one (Ctrl+D)."),
                    MenuItem.Separator(),
                    new MenuItem("Delete Node",
                            () => SceneNodeEditing.Delete(_world, _selection, _history, node))
                        .EnabledWhen(() => SceneEditorOperations.CanDeleteNode(_world, node))
                        .WithTooltip("Delete this node and its children from the scene (Delete).")
                };
            }

            private void Paste(ManaScene scene)
            {
                _selection.SelectedNode = null;
                _selection.SelectedComponent = null;
                _selection.SelectedScene = scene;
                SceneNodeEditing.Paste(_world, _selection, _history);
            }

            private MenuItem BuildAddNodeMenu(ManaScene scene, ManaSceneNode? parent) =>
                SceneAddNodeMenu.Submenu(_selection, scene, parent, parent?.WorldTransform.Translation ?? Vector3.Zero);

            private ManaScene? FindOwningScene(ManaSceneNode node)
            {
                foreach (var scene in _world.Scenes)
                {
                    if (Contains(scene.Children, node))
                    {
                        return scene;
                    }
                }

                return null;
            }

            private static bool Contains(IEnumerable<ManaSceneNode> nodes, ManaSceneNode target)
            {
                foreach (var candidate in nodes)
                {
                    if (ReferenceEquals(candidate, target) || Contains(candidate.Children, target))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// "Move to Scene": one row per scene in the world, the node's own greyed out. The list is read when
        /// the menu is built, which is when it opens, so a scene added a moment ago is already in it.
        /// </summary>
        public static MenuItem BuildMoveToSceneMenu(ManaWorld world, SceneSelectionState selection, ManaSceneNode node)
        {
            var menu = new MenuItem("Move to Scene")
                .WithTooltip("Moves this node, with its children, into another scene.");

            var number = 0;
            foreach (var scene in world.Scenes.ToList())
            {
                var target = scene;
                number++;

                menu.Items.Add(new MenuItem(
                        Fallback(scene.Name, $"Scene {number}"),
                        () => SceneEditorOperations.MoveNodeToScene(world, selection, node, target))
                    .EnabledWhen(() => SceneEditorOperations.CanMoveNodeToScene(world, node, target)));
            }

            return menu;
        }

        private enum OutlinerIconKind
        {
            Scene,
            Group,
            Shape,
            Mesh,
            Voxels,
            PlayerStart,
            Node,
            MeshRenderer,
            Light,
            Camera,
            Component
        }

        private sealed class OutlinerIcon : UiElement
        {
            private readonly OutlinerIconKind _kind;
            private readonly Vector4 _color;
            private readonly float _size;

            public OutlinerIcon(OutlinerIconKind kind, Vector4 color, float size = 14.0f)
            {
                _kind = kind;
                _color = color;
                _size = size;
            }

            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(_size, _size);

            protected override void OnRender(ManaUiDrawList drawList)
            {
                var box = Bounds.Deflate(new Thickness(1.0f));
                var soft = _color.WithAlpha(_color.W * 0.32f);

                switch (_kind)
                {
                    case OutlinerIconKind.Scene:
                        DrawScene(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Group:
                        DrawGroup(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Shape:
                        DrawShape(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Mesh:
                    case OutlinerIconKind.MeshRenderer:
                        DrawMesh(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Voxels:
                        DrawVoxels(drawList, box, _color);
                        break;
                    case OutlinerIconKind.PlayerStart:
                        DrawPlayerStart(drawList, box, _color);
                        break;
                    case OutlinerIconKind.Light:
                        DrawLight(drawList, box, _color, soft);
                        break;
                    case OutlinerIconKind.Camera:
                        DrawCamera(drawList, box, _color, soft);
                        break;
                    default:
                        DrawNode(drawList, box, _color, soft);
                        break;
                }
            }
        }

        private static Vector2 P(UiRect box, float x, float y)
        {
            return new Vector2(box.X + box.Width * x, box.Y + box.Height * y);
        }

        private static void DrawScene(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var page = UiRect.FromEdges(box.Left + 2.0f, box.Top + 1.0f, box.Right - 2.0f, box.Bottom - 1.0f);
            drawList.FillRect(page, soft, new CornerRadius(1.0f));
            drawList.DrawBorder(page, color, 1.2f, new CornerRadius(1.0f));
            drawList.DrawLine(P(page, 0.28f, 0.36f), P(page, 0.72f, 0.36f), color, 1.0f);
            drawList.DrawLine(P(page, 0.28f, 0.58f), P(page, 0.64f, 0.58f), color, 1.0f);
        }

        private static void DrawGroup(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var tab = UiRect.FromEdges(box.Left + 1.0f, box.Top + 3.0f, box.Left + box.Width * 0.48f, box.Top + 6.0f);
            var body = UiRect.FromEdges(box.Left + 1.0f, box.Top + 5.0f, box.Right - 1.0f, box.Bottom - 2.0f);
            drawList.FillRect(tab, soft, new CornerRadius(1.0f));
            drawList.FillRect(body, soft, new CornerRadius(1.5f));
            drawList.DrawBorder(body, color, 1.2f, new CornerRadius(1.5f));
        }

        private static void DrawShape(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var front = UiRect.FromEdges(box.Left + 3.0f, box.Top + 5.0f, box.Right - 3.0f, box.Bottom - 2.0f);
            drawList.FillRect(front, soft, new CornerRadius(1.0f));
            drawList.DrawBorder(front, color, 1.1f, new CornerRadius(1.0f));
            drawList.DrawLine(P(box, 0.28f, 0.38f), P(box, 0.5f, 0.16f), color, 1.1f);
            drawList.DrawLine(P(box, 0.72f, 0.38f), P(box, 0.5f, 0.16f), color, 1.1f);
            drawList.DrawLine(P(box, 0.28f, 0.38f), P(box, 0.72f, 0.38f), color, 1.1f);
        }

        private static void DrawMesh(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var a = P(box, 0.50f, 0.12f);
            var b = P(box, 0.86f, 0.50f);
            var c = P(box, 0.50f, 0.88f);
            var d = P(box, 0.14f, 0.50f);
            drawList.FillRect(UiRect.FromEdges(box.Left + 4.0f, box.Top + 4.0f, box.Right - 4.0f, box.Bottom - 4.0f), soft);
            drawList.DrawLine(a, b, color, 1.2f);
            drawList.DrawLine(b, c, color, 1.2f);
            drawList.DrawLine(c, d, color, 1.2f);
            drawList.DrawLine(d, a, color, 1.2f);
        }

        private static void DrawVoxels(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            var s = MathF.Max(2.0f, box.Width * 0.28f);
            drawList.FillRect(new UiRect(box.Left + 1.0f, box.Top + 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + box.Width - s - 1.0f, box.Top + 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + 1.0f, box.Top + box.Height - s - 1.0f, s, s), color, new CornerRadius(1.0f));
            drawList.FillRect(new UiRect(box.Left + box.Width - s - 1.0f, box.Top + box.Height - s - 1.0f, s, s), color, new CornerRadius(1.0f));
        }

        private static void DrawPlayerStart(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            drawList.DrawLine(P(box, 0.35f, 0.86f), P(box, 0.35f, 0.15f), color, 1.5f);
            drawList.DrawLine(P(box, 0.36f, 0.18f), P(box, 0.78f, 0.32f), color, 1.5f);
            drawList.DrawLine(P(box, 0.78f, 0.32f), P(box, 0.36f, 0.48f), color, 1.5f);
            drawList.FillRect(new UiRect(box.Left + 2.5f, box.Bottom - 2.5f, box.Width - 5.0f, 1.6f), color, new CornerRadius(0.8f));
        }

        private static void DrawLight(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var center = P(box, 0.5f, 0.5f);
            var radius = box.Width * 0.26f;
            var bulb = new UiRect(center.X - radius, center.Y - radius, radius * 2.0f, radius * 2.0f);
            drawList.FillRect(bulb, soft, new CornerRadius(radius));
            drawList.DrawBorder(bulb, color, 1.1f, new CornerRadius(radius));
            drawList.DrawLine(P(box, 0.5f, 0.05f), P(box, 0.5f, 0.20f), color, 1.0f);
            drawList.DrawLine(P(box, 0.5f, 0.80f), P(box, 0.5f, 0.95f), color, 1.0f);
            drawList.DrawLine(P(box, 0.05f, 0.5f), P(box, 0.20f, 0.5f), color, 1.0f);
            drawList.DrawLine(P(box, 0.80f, 0.5f), P(box, 0.95f, 0.5f), color, 1.0f);
        }

        private static void DrawCamera(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var body = UiRect.FromEdges(box.Left + 1.0f, box.Top + 4.0f, box.Right - 4.0f, box.Bottom - 3.0f);
            drawList.FillRect(body, soft, new CornerRadius(1.5f));
            drawList.DrawBorder(body, color, 1.1f, new CornerRadius(1.5f));
            drawList.DrawLine(P(box, 0.72f, 0.42f), P(box, 0.94f, 0.28f), color, 1.2f);
            drawList.DrawLine(P(box, 0.72f, 0.62f), P(box, 0.94f, 0.76f), color, 1.2f);
        }

        private static void DrawNode(ManaUiDrawList drawList, UiRect box, Vector4 color, Vector4 soft)
        {
            var node = box.Deflate(new Thickness(2.0f));
            drawList.FillRect(node, soft, new CornerRadius(2.0f));
            drawList.DrawBorder(node, color, 1.2f, new CornerRadius(2.0f));
        }
    }
}
