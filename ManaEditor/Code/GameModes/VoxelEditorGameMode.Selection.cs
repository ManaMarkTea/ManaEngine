using BepuPhysics.Collidables;
using ManaUI;
using System.Linq;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaEditor.Code.Services;
using ManaEditor.Code.Tools;
using ManaEditor.Code.Assets;
using ManaEditor.Code.Levels;
using ManaEditor.Code.Search;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    // Selecting in the viewport, and the shortcuts that act on the selection: F to frame, End to drop, Escape to clear.
    public partial class VoxelEditorGameMode
    {
        private bool _selectPointerWasDown;
        private bool _frameSelectedKeyWasDown;
        private bool _dropSelectedToFloorKeyWasDown;
        private bool _escapeSelectionKeyWasDown;

        // "F to frame selected" -- edge-detected the same way UpdateSelectionPointerInteraction debounces
        // a mouse click (a bool "was it down last frame" field), so holding F doesn't re-fire every frame.
        // Gated on WantCaptureKeyboard so it doesn't fire while typing in an ImGui text field (e.g.
        // renaming a node) that happens to contain the letter F.
        private void UpdateFrameSelectedShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.F);
            var pressed = keyDown && !_frameSelectedKeyWasDown;
            _frameSelectedKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            var selection = Context.World.Selection;
            Context.World.MainCamera.TryFrameSelection(selection.SelectedComponent as MeshRendererComponent, selection.SelectedNode);
        }

        // "End to drop to floor" -- same edge-detection idiom as the F shortcut above. Casts straight down
        // from the selection's own footprint and settles it onto whatever the ray finds first (the editor
        // grid, a voxel mesh, another prop), rather than only the literal ground plane.
        private void UpdateDropSelectedToFloorShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.End);
            var pressed = keyDown && !_dropSelectedToFloorKeyWasDown;
            _dropSelectedToFloorKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            DropSelectedToFloor();
        }

        private void DropSelectedToFloor()
        {
            var selection = Context.World.Selection;
            var node = selection.SelectedNode;
            if (node == null)
            {
                return;
            }

            if (!CameraComponent.TryComputeSelectionWorldBounds(
                selection.SelectedComponent as MeshRendererComponent, node, out var worldMin, out var worldMax))
            {
                return;
            }

            // The rays start just above the footprint's own bottom, not its centre: a raycast from the middle
            // of a tall object would sail past a floor that only reaches partway up its height, and from exactly
            // the bottom it would miss a floor the object has already sunk a hair into.
            var start = worldMin.Y + MathF.Min(0.05f, (worldMax.Y - worldMin.Y) * 0.25f);

            // Against the physics world rather than the scene picker used everywhere else: most placed
            // shapes/imports carry no collider at all (collision is opt-in, see the Inspector's Collision
            // section), so a physics-only raycast sailed straight through them to whatever collidable was
            // underneath -- usually the grid floor. ManaScenePicker ray-tests the actual mesh triangles
            // instead, which is exactly what decides whether something looks like it is resting on
            // something else. The whole subtree is excluded, not just the node itself, so a multi-mesh
            // selection (the lamp's post plus its head) cannot detect its own geometry as the floor. Every
            // IsEditorTransient node is excluded too -- the selection's own yellow collider-debug box (see
            // RebuildSelectionColliderDebug) sits almost exactly at these same bounds and is not part of
            // this subtree, so without this the ray immediately re-hit it and "dropped" by a hair each
            // press instead of ever reaching whatever is actually below.
            var excluded = new HashSet<ManaSceneNode>();
            CollectSubtree(node, excluded);
            foreach (var scene in Context.World.Scenes)
            {
                foreach (var child in scene.Children)
                {
                    CollectTransientNodes(child, excluded);
                }
            }

            // Five rays -- the centre and the footprint's corners, pulled in a little -- and the highest thing any
            // of them lands on wins, as Unreal's End does: a table half over a ledge rests on the ledge rather
            // than dropping through to whatever is under its middle.
            var centre = new Vector2((worldMin.X + worldMax.X) * 0.5f, (worldMin.Z + worldMax.Z) * 0.5f);
            var half = new Vector2(worldMax.X - worldMin.X, worldMax.Z - worldMin.Z) * 0.45f;
            float? landing = null;
            foreach (var corner in new[] { Vector2.Zero, new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                var at = centre + corner * half;
                if (ManaScenePicker.PickWithHit(Context.World.Scenes, new Vector3(at.X, start, at.Y), -Vector3.UnitY, excluded) is { } found)
                {
                    landing = MathF.Max(landing ?? float.MinValue, found.Position.Y);
                }
            }

            if (landing == null)
            {
                return;
            }

            var riseNeeded = landing.Value - worldMin.Y;
            if (MathF.Abs(riseNeeded) < 0.0001f)
            {
                return;
            }

            var worldDelta = new Vector3(0.0f, riseNeeded, 0.0f);
            if (node.Parent != null)
            {
                if (!Matrix4x4.Invert(node.Parent.WorldTransform, out var worldToParent))
                {
                    return;
                }

                worldDelta = Vector3.TransformNormal(worldDelta, worldToParent);
            }

            var before = SceneNodeTransformState.Capture(node);
            node.Position += worldDelta;
            node.UpdateTransform();
            var after = SceneNodeTransformState.Capture(node);
            _viewModel.History.RecordTransform(node, before, after, $"Drop {node.Name} to Floor", selection);
        }

        private static void CollectSubtree(ManaSceneNode node, HashSet<ManaSceneNode> into)
        {
            into.Add(node);
            foreach (var child in node.Children)
            {
                CollectSubtree(child, into);
            }
        }

        private static void CollectTransientNodes(ManaSceneNode node, HashSet<ManaSceneNode> into)
        {
            if (node.IsEditorTransient)
            {
                into.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectTransientNodes(child, into);
            }
        }

        // Escape backs all the way out: it already releases a possessed pawn (ManaPawnController) and
        // cancels an in-progress mesh-picker placement (HandleMeshPickerEscapeCancel) as two independent
        // edge-detected consumers of the same key -- this is the third, clearing whatever is selected and
        // handing control back to the Select tool rather than leaving the last tool active over nothing.
        private void UpdateEscapeSelectionShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }

            var keyDown = !UiWantsKeyboard && keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape);
            var pressed = keyDown && !_escapeSelectionKeyWasDown;
            _escapeSelectionKeyWasDown = keyDown;

            if (!pressed)
            {
                return;
            }

            Context.World.Selection.SelectedScene = null;
            Context.World.Selection.SelectedNode = null;
            Context.World.Selection.SelectedComponent = null;
            _viewModel.IsSelectToolActive = true;
        }

        private void UpdateSelectionPointerInteraction(bool inputCaptured, IMouse mouseState)
        {
            bool leftMouseDown = !inputCaptured && mouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            bool clicked = leftMouseDown && !_selectPointerWasDown;
            _selectPointerWasDown = leftMouseDown;

            if (!clicked)
            {
                return;
            }

            var mouseStart = new Vector3(mouseState.Position, 0.0f);
            var mouseEnd = new Vector3(mouseState.Position, 1.0f);
            var ray = Context.World.MainCamera.ProjectScreenToWorld(mouseStart, mouseEnd);
            var pickResult = ManaScenePicker.Pick(Context.World.Scenes, ray.Origin, ray.Direction, _pickingExclusions);

            // Pawn clicks get first refusal: clicking a character possesses it (and selects the pawn
            // root rather than the body-mesh child the ray actually hit), and a follow-up click
            // elsewhere is a walk order that must not clear the selection out from under it.
            var pawnOutcome = _pawnController?.HandleClick(pickResult?.Node, ray.Origin, ray.Direction)
                ?? ManaPawnController.ClickOutcome.Ignored;

            if (pawnOutcome == ManaPawnController.ClickOutcome.Ordered)
            {
                return;
            }

            if (pawnOutcome == ManaPawnController.ClickOutcome.Possessed)
            {
                Context.World.Selection.SelectedScene = null;
                Context.World.Selection.SelectedNode = _pawnController?.PossessedPawn;
                Context.World.Selection.SelectedComponent = null;
                return;
            }

            var selectedNode = ResolveEditorSelectableNode(pickResult?.Node, Context.World.Selection.SelectedNode);
            Context.World.Selection.SelectedScene = null;
            Context.World.Selection.SelectedNode = selectedNode;
            Context.World.Selection.SelectedComponent = ReferenceEquals(selectedNode, pickResult?.Node)
                ? pickResult?.Renderer
                : null;

            var now = Environment.TickCount64;
            object? clickedTarget = (object?)pickResult?.Renderer ?? pickResult?.Node;
            var isDoubleClick = clickedTarget != null
                && ReferenceEquals(clickedTarget, _lastSelectClickedTarget)
                && now - _lastSelectClickTimeMs <= DoubleClickThresholdMs;
            _lastSelectClickTimeMs = now;
            _lastSelectClickedTarget = clickedTarget;

            // Double-click is the deliberate-but-quick alternative to the Inspector's "Increase source
            // zoom" button -- still only ever an EXPLICIT gesture, never triggered by plain
            // reselection. No-ops via TrySplitPatch's own guard if the patch isn't currently Done or
            // can't increase source zoom without undersampling.
            if (isDoubleClick && _planet != null && _planet.TryGetPatchKey(pickResult?.Renderer, out var patchKey))
            {
                _planet.TrySplitPatch(patchKey);
            }
        }

        /// <summary>
        /// A click always resolves up to the whole authored object first -- so a FlightHelmet import
        /// (one <see cref="ManaLevelSource"/> on the root, none on its Lens/Metal/etc. child meshes)
        /// selects as one thing no matter which part the ray actually hit -- unless that same object is
        /// already the selection, in which case a further click drills straight to the specific part
        /// under the cursor instead of just reselecting the whole thing again. Clicking a different
        /// object resets back to "select the whole thing" for it; clicking back into an object you're
        /// already drilled into (any of its parts already selected, not just the root) keeps drilling
        /// directly between parts rather than bouncing back up each time.
        /// </summary>
        // Public: SceneSelectionDrillDownTests exercises this directly against a small synthetic
        // hierarchy, rather than only through a live click, since it's a pure function of the pick and
        // the current selection with no other dependency.
        public static ManaSceneNode? ResolveEditorSelectableNode(ManaSceneNode? pickedNode, ManaSceneNode? currentSelection)
        {
            if (pickedNode == null)
            {
                return null;
            }

            // Nothing the editor builds for itself is selectable -- a grid map's tiles are the map's, rebuilt
            // from its cells -- so a click on one drills no further than the node that owns it. Drilling into a
            // tile used to select it, which took the selection off the map and shut its tool windows.
            while (pickedNode.IsEditorTransient && pickedNode.Parent != null)
            {
                pickedNode = pickedNode.Parent;
            }

            var authoredRoot = FindAuthoredRoot(pickedNode) ?? pickedNode;

            if (currentSelection != null && ReferenceEquals(FindAuthoredRoot(currentSelection) ?? currentSelection, authoredRoot))
            {
                return pickedNode;
            }

            return authoredRoot;
        }

        private static ManaSceneNode? FindAuthoredRoot(ManaSceneNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current.LevelSource != null)
                {
                    // A group that carries components is the object, and its meshes are only how it looks: a coin
                    // is a group holding the Collectible, with a model child to click. Stopping at the model
                    // selected something with no settings while the coin's own were one level up.
                    if (current.Parent is { LevelSource.Kind: Mana.Code.Levels.ManaLevelSourceKind.Group, Components.Count: > 0 } behaviour)
                    {
                        return behaviour;
                    }

                    return current;
                }
            }

            return null;
        }
    }
}
