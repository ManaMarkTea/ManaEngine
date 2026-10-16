using Mana.Code.Nodes;
using ManaVoxel.Code.Measure;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    // The measuring tool (M, or the ruler on the toolbar): while it is on it owns the pointer. Each frame the
    // pointer is snapped to the triangle beneath it (or the ground), dropped onto the first point's surface when
    // asked, and a click adds the point. MeasureOverlay draws the result.
    internal sealed class MeasureInputController(ManaContext context, VoxelEditorViewModel viewModel, HashSet<ManaSceneNode> pickingExclusions)
    {
        /// <summary>Within this many pixels of a corner, an edge's middle or an edge, the point jumps to it.</summary>
        private const float MeasureSnapPixels = 14.0f;

        private bool _measureToggleWasDown;
        private bool _measureEscapeWasDown;
        private bool _measureEnterWasDown;
        private bool _measureLeftWasDown;
        private Vector2 _measureLastPointer = new(float.NaN);
        private Matrix4x4 _measureLastView;
        private MeasureSnapTargets _measureLastTargets;
        private MeasureSnap? _measureLastSnap;

        /// <summary>M switches the tool on and off, as the toolbar's ruler does.</summary>
        public void UpdateMeasureShortcut(bool isPlaying, bool uiWantsKeyboard)
        {
            var keyboard = context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }
            var down = !uiWantsKeyboard && !isPlaying && keyboard.IsKeyPressed(Key.M)
                && !keyboard.IsKeyPressed(Key.ControlLeft) && !keyboard.IsKeyPressed(Key.ControlRight);
            if (down && !_measureToggleWasDown)
            {
                viewModel.Measure.Active = !viewModel.Measure.Active;
            }
            _measureToggleWasDown = down;
        }

        /// <summary>True while measuring, so the click is the tool's and nothing gets selected.</summary>
        public bool UpdateMeasuring(bool inputCaptured, IMouse mouse, bool isPlaying, bool uiWantsKeyboard)
        {
            var tool = viewModel.Measure;
            if (!tool.Active || isPlaying)
            {
                _measureLeftWasDown = mouse.IsButtonPressed(MouseButton.Left);
                return false;
            }

            var keyboard = context.Input.Keyboards.FirstOrDefault();
            var shift = keyboard != null && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
            if (keyboard != null && !uiWantsKeyboard)
            {
                var escape = keyboard.IsKeyPressed(Key.Escape);
                if (escape && !_measureEscapeWasDown)
                {
                    // First Esc lets go of the measurement in progress; the next one puts the tool away.
                    if (tool.Current != null)
                    {
                        tool.Cancel();
                    }
                    else
                    {
                        tool.Active = false;
                    }
                }
                _measureEscapeWasDown = escape;

                var enter = keyboard.IsKeyPressed(Key.Enter) || keyboard.IsKeyPressed(Key.KeypadEnter);
                if (enter && !_measureEnterWasDown)
                {
                    tool.Finish();
                }
                _measureEnterWasDown = enter;
            }

            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            var clicked = leftDown && !_measureLeftWasDown;
            _measureLeftWasDown = leftDown;

            // A script is pointing (the measure command); the real pointer takes over again once it moves.
            if (_measureScriptedPointer is { } scripted)
            {
                if (mouse.Position == scripted.Real && !clicked)
                {
                    return true;
                }
                _measureScriptedPointer = null;
            }

            if (inputCaptured || !tool.Active)
            {
                tool.Hover = null;
                return true;
            }

            PointMeasure(mouse.Position, shift, clicked);
            return true;
        }

        private (Vector2 Real, Vector2 Scripted)? _measureScriptedPointer;

        /// <summary>Snaps the point under <paramref name="pointer"/>, drops it onto the first surface if asked, and clicks it.</summary>
        private void PointMeasure(Vector2 pointer, bool shift, bool click)
        {
            var tool = viewModel.Measure;
            // Ray mode's ray: the scene's own triangles, skipping the editor's helpers as the pointer does.
            tool.CastRay ??= (origin, direction) =>
                ManaScenePicker.PickTriangle(context.World.Scenes, origin, direction, pickingExclusions)?.Position;
            tool.Point(SnapMeasurePoint(pointer, tool), shift, click);
        }



        /// <summary>
        /// The measure script command: modes and options by name, and "hover"/"click" at a world point -- projected
        /// to the screen and run through the same snapping as the pointer, so a script tests what a person gets.
        /// </summary>
        public void RunMeasureCommand(string? name, float[]? position)
        {
            var tool = viewModel.Measure;
            switch (name?.ToLowerInvariant())
            {
                case "off": tool.Active = false; return;
                case "clear": tool.Clear(); return;
                case "finish": tool.Finish(); return;
                case "distance": tool.Active = true; tool.Mode = MeasureMode.Distance; return;
                case "radius": tool.Active = true; tool.Mode = MeasureMode.Radius; return;
                case "path": tool.Active = true; tool.Mode = MeasureMode.Path; return;
                case "ray": tool.Active = true; tool.Mode = MeasureMode.Ray; return;
                case "ray_out": tool.RayDirection = MeasureRayDirection.Out; return;
                case "ray_up": tool.RayDirection = MeasureRayDirection.Up; return;
                case "ray_down": tool.RayDirection = MeasureRayDirection.Down; return;
                case "stay": tool.StayOnFirstSurface = true; return;
                case "free": tool.StayOnFirstSurface = false; return;
                case "metric": tool.Units = MeasureUnits.Metric; return;
                case "imperial": tool.Units = MeasureUnits.Imperial; return;
                case "hover":
                case "click":
                    if (position is not { Length: 3 } p || context.World.MainCamera is not { } camera)
                    {
                        throw new InvalidDataException("measure: hover and click need a world 'position' and a camera.");
                    }
                    tool.Active = true;
                    var window = new Vector2(context.Window.Size.X, context.Window.Size.Y);
                    if (!VoxelPlacementDepthOverlay.TryProject(camera._ViewMatrix * camera._ProjectionMatrix, window,
                            new Vector3(p[0], p[1], p[2]), out var screen, out _))
                    {
                        throw new InvalidDataException("measure: that point is behind the camera.");
                    }
                    var real = context.Input.Mice.FirstOrDefault()?.Position ?? Vector2.Zero;
                    _measureScriptedPointer = (real, screen);
                    PointMeasure(screen, shift: false, click: name.Equals("click", StringComparison.OrdinalIgnoreCase));
                    var hover = tool.Hover;
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                        $"Command file: measure {name} -> {hover?.Kind} at {hover?.Position}; {tool.Measurements.Count} kept"
                        + (tool.Measurements.Count > 0 ? $", last {tool.Headline(tool.Measurements[^1])}" : string.Empty));
                    return;
                default:
                    throw new InvalidDataException($"measure: unknown '{name}'.");
            }
        }

        // The triangle test walks every mesh, so it runs only when the pointer has moved (or the scene might have).
        private MeasureSnap? SnapMeasurePoint(Vector2 pointer, MeasureTool tool)
        {
            var camera = context.World.MainCamera;
            if (camera == null)
            {
                return null;
            }
            var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
            if (pointer == _measureLastPointer && viewProjection == _measureLastView && tool.Targets == _measureLastTargets)
            {
                return _measureLastSnap;
            }
            _measureLastPointer = pointer;
            _measureLastView = viewProjection;
            _measureLastTargets = tool.Targets;

            var ray = camera.ProjectScreenToWorld(new Vector3(pointer, 0.0f), new Vector3(pointer, 1.0f));
            var window = new Vector2(context.Window.Size.X, context.Window.Size.Y);
            Vector2? ToScreen(Vector3 world) =>
                VoxelPlacementDepthOverlay.TryProject(viewProjection, window, world, out var screen, out var depth) && depth >= 0 && depth <= 1
                    ? screen
                    : null;

            MeasureSnap? snap = null;
            const MeasureSnapTargets surface = MeasureSnapTargets.Vertex | MeasureSnapTargets.Midpoint
                | MeasureSnapTargets.Edge | MeasureSnapTargets.Face;
            if ((tool.Targets & surface) != 0
                && ManaScenePicker.PickTriangle(context.World.Scenes, ray.Origin, ray.Direction, pickingExclusions) is { } hit)
            {
                snap = MeasureSnapping.FromTriangle(hit.Position, hit.Normal, hit.A, hit.B, hit.C, pointer, ToScreen,
                    MeasureSnapPixels, tool.Targets, hit.CreaseEdges);
            }
            else
            {
                // Over nothing: the ground, on the editor's snap grid when snapping is on.
                var step = tool.Targets.HasFlag(MeasureSnapTargets.Grid) && viewModel.Snap.Enabled ? viewModel.Snap.TranslateStep : 0f;
                snap = MeasureSnapping.OnGround(ray.Origin, ray.Direction, step);
            }
            _measureLastSnap = snap;
            return snap;
        }
    }
}
