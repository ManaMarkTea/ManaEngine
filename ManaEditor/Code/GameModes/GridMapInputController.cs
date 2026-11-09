using Mana.Code.Nodes;
using ManaUI;
using ManaEditor.Code.UI;
using ManaCore.Assets;
using ManaCore.Diagnostics;
using Mana.Code.UI;
using ManaEditor.Code.Assets;
using ManaEditor.Code.GridMaps;
using ManaEditor.Code.UI.ManaUi;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// Painting a grid map in the viewport. While <see cref="GridMapEditing.Target"/> is set the pointer paints
    /// cells instead of selecting: left-drag paints (an auto-tile set -- a road -- picks each cell's piece as it goes), Shift erases, Q (or Ctrl+click) picks a cell's tile up as the brush, T turns the tile under the pointer, a right click clears it,
    /// R turns the brush (Shift+R the other way), - and + change floor, and Escape stops.
    /// Each stroke is one undo step.
    /// </summary>
    internal sealed class GridMapInputController(ManaContext context, VoxelEditorViewModel viewModel, HashSet<ManaSceneNode> pickingExclusions, Func<UiRoot?> rootProvider)
    {
        private static readonly Vector4 GridBrushOutline = new(0.35f, 0.8f, 1.0f, 1.0f);
        private static readonly Vector4 GridEraseOutline = new(1.0f, 0.3f, 0.25f, 1.0f);

        private ManaSceneNode? _gridBrushPreview;
        private (ManaGridMapNode Map, object Palette, int Item, Matrix4x4 Model)? _gridBrushPreviewKey;
        private ManaSceneNode? _gridEraseHighlight;

        private ManaGridMapNode? _gridStrokeMap;
        private GridMapState? _gridStrokeBefore;
        private readonly HashSet<GridMapCell> _gridStrokeCovered = new();
        private GridMapCell? _gridStrokeLastCell;
        private bool _gridPickWasDown;
        private bool _gridEyedropWasDown;
        private bool _gridTurnTileWasDown;
        private bool _gridRightWasDown;
        private Vector2 _gridRightPressedAt;
        private bool _gridRightDragged;

        private bool _gridRotateWasDown;
        private bool _gridFloorUpWasDown;
        private bool _gridFloorDownWasDown;
        private bool _gridStopWasDown;

        private readonly List<(Task Download, ManaGridMapNode Map, string Asset)> _gridPaletteDownloads = new();

        /// <summary>
        /// Runs the brush for this frame. True when painting owned the pointer, so selection and the voxel
        /// tools must leave it alone.
        /// </summary>
        public bool UpdateGridMapPainting(bool inputCaptured, IMouse mouse, bool isPlaying, bool uiWantsKeyboard)
        {
            CompleteGridPaletteDownloads();
            UpdateGridGeneratorArea(isPlaying);

            var target = GridMapEditing.Target;
            if (target != null && (isPlaying || !ReferenceEquals(context.World.Selection.SelectedNode, target) || !IsInWorld(target)))
            {
                // Selecting something else, deleting the map or starting play all end painting.
                GridMapEditing.Stop();
                target = null;
            }

            // Q is the eyedropper while painting, so it only flies the camera down during a look drag.
            if (context.World.MainCamera is { } camera)
            {
                camera.VerticalFlyKeysNeedLook = target != null;
            }

            if (target == null)
            {
                EndGridStroke();
                HideGridBrush();
                HideGridOverlay(ref _gridFilledOverlay);
                _gridFilledOverlayKey = null;
                return false;
            }

            UpdateGridMapKeys(target, uiWantsKeyboard);
            UpdateGridFilledOverlay(target);

            var keyboard = context.Input.Keyboards.FirstOrDefault();
            var shift = keyboard != null && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
            var control = keyboard != null && (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            var erasing = shift || GridMapEditing.Erasing;

            var cell = inputCaptured ? null : HoveredGridCell(target, mouse);
            if (cell == null)
            {
                HideGridBrush();
                if (!leftDown)
                {
                    EndGridStroke();
                }

                return true;
            }

            // Q: the eyedropper. The tile under the pointer, turned as it is, becomes the brush -- or Blank over an
            // empty cell. Not during a look drag, where Q flies the camera down.
            var eyedropDown = keyboard != null && !uiWantsKeyboard && keyboard.IsKeyPressed(Key.Q)
                && !mouse.IsButtonPressed(MouseButton.Right) && !mouse.IsButtonPressed(MouseButton.Middle);
            if (eyedropDown && !_gridEyedropWasDown)
            {
                if (target.TryGetTileCovering(cell.Value, out _, out var dropped))
                {
                    PickGridBrush(target, dropped);
                }
                else
                {
                    GridMapEditing.Brush = GridMapEditing.Blank;
                }
            }

            _gridEyedropWasDown = eyedropDown;

            // T turns the tile under the pointer where it stands (Shift+T the other way): fixing one already
            // placed without repainting it. A turned big tile can knock out what it now lands on, so the whole
            // grid is the undo step, as for a stroke.
            var turnDown = keyboard != null && !uiWantsKeyboard && keyboard.IsKeyPressed(Key.T);
            if (turnDown && !_gridTurnTileWasDown && target.TryGetTileCovering(cell.Value, out var turnAnchor, out var turning))
            {
                EndGridStroke();
                var before = target.CaptureState();
                target.SetTile(turnAnchor, turning with { Orientation = turning.Orientation + (shift ? -1 : 1) });
                var after = target.CaptureState();
                viewModel.History.Record("Turn grid tile", () => target.RestoreState(before), () => target.RestoreState(after));
            }

            _gridTurnTileWasDown = turnDown;

            // A right click -- press and release without moving -- clears the tile under the pointer. A right drag
            // is still the camera's, so this only acts on a release the pointer did not travel for.
            var rightDown = mouse.IsButtonPressed(MouseButton.Right);
            if (rightDown && !_gridRightWasDown)
            {
                _gridRightPressedAt = mouse.Position;
                _gridRightDragged = false;
            }
            else if (rightDown && Vector2.Distance(mouse.Position, _gridRightPressedAt) > 4.0f)
            {
                _gridRightDragged = true;
            }
            else if (!rightDown && _gridRightWasDown && !_gridRightDragged && target.TryGetTileCovering(cell.Value, out _, out _))
            {
                EndGridStroke();
                var before = target.CaptureState();
                target.ClearTile(cell.Value);
                var after = target.CaptureState();
                viewModel.History.Record("Clear grid tile", () => target.RestoreState(before), () => target.RestoreState(after));
            }

            _gridRightWasDown = rightDown;
            erasing = shift || GridMapEditing.Erasing;

            ShowGridBrush(target, cell.Value, erasing);

            // A click in the viewport ends typing in the panel: otherwise a cell size field keeps the keyboard
            // and R types an "r" into it instead of turning the brush.
            if (leftDown && !_gridPickWasDown && uiWantsKeyboard)
            {
                rootProvider()?.FocusManager.Focus(null);
            }

            if (control)
            {
                // Pick: the cell's tile and rotation become the brush. Once per click, not per frame held.
                if (leftDown && !_gridPickWasDown && target.TryGetTileCovering(cell.Value, out _, out var picked))
                {
                    PickGridBrush(target, picked);
                }

                _gridPickWasDown = leftDown;
                EndGridStroke();
                return true;
            }

            _gridPickWasDown = leftDown;
            if (!leftDown)
            {
                EndGridStroke();
                return true;
            }

            // Once per cell entered, so holding still does not repaint the same cell every frame.
            if (_gridStrokeLastCell == cell)
            {
                return true;
            }

            var from = _gridStrokeLastCell;
            _gridStrokeLastCell = cell;
            BeginGridStroke(target);
            foreach (var step in GridStrokePath(from, cell.Value))
            {
                PaintGridCell(target, step, erasing);
            }

            return true;
        }

        private void PaintGridCell(ManaGridMapNode target, GridMapCell cell, bool erasing)
        {
            if (erasing)
            {
                target.ClearTile(cell);
            }
            else if (GridMapEditing.IsAutoTile)
            {
                target.PaintAutoTile(GridMapEditing.AutoTile, cell);
            }
            else if ((uint)GridMapEditing.Brush < (uint)target.Palette.Count)
            {
                // A tile bigger than a cell would otherwise be painted again at every cell the drag crosses,
                // each one knocking out the last. Within one stroke a tile only goes where the stroke has not
                // already put one.
                var tile = new GridMapTile(GridMapEditing.Brush, GridMapEditing.Orientation);
                var covered = target.CoveredCells(cell, tile).ToList();
                if (!covered.Any(_gridStrokeCovered.Contains))
                {
                    target.SetTile(cell, tile);
                    _gridStrokeCovered.UnionWith(covered);
                }
            }
        }

        /// <summary>
        /// The cells from the last one painted to this one, side by side. A quick drag jumps several cells a
        /// frame, and a road painted only where the frames landed would come out in pieces.
        /// </summary>
        public static IEnumerable<GridMapCell> GridStrokePath(GridMapCell? from, GridMapCell to)
        {
            if (from is not { } start || start.Y != to.Y)
            {
                yield return to;
                yield break;
            }

            var steps = Math.Max(Math.Abs(to.X - start.X), Math.Abs(to.Z - start.Z));
            var previous = start;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var next = new GridMapCell(
                    (int)MathF.Round(start.X + (to.X - start.X) * t),
                    to.Y,
                    (int)MathF.Round(start.Z + (to.Z - start.Z) * t));
                if (next.X != previous.X && next.Z != previous.Z)
                {
                    // A diagonal step goes round the corner, so each cell shares a side with the one before.
                    yield return next with { Z = previous.Z };
                }

                yield return next;
                previous = next;
            }
        }

        /// <summary>The brush becomes what a tile is: its auto-tile set when it is a piece of one, else the tile itself.</summary>
        private static void PickGridBrush(ManaGridMapNode target, GridMapTile tile)
        {
            GridMapEditing.Brush = tile.Item;
            GridMapEditing.Orientation = tile.Orientation;
            var set = target.AutoTileSetOf(tile.Item);
            if (set >= 0)
            {
                GridMapEditing.AutoTile = set;
            }
        }

        /// <summary>
        /// What the brush would put in a cell: the palette item turned as the brush is, or for an auto-tile
        /// set, the piece it would pick there. Null for Blank or a set with no pieces.
        /// </summary>
        private static GridMapTile? GridBrushTile(ManaGridMapNode target, GridMapCell cell)
        {
            if (GridMapEditing.IsAutoTile)
            {
                return target.ResolveAutoTile(GridMapEditing.AutoTile, cell);
            }

            return (uint)GridMapEditing.Brush < (uint)target.Palette.Count
                ? new GridMapTile(GridMapEditing.Brush, GridMapEditing.Orientation)
                : null;
        }

        private void UpdateGridMapKeys(ManaGridMapNode target, bool uiWantsKeyboard)
        {
            var keyboard = context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null || uiWantsKeyboard)
            {
                _gridRotateWasDown = _gridFloorUpWasDown = _gridFloorDownWasDown = _gridStopWasDown = false;
                return;
            }

            var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
            if (Pressed(keyboard, Key.R, ref _gridRotateWasDown))
            {
                GridMapEditing.Rotate(shift ? -1 : 1);
            }

            // + and - step the floor, from the main keys or the numpad. + is the = key unshifted.
            if (Pressed2(keyboard, Key.Equal, Key.KeypadAdd, ref _gridFloorUpWasDown))
            {
                GridMapEditing.Floor++;
            }

            if (Pressed2(keyboard, Key.Minus, Key.KeypadSubtract, ref _gridFloorDownWasDown))
            {
                GridMapEditing.Floor--;
            }

            if (Pressed(keyboard, Key.Escape, ref _gridStopWasDown))
            {
                GridMapEditing.Stop();
            }

            static bool Pressed(IKeyboard keyboard, Key key, ref bool wasDown) => Pressed2(keyboard, key, key, ref wasDown);

            static bool Pressed2(IKeyboard keyboard, Key key, Key alternate, ref bool wasDown)
            {
                var down = keyboard.IsKeyPressed(key) || keyboard.IsKeyPressed(alternate);
                var pressed = down && !wasDown;
                wasDown = down;
                return pressed;
            }
        }

        /// <summary>The cell under the pointer on the brush's floor, cut against that floor's plane.</summary>
        private GridMapCell? HoveredGridCell(ManaGridMapNode target, IMouse mouse)
        {
            var camera = context.World.MainCamera;
            if (camera == null || !Matrix4x4.Invert(target.WorldTransform, out var worldToGrid))
            {
                return null;
            }

            var ray = camera.ProjectScreenToWorld(new Vector3(mouse.Position, 0.0f), new Vector3(mouse.Position, 1.0f));
            var origin = Vector3.Transform(ray.Origin, worldToGrid);
            var direction = Vector3.TransformNormal(ray.Direction, worldToGrid);
            var floorHeight = GridMapEditing.Floor * target.CellSize.Y;
            if (MathF.Abs(direction.Y) < 1e-5f)
            {
                return null;
            }

            var travel = (floorHeight - origin.Y) / direction.Y;
            if (travel <= 0.0f)
            {
                return null;
            }

            return target.CellAt(origin + direction * travel, GridMapEditing.Floor);
        }

        private void BeginGridStroke(ManaGridMapNode target)
        {
            if (!ReferenceEquals(_gridStrokeMap, target))
            {
                EndGridStroke();
                _gridStrokeMap = target;
                _gridStrokeBefore = target.CaptureState();
            }
        }

        /// <summary>
        /// Records the stroke as one undo step. The whole grid before and after, not the cells touched: a big tile
        /// knocks out the neighbours it lands on, and undo has to bring those back too.
        /// </summary>
        private void EndGridStroke()
        {
            _gridStrokeLastCell = null;
            _gridStrokeCovered.Clear();
            var target = _gridStrokeMap;
            var before = _gridStrokeBefore;
            _gridStrokeMap = null;
            _gridStrokeBefore = null;
            if (target == null || before == null)
            {
                return;
            }

            var after = target.CaptureState();
            var changed = before.Layers.Count != after.Layers.Count
                || before.Layers.Zip(after.Layers).Any(pair => pair.First.Cells.Count != pair.Second.Cells.Count
                    || pair.First.Cells.Any(cell => !pair.Second.Cells.TryGetValue(cell.Key, out var tile) || tile != cell.Value));
            if (!changed)
            {
                return;
            }

            viewModel.History.Record("Paint grid map", () => target.RestoreState(before), () => target.RestoreState(after));
        }

        /// <summary>
        /// The brush preview: the palette item's model standing in the hovered cell, outlined, or the tile
        /// that would be erased outlined in red. The preview lives in the default scene rather than under
        /// the map so it can never be mistaken for a tile.
        /// </summary>
        private void ShowGridBrush(ManaGridMapNode target, GridMapCell cell, bool erasing)
        {
            SetGridEraseHighlight(erasing ? target.TileNode(cell) : null);
            UpdateGridTargetOverlay(target, cell, erasing);

            if (erasing || GridBrushTile(target, cell) is not { } brush)
            {
                RemoveGridBrushPreview();
                return;
            }

            var item = brush.Item;
            var key = (target, target.PaletteToken, item, target.ModelTransform(item));
            if (_gridBrushPreview == null || _gridBrushPreviewKey != key)
            {
                RemoveGridBrushPreview();
                _gridBrushPreview = target.CreatePreview(item);
                _gridBrushPreviewKey = key;
                if (_gridBrushPreview == null)
                {
                    return;
                }

                SetOutline(_gridBrushPreview, GridBrushOutline);
                lock (context.World.SceneLock)
                {
                    context.World.DefaultScene.Children.Add(_gridBrushPreview);
                }

                // Never something a click lands on: the pointer is always over it.
                pickingExclusions.Add(_gridBrushPreview);
            }

            _gridBrushPreview?.SetLocalTransform(target.TileTransform(cell, brush) * target.WorldTransform);
        }

        private void HideGridBrush()
        {
            RemoveGridBrushPreview();
            SetGridEraseHighlight(null);
            HideGridOverlay(ref _gridTargetOverlay);
            _gridTargetOverlayKey = null;
        }

        private void RemoveGridBrushPreview()
        {
            if (_gridBrushPreview != null)
            {
                lock (context.World.SceneLock)
                {
                    context.World.DefaultScene.Children.Remove(_gridBrushPreview);
                }

                pickingExclusions.Remove(_gridBrushPreview);
            }

            _gridBrushPreview = null;
            _gridBrushPreviewKey = null;
        }

        private void SetGridEraseHighlight(ManaSceneNode? tile)
        {
            if (ReferenceEquals(tile, _gridEraseHighlight))
            {
                return;
            }

            if (_gridEraseHighlight != null)
            {
                SetOutline(_gridEraseHighlight, null);
            }

            _gridEraseHighlight = tile;
            if (tile != null)
            {
                SetOutline(tile, GridEraseOutline);
            }
        }

        private static void SetOutline(ManaSceneNode node, Vector4? color)
        {
            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                renderer.EditorOutlineEnabled = color.HasValue;
                if (color is { } outline)
                {
                    renderer.EditorOutlineColor = outline;
                }
            }

            foreach (var child in node.Children)
            {
                SetOutline(child, color);
            }
        }

        private static readonly Vector4 GridFilledColor = new(0.3f, 0.75f, 1.0f, 1.0f);
        private static readonly Vector4 GridEmptyTargetColor = new(0.35f, 1.0f, 0.45f, 1.0f);
        private static readonly Vector4 GridReplaceTargetColor = new(1.0f, 0.65f, 0.2f, 1.0f);
        private static readonly Vector4 GridEraseTargetColor = new(1.0f, 0.3f, 0.25f, 1.0f);

        private ManaSceneNode? _gridFilledOverlay;
        private (ManaGridMapNode Map, int Revision, int Floor, Vector3 CellSize)? _gridFilledOverlayKey;
        private ManaSceneNode? _gridTargetOverlay;
        private (ManaGridMapNode Map, int Revision, GridMapCell Cell, int Item, int Orientation, bool Erasing)? _gridTargetOverlayKey;

        /// <summary>
        /// Outlines every tile on the floor being painted, so what is already filled is visible even where the
        /// tiles themselves are flat or hidden -- and so a stray line painted by a drag is easy to find.
        /// </summary>
        private void UpdateGridFilledOverlay(ManaGridMapNode target)
        {
            if (!ManaEditorGizmos.Visible)
            {
                HideGridOverlay(ref _gridFilledOverlay);
                _gridFilledOverlayKey = null;
                return;
            }

            var key = (target, target.Revision, GridMapEditing.Floor, target.CellSize);
            if (_gridFilledOverlayKey == key)
            {
                PlaceGridOverlay(_gridFilledOverlay, target);
                return;
            }

            _gridFilledOverlayKey = key;
            var rectangles = new List<ManaLinePolyline>();
            foreach (var (anchor, tile) in target.Cells)
            {
                if (anchor.Y == GridMapEditing.Floor)
                {
                    rectangles.Add(FootprintOutline(target, anchor, target.Footprint(tile), inset: 0.06f));
                }
            }

            ReplaceGridOverlay(ref _gridFilledOverlay, target, rectangles, GridFilledColor, 0.025f, "Grid Map Filled Cells", opacity: 0.35f);
        }

        /// <summary>
        /// Outlines the cells the brush is about to act on: green where it would paint into empty cells, orange
        /// where it would replace tiles, red around the tile it would erase.
        /// </summary>
        private void UpdateGridTargetOverlay(ManaGridMapNode target, GridMapCell cell, bool erasing)
        {
            if (!ManaEditorGizmos.Visible)
            {
                HideGridOverlay(ref _gridTargetOverlay);
                _gridTargetOverlayKey = null;
                return;
            }

            var brush = erasing ? null : GridBrushTile(target, cell);
            var item = brush?.Item ?? GridMapEditing.Blank;
            var key = (target, target.Revision, cell, item, brush?.Orientation ?? GridMapEditing.Orientation, erasing);
            if (_gridTargetOverlayKey == key)
            {
                PlaceGridOverlay(_gridTargetOverlay, target);
                return;
            }

            _gridTargetOverlayKey = key;
            ManaLinePolyline outline;
            Vector4 color;
            if (erasing)
            {
                // Nothing to erase shows a plain cell outline, so the cursor still says where it is.
                outline = target.TryGetTileCovering(cell, out var anchor, out var existing)
                    ? FootprintOutline(target, anchor, target.Footprint(existing), inset: 0.0f)
                    : FootprintOutline(target, cell, new GridMapFootprint(1, 1), inset: 0.0f);
                color = GridEraseTargetColor;
            }
            else
            {
                var valid = brush != null;
                var tile = brush ?? new GridMapTile(0, 0);
                var footprint = valid ? target.Footprint(tile) : new GridMapFootprint(1, 1);
                var replaces = valid && target.CoveredCells(cell, tile).Any(covered => target.TryGetTileCovering(covered, out _, out _));
                outline = FootprintOutline(target, cell, footprint, inset: 0.0f);
                color = replaces ? GridReplaceTargetColor : GridEmptyTargetColor;
            }

            ReplaceGridOverlay(ref _gridTargetOverlay, target, new List<ManaLinePolyline> { outline }, color, 0.06f, "Grid Map Brush Cells", opacity: 0.85f);
        }

        /// <summary>A rectangle round a footprint's cells, just above their floor, in the grid map's space.</summary>
        private static ManaLinePolyline FootprintOutline(ManaGridMapNode target, GridMapCell anchor, GridMapFootprint footprint, float inset)
        {
            var size = target.CellSize;
            var margin = inset * MathF.Min(size.X, size.Z);
            var y = anchor.Y * size.Y + 0.002f * size.Y;
            var minX = anchor.X * size.X + margin;
            var minZ = anchor.Z * size.Z + margin;
            var maxX = (anchor.X + footprint.Width) * size.X - margin;
            var maxZ = (anchor.Z + footprint.Depth) * size.Z - margin;
            return new ManaLinePolyline(
                new[] { new Vector3(minX, y, minZ), new Vector3(maxX, y, minZ), new Vector3(maxX, y, maxZ), new Vector3(minX, y, maxZ) },
                closed: true);
        }

        private void ReplaceGridOverlay(
            ref ManaSceneNode? overlay,
            ManaGridMapNode target,
            List<ManaLinePolyline> lines,
            Vector4 color,
            float thicknessPerCell,
            string name,
            float opacity)
        {
            HideGridOverlay(ref overlay);
            if (lines.Count == 0)
            {
                return;
            }

            var mesh = new ManaLineMesh(
                context,
                lines,
                thickness: thicknessPerCell * MathF.Min(target.CellSize.X, target.CellSize.Z),
                color,
                emissionStrength: 0.8f);
            var node = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = name,
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true
            };

            // On top of the tiles: a floor's own geometry would otherwise hide the outline of the floor it is.
            var renderer = new MeshRendererComponent(node, mesh) { DrawAfterScene = true, AlwaysOnTop = true };
            renderer.Material.Opacity.Value = opacity;
            node.Components.Add(renderer);
            lock (context.World.SceneLock)
            {
                context.World.DefaultScene.Children.Add(node);
            }

            pickingExclusions.Add(node);
            overlay = node;
            PlaceGridOverlay(overlay, target);
        }

        /// <summary>Follows the map, which can be moved with the gizmo while the overlay is up.</summary>
        private static void PlaceGridOverlay(ManaSceneNode? overlay, ManaGridMapNode target)
        {
            overlay?.SetLocalTransform(target.WorldTransform);
        }

        private void HideGridOverlay(ref ManaSceneNode? overlay)
        {
            if (overlay == null)
            {
                return;
            }

            lock (context.World.SceneLock)
            {
                context.World.DefaultScene.Children.Remove(overlay);
            }

            pickingExclusions.Remove(overlay);
            overlay = null;
        }

        private static readonly Vector4 GridGeneratorAreaColor = new(1.0f, 0.8f, 0.2f, 1.0f);

        private ManaSceneNode? _gridGeneratorOverlay;
        private (ManaGridMapNode Map, int X, int Z, int Width, int Depth, int Floor, Vector3 CellSize)? _gridGeneratorOverlayKey;

        /// <summary>Outlines the area the Generator window fills, while it is up over a selected grid map.</summary>
        private void UpdateGridGeneratorArea(bool isPlaying)
        {
            if (!GridMapWfcEditing.WindowOpen || !ManaEditorGizmos.Visible || isPlaying
                || GridMapWfcEditing.MapFor(context.World.Selection.SelectedNode, IsInWorld) is not { } map)
            {
                HideGridOverlay(ref _gridGeneratorOverlay);
                _gridGeneratorOverlayKey = null;
                return;
            }

            var key = (map, GridMapWfcEditing.X, GridMapWfcEditing.Z, GridMapWfcEditing.Width, GridMapWfcEditing.Depth, GridMapEditing.Floor, map.CellSize);
            if (_gridGeneratorOverlayKey == key)
            {
                PlaceGridOverlay(_gridGeneratorOverlay, map);
                return;
            }

            _gridGeneratorOverlayKey = key;
            var outline = FootprintOutline(map, new GridMapCell(key.X, key.Floor, key.Z), new GridMapFootprint(key.Width, key.Depth), inset: 0.0f);
            ReplaceGridOverlay(ref _gridGeneratorOverlay, map, new List<ManaLinePolyline> { outline }, GridGeneratorAreaColor, 0.08f, "Grid Map Generator Area", opacity: 0.9f);
        }

        /// <summary>
        /// Takes every grid map helper out of the scene -- brush, outlines, the generator's area -- and stops
        /// painting. For play, where the update that would otherwise keep them in step does not run.
        /// </summary>
        public void HideGridMapEditorVisuals()
        {
            GridMapEditing.Stop();
            EndGridStroke();
            HideGridBrush();
            HideGridOverlay(ref _gridFilledOverlay);
            _gridFilledOverlayKey = null;
            HideGridOverlay(ref _gridGeneratorOverlay);
            _gridGeneratorOverlayKey = null;
        }

        private bool IsInWorld(ManaSceneNode node)
        {
            var root = node;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            lock (context.World.SceneLock)
            {
                return context.World.Scenes.Any(scene => scene.Children.Contains(root));
            }
        }

        /// <summary>Adds several models, in order, and makes the first of them the brush.</summary>
        public void AddAllToGridPalette(ManaGridMapNode target, IReadOnlyList<string> assets)
        {
            foreach (var asset in assets)
            {
                AddToGridPalette(target, asset);
            }

            if (assets.Count > 1 && target.Palette.ToList().FindIndex(item => string.Equals(item, assets[0], StringComparison.OrdinalIgnoreCase)) is var first and >= 0)
            {
                GridMapEditing.Brush = first;
            }
        }

        /// <summary>
        /// Adds a model to a map's palette and makes it the brush. On the web a model is downloaded first:
        /// a palette item that failed to load is remembered as failed, so it must not be added before it can load.
        /// </summary>
        internal void AddToGridPalette(ManaGridMapNode target, string asset)
        {
            if (ManaAssetCatalog.Current is { } catalog && !catalog.IsReady(asset))
            {
                viewModel.Levels.SetStatus($"Downloading {MeshAssetLibrary.DisplayName(asset)} for the grid map palette...");
                _gridPaletteDownloads.Add((catalog.EnsureAsync(asset), target, asset));
                return;
            }

            GridMapEditing.Brush = target.AddPaletteItem(asset);
        }

        private void CompleteGridPaletteDownloads()
        {
            for (var i = _gridPaletteDownloads.Count - 1; i >= 0; i--)
            {
                var (download, map, asset) = _gridPaletteDownloads[i];
                if (!download.IsCompleted)
                {
                    continue;
                }

                _gridPaletteDownloads.RemoveAt(i);
                if (download.IsFaulted)
                {
                    ManaLog.WriteDiagnostic($"Grid map palette download failed for '{asset}': {download.Exception?.GetBaseException().Message}");
                    viewModel.Levels.SetStatus($"Could not download {MeshAssetLibrary.DisplayName(asset)}.");
                    continue;
                }

                GridMapEditing.Brush = map.AddPaletteItem(asset);
                viewModel.Levels.SetStatus(string.Empty);
            }
        }
    }
}
