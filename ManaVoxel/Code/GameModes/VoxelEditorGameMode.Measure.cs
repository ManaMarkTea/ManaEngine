using ManaVoxel.Code.Measure;
using ManaVoxel.Code.UI.ManaUi;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    // The measuring tool (M, or the ruler on the toolbar): while it is on it owns the pointer. Each frame the
    // pointer is snapped to the triangle beneath it (or the ground), dropped onto the first point's surface when
    // asked, and a click adds the point. MeasureOverlay draws the result.
    public partial class VoxelEditorGameMode
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
        private void UpdateMeasureShortcut()
        {
            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                return;
            }
            var down = !UiWantsKeyboard && !IsPlaying && keyboard.IsKeyPressed(Key.M)
                && !keyboard.IsKeyPressed(Key.ControlLeft) && !keyboard.IsKeyPressed(Key.ControlRight);
            if (down && !_measureToggleWasDown)
            {
                _viewModel.Measure.Active = !_viewModel.Measure.Active;
            }
            _measureToggleWasDown = down;
        }

        /// <summary>True while measuring, so the click is the tool's and nothing gets selected.</summary>
        private bool UpdateMeasuring(bool inputCaptured, IMouse mouse)
        {
            var tool = _viewModel.Measure;
            if (!tool.Active || IsPlaying)
            {
                _measureLeftWasDown = mouse.IsButtonPressed(MouseButton.Left);
                return false;
            }

            var keyboard = Context.Input.Keyboards.FirstOrDefault();
            var shift = keyboard != null && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
            if (keyboard != null && !UiWantsKeyboard)
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
            var tool = _viewModel.Measure;
            var snap = SnapMeasurePoint(pointer, tool);
            var plane = tool.LockPlane(shift);
            tool.HoverUnprojected = null;
            if (snap is { } point && plane is { } onto)
            {
                var dropped = MeasureSnapping.OntoPlane(point.Position, onto.Point, onto.Normal);
                if (Vector3.Distance(dropped, point.Position) > 1e-4f)
                {
                    tool.HoverUnprojected = point.Position;
                }
                snap = point with { Position = dropped };
            }
            tool.Hover = snap;

            if (click && snap is { } at)
            {
                tool.Click(at);
            }
        }

        /// <summary>
        /// The measure script command: modes and options by name, and "hover"/"click" at a world point -- projected
        /// to the screen and run through the same snapping as the pointer, so a script tests what a person gets.
        /// </summary>
        private void RunMeasureCommand(string? name, float[]? position)
        {
            var tool = _viewModel.Measure;
            switch (name?.ToLowerInvariant())
            {
                case "off": tool.Active = false; return;
                case "clear": tool.Clear(); return;
                case "finish": tool.Finish(); return;
                case "distance": tool.Active = true; tool.Mode = MeasureMode.Distance; return;
                case "radius": tool.Active = true; tool.Mode = MeasureMode.Radius; return;
                case "path": tool.Active = true; tool.Mode = MeasureMode.Path; return;
                case "stay": tool.StayOnFirstSurface = true; return;
                case "free": tool.StayOnFirstSurface = false; return;
                case "metric": tool.Units = MeasureUnits.Metric; return;
                case "imperial": tool.Units = MeasureUnits.Imperial; return;
                case "hover":
                case "click":
                    if (position is not { Length: 3 } p || Context.World.MainCamera is not { } camera)
                    {
                        throw new InvalidDataException("measure: hover and click need a world 'position' and a camera.");
                    }
                    tool.Active = true;
                    var window = new Vector2(Context.Window.Size.X, Context.Window.Size.Y);
                    if (!VoxelPlacementDepthOverlay.TryProject(camera._ViewMatrix * camera._ProjectionMatrix, window,
                            new Vector3(p[0], p[1], p[2]), out var screen, out _))
                    {
                        throw new InvalidDataException("measure: that point is behind the camera.");
                    }
                    var real = Context.Input.Mice.FirstOrDefault()?.Position ?? Vector2.Zero;
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
            var camera = Context.World.MainCamera;
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
            var window = new Vector2(Context.Window.Size.X, Context.Window.Size.Y);
            Vector2? ToScreen(Vector3 world) =>
                VoxelPlacementDepthOverlay.TryProject(viewProjection, window, world, out var screen, out var depth) && depth >= 0 && depth <= 1
                    ? screen
                    : null;

            MeasureSnap? snap = null;
            const MeasureSnapTargets surface = MeasureSnapTargets.Vertex | MeasureSnapTargets.Midpoint
                | MeasureSnapTargets.Edge | MeasureSnapTargets.Face;
            if ((tool.Targets & surface) != 0
                && ManaScenePicker.PickTriangle(Context.World.Scenes, ray.Origin, ray.Direction, _pickingExclusions) is { } hit)
            {
                snap = MeasureSnapping.FromTriangle(hit.Position, hit.Normal, hit.A, hit.B, hit.C, pointer, ToScreen,
                    MeasureSnapPixels, tool.Targets, hit.CreaseEdges);
            }
            else
            {
                // Over nothing: the ground, on the editor's snap grid when snapping is on.
                var step = tool.Targets.HasFlag(MeasureSnapTargets.Grid) && _viewModel.Snap.Enabled ? _viewModel.Snap.TranslateStep : 0f;
                snap = MeasureSnapping.OnGround(ray.Origin, ray.Direction, step);
            }
            _measureLastSnap = snap;
            return snap;
        }
    }
}
