#if !MANA_BROWSER
#endif
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;
using ManaCore.Input;
using ManaUI;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Mana
{
    public enum CameraViewPreset
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Bottom
    }

    public class CameraComponent : ManaComponent
    {
        public bool OrthoGraphic = false;
        public float NearClipPlane = 0.1f;
        public float FarClipPlane = 1000.0f;
        public float AspectRatio = 1.0f;
        public float FieldOfViewDegrees = 55.0f;
        public float EffectiveFieldOfViewDegrees => Math.Clamp(
            FieldOfViewDegrees + (AttachedSpringArm?.FieldOfViewOffsetDegrees ?? 0.0f), 1.0f, 150.0f);
        public float OrthographicHeight = 80.0f;
        public float PanSpeed = 1.0f;
        public bool TouchpadPanInOrthographic = true;

        /// <summary>
        /// Q and E fly the camera down and up only while a look drag is held -- set by a tool that wants those
        /// keys for itself the rest of the time (grid map painting, whose Q picks a tile up).
        /// </summary>
        public bool VerticalFlyKeysNeedLook { get; set; }
        public Func<bool>? ShouldCaptureMouseInput { get; set; }

        // Mirrors ShouldCaptureMouseInput for the WASD/QE fly block. Something else in the app can lay
        // claim to the movement keys for a frame -- ManaPawnController does exactly this while a
        // character is possessed, so WASD drives the character instead of the camera.
        public Func<bool>? ShouldCaptureKeyboardInput { get; set; }
        public Vector2D<int> DebugPointCount = new Vector2D<int>(5, 3);


        public Matrix4x4 _ProjectionMatrix;
        public Matrix4x4 _ViewMatrix;
        public Matrix4x4 _WorldInverseMatrix;
        public Vector3 ViewParams { get; internal set; }
        public Vector3[] ViewAxes { get; internal set; } = new Vector3[3];


        float _MovementSpeed = 50;
        public float MovementSpeed
        {
            get => _MovementSpeed;
            set => _MovementSpeed = value;
        }

        int _MouseAverageSize = 4;
        Vector2 _MouseSensitivity = new(8.0f, 8.0f);
        Vector2 _LastMousePos;

        // See MouseGestureLatch's own doc comment for why re-reading uiCapturesMouse every frame is wrong
        // for a held drag.
        private MouseGestureLatch _rightMouseGesture;
        private MouseGestureLatch _middleMouseGesture;
        private MouseGestureLatch _leftMouseGesture;
        private bool _pointerCaptureActive;

        Queue<float> _deltaHistoryX = new Queue<float>();
        Queue<float> _deltaHistoryY = new Queue<float>();

        Queue<float> _deltaAvgHistoryX = new Queue<float>();
        Queue<float> _deltaAvgHistoryY = new Queue<float>();
        private float _viewCenterDistance;

        public CameraComponent(ManaSceneNode owner) : base(owner)
        {
            owner.Context.Window.Resize += _Window_Resize;
            UpdateProjection();
            _viewCenterDistance = EstimateInitialViewCenterDistance(owner);
        }

        private void _Window_Resize(Vector2D<int> obj)
        {
            UpdateProjection();
        }

        public void UpdateProjection()
        {
            var viewportSize = GetProjectionViewportSize();

            AspectRatio = (float)viewportSize.X / (float)viewportSize.Y;
            if (OrthoGraphic)
            {
                OrthographicHeight = MathF.Max(1.0f, OrthographicHeight);
                _ProjectionMatrix = Matrix4x4.CreateOrthographic(OrthographicHeight * AspectRatio, OrthographicHeight, NearClipPlane, FarClipPlane);
                ViewParams = new Vector3(OrthographicHeight * AspectRatio, OrthographicHeight, NearClipPlane);
                return;
            }
            else
            {
                _ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(EffectiveFieldOfViewDegrees.ToRadians(), AspectRatio, NearClipPlane, FarClipPlane);
            }

            float planeHeight = NearClipPlane * MathF.Tan(EffectiveFieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
            float planeWidth = planeHeight * AspectRatio;

            ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
        }

        public void SetProjectionMode(bool orthographic)
        {
            if (OrthoGraphic == orthographic)
            {
                return;
            }

            OrthoGraphic = orthographic;
            UpdateProjection();
        }

        /// <summary>
        /// Points the camera down a world axis, orbiting whatever is worth looking at rather than wherever
        /// it happened to be pointing.
        /// <para>
        /// It used to orbit the point <see cref="_viewCenterDistance"/> units in front of the camera. That field is written in exactly two places, the constructor and
        /// <see cref="FlyToWorldPosition"/>, and the fly camera writes Owner.Position directly without
        /// touching it. So after any WASD/QE flight the "orbit centre" was a stale number pointing at empty
        /// space, and a preset swung the camera around that instead of around the scene: you pressed Top and
        /// arrived somewhere correct and empty. Far enough out and the room was past the far plane too,
        /// which is why it looked like the clip planes rather than the pivot.
        /// </para>
        /// </summary>
        public void SetViewPreset(CameraViewPreset preset)
        {
            Owner.Rotation = preset switch
            {
                CameraViewPreset.Back => new Vector3(0.0f, 180.0f.ToRadians(), 0.0f),
                CameraViewPreset.Left => new Vector3(0.0f, -90.0f.ToRadians(), 0.0f),
                CameraViewPreset.Right => new Vector3(0.0f, 90.0f.ToRadians(), 0.0f),
                CameraViewPreset.Top => new Vector3(-90.0f.ToRadians(), 0.0f, 0.0f),
                CameraViewPreset.Bottom => new Vector3(90.0f.ToRadians(), 0.0f, 0.0f),
                _ => Vector3.Zero,
            };

            var pivot = ResolveViewPresetPivot(out var distance);

            Owner.Position = pivot - Owner.Forward.Normalized() * distance;

            // Kept in step, so a second preset orbits the same pivot at the same range rather than
            // re-deriving a different one from the position this just wrote.
            _viewCenterDistance = distance;
            UpdateViewMatrix();
        }

        /// <summary>
        /// What a view preset should swing around, and from how far.
        /// <para>
        /// The selection when there is one -- "show me the top of this" is what the button is for with
        /// something picked. Otherwise the world origin, which is where the editor's room, grid and player
        /// start all are; the camera keeps its current range from it so the preset changes the angle rather
        /// than the zoom.
        /// </para>
        /// </summary>
        private Vector3 ResolveViewPresetPivot(out float distance)
        {
            var selection = Owner.Context?.World?.Selection;

            if (selection != null
                && TryComputeSelectionWorldBounds(
                    selection.SelectedComponent as MeshRendererComponent,
                    selection.SelectedNode,
                    out var worldMin,
                    out var worldMax))
            {
                var centre = (worldMin + worldMax) * 0.5f;
                var radius = MathF.Max(Vector3.Distance(centre, worldMax), 0.001f);

                distance = ClampViewPresetDistance(
                    radius / MathF.Sin(EffectiveFieldOfViewDegrees.ToRadians() * 0.5f) * FramingPaddingMultiplier,
                    NearClipPlane,
                    FarClipPlane);

                return centre;
            }

            distance = ClampViewPresetDistance(
                Vector3.Distance(Owner.Position, Vector3.Zero), NearClipPlane, FarClipPlane);

            return Vector3.Zero;
        }

        /// <summary>
        /// A range that actually shows the pivot. The upper bound is the real fix for "the preset worked and
        /// I still cannot see anything": a camera parked further from its target than the far plane renders
        /// nothing at all, and the presets were happy to put it there.
        /// <para>
        /// Static and pure so the bounds can be checked without a camera, which needs a window to exist.
        /// </para>
        /// </summary>
        public static float ClampViewPresetDistance(float distance, float nearClip, float farClip)
        {
            var minimum = MathF.Max(nearClip * 4.0f, 0.01f);
            var maximum = MathF.Max(minimum, farClip * 0.5f);

            if (!float.IsFinite(distance))
            {
                return minimum;
            }

            return Math.Clamp(distance, minimum, maximum);
        }

        // "Frame selected" (F key, see VoxelEditorGameMode) -- pulls the camera back/forward along its
        // CURRENT forward direction (orientation is untouched, matching SetViewPreset's own "target -
        // Forward*distance" pattern) until the selection's world bounds fit the vertical FOV. Prefers the
        // specific selected component's own bounds; falls back to the union of every MeshRendererComponent
        // on the selected node (e.g. selecting a bare PlanetNode shared root node with several patches).
        // Returns false (no-op) if nothing selected has any mesh bounds to frame.
        public bool TryFrameSelection(MeshRendererComponent? selectedComponent, ManaSceneNode? selectedNode)
        {
            if (!TryComputeSelectionWorldBounds(selectedComponent, selectedNode, out var worldMin, out var worldMax))
            {
                return false;
            }

            var center = (worldMin + worldMax) * 0.5f;
            var radius = Vector3.Distance(center, worldMax);
            FrameWorldBounds(center, radius);
            return true;
        }

        public static bool TryComputeSelectionWorldBounds(MeshRendererComponent? selectedComponent, ManaSceneNode? selectedNode, out Vector3 worldMin, out Vector3 worldMax)
        {
            // Local function can't capture the method's own out parameters directly (C# forbids it), so
            // accumulate into plain locals and assign the out parameters once at the end.
            var min = Vector3.Zero;
            var max = Vector3.Zero;
            var found = false;

            void Include(MeshRendererComponent renderer)
            {
                if (!renderer.TryGetWorldBounds(out var rendererMin, out var rendererMax))
                {
                    return;
                }

                min = found ? Vector3.Min(min, rendererMin) : rendererMin;
                max = found ? Vector3.Max(max, rendererMax) : rendererMax;
                found = true;
            }

            // Recurses into children -- a node like PlanetNode never carries a MeshRendererComponent
            // itself (it's a pure container), only its descendants do, so framing the whole thing
            // requires walking the subtree rather than just its own direct Components.
            void IncludeSubtree(ManaSceneNode node)
            {
                foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
                {
                    Include(renderer);
                }

                foreach (var child in node.Children)
                {
                    IncludeSubtree(child);
                }
            }

            if (selectedComponent != null)
            {
                Include(selectedComponent);
            }
            else if (selectedNode != null)
            {
                IncludeSubtree(selectedNode);
            }

            worldMin = min;
            worldMax = max;
            return found;
        }

        // Padding keeps the object from touching the screen edge; distance is clamped to at least twice
        // the near clip plane so an extremely small/degenerate bounds (e.g. a single-point mesh) can't
        // put the camera on top of (or behind) its own near plane.
        private const float FramingPaddingMultiplier = 1.25f;

        // Below this, "how much window is visible" has collapsed to a sliver (or nothing has published a
        // viewport at all) and treating it literally would send the camera to a wildly wrong distance.
        private const float MinVisibleHalfFovRadians = 0.02f;

        // A plain FlyToWorldPosition (forward-axis-only, see below) lands the framed point exactly at the
        // centre of the WHOLE window, not the centre of whatever is left of it once docked panels
        // (Outliner, Inspector, tool windows) have taken their share -- so on screen the selection reads
        // both too small (sized against the full window) and off-centre (aimed at a point a covered
        // panel may sit right on top of). This sizes the distance against the visible viewport
        // (Mana.Code.UI.ManaEditorGizmos.Viewport, published every frame by ViewportOverlayLayer) and
        // then nudges the camera sideways/up, still without rotating it, so the selection projects to
        // that viewport's own centre instead of the window's.
        private void FrameWorldBounds(Vector3 worldCenter, float worldRadius)
        {
            var radius = MathF.Max(worldRadius, 0.05f);
            var viewport = Mana.Code.UI.ManaEditorGizmos.Viewport;
            var windowSize = Context.Window.Size;

            var verticalHalfFovRad = EffectiveFieldOfViewDegrees.ToRadians() * 0.5f;
            var horizontalHalfFovRad = MathF.Atan(MathF.Tan(verticalHalfFovRad) * AspectRatio);

            var (widthFraction, heightFraction) = GetVisibleViewportFraction(viewport, windowSize);
            var visibleVerticalHalfFovRad = MathF.Max(
                MathF.Atan(MathF.Tan(verticalHalfFovRad) * heightFraction), MinVisibleHalfFovRadians);
            var visibleHorizontalHalfFovRad = MathF.Max(
                MathF.Atan(MathF.Tan(horizontalHalfFovRad) * widthFraction), MinVisibleHalfFovRadians);

            var distanceForHeight = radius / MathF.Sin(visibleVerticalHalfFovRad);
            var distanceForWidth = radius / MathF.Sin(visibleHorizontalHalfFovRad);
            var distance = MathF.Max(MathF.Max(distanceForHeight, distanceForWidth) * FramingPaddingMultiplier, NearClipPlane * 2.0f);

            // How far off the window's own centre the viewport's centre sits, in normalized device
            // coordinates -- i.e. exactly the on-screen shift the selection needs so it lands there
            // instead. At distance D a full NDC unit of screen X/Y corresponds to D*tan(halfFov) world
            // units, so that same NDC offset converts to a camera-local lateral/vertical translation by
            // just scaling by that, with the sign flipped (moving the camera right moves what it sees
            // left on screen).
            var (ndcX, ndcY) = GetViewportCenterNdcOffset(viewport, windowSize);
            var lateralOffset = -ndcX * distance * MathF.Tan(horizontalHalfFovRad);
            var verticalOffset = -ndcY * distance * MathF.Tan(verticalHalfFovRad);

            var targetPosition = worldCenter
                - Owner.Forward.Normalized() * distance
                + Owner.Right.Normalized() * lateralOffset
                + Owner.Up.Normalized() * verticalOffset;
            _viewCenterDistance = distance;
            BeginFramingTransition(targetPosition);
        }

        // Snapping straight to the framed position reads as a jump cut; easing there over a short,
        // fixed duration instead (ticked from UpdateComponent, like every other per-frame camera
        // motion) keeps the "where did the selection go" moment readable without slowing the shortcut
        // down enough to feel laggy.
        private const float FramingTransitionDurationSeconds = 0.25f;

        private bool _isFramingTransitionActive;
        private Vector3 _framingTransitionStart;
        private Vector3 _framingTransitionTarget;
        private float _framingTransitionElapsedSeconds;

        private void BeginFramingTransition(Vector3 targetPosition)
        {
            _framingTransitionStart = Owner.Position;
            _framingTransitionTarget = targetPosition;
            _framingTransitionElapsedSeconds = 0.0f;
            _isFramingTransitionActive = true;
        }

        private void UpdateFramingTransition()
        {
            // Clamped the same way ManaAudioSourceComponent's per-frame work is: a stutter should not
            // make the camera leap most of the way there in one tick.
            var deltaSeconds = (float)Math.Clamp(ManaTime.GlobalTime.DeltaTime.TotalSeconds, 0.0, 0.1);
            _framingTransitionElapsedSeconds += deltaSeconds;

            var t = Math.Clamp(_framingTransitionElapsedSeconds / FramingTransitionDurationSeconds, 0.0f, 1.0f);
            var eased = 1.0f - MathF.Pow(1.0f - t, 3.0f); // ease-out cubic: fast start, gentle arrival
            Owner.Position = Vector3.Lerp(_framingTransitionStart, _framingTransitionTarget, eased);
            UpdateViewMatrix();

            if (t >= 1.0f)
            {
                _isFramingTransitionActive = false;
            }
        }

        // Both fractions are 1 (the whole window) when nothing has published a viewport yet -- headless
        // tests, ManaSceneViewer, or the first frame -- which reproduces the old unconstrained behaviour.
        private static (float Width, float Height) GetVisibleViewportFraction(UiRect viewport, Vector2D<int> windowSize)
        {
            if (viewport.IsEmpty || windowSize.X <= 0 || windowSize.Y <= 0)
            {
                return (1.0f, 1.0f);
            }

            return (
                Math.Clamp(viewport.Width / windowSize.X, 0.0f, 1.0f),
                Math.Clamp(viewport.Height / windowSize.Y, 0.0f, 1.0f));
        }

        // Zero (no shift) under the same "nothing published yet" fallback as GetVisibleViewportFraction.
        // UI rects are Y-down pixels; NDC Y is up, hence the flip on the Y term.
        private static (float X, float Y) GetViewportCenterNdcOffset(UiRect viewport, Vector2D<int> windowSize)
        {
            if (viewport.IsEmpty || windowSize.X <= 0 || windowSize.Y <= 0)
            {
                return (0.0f, 0.0f);
            }

            var center = viewport.Center;
            var ndcX = (center.X - windowSize.X * 0.5f) / (windowSize.X * 0.5f);
            var ndcY = -(center.Y - windowSize.Y * 0.5f) / (windowSize.Y * 0.5f);
            return (ndcX, ndcY);
        }


        // Repositions the camera along its CURRENT forward direction (orientation untouched, same idiom
        // as SetViewPreset/FrameWorldBounds) so worldPosition ends up exactly distance units in front of
        // it. For callers with no mesh/bounding box to frame -- e.g. a geocoded search result, which is
        // just a bare point -- so the caller picks a sensible arrival distance itself instead of this
        // deriving one from FOV+radius the way FrameWorldBounds does.
        public void FlyToWorldPosition(Vector3 worldPosition, float distance)
        {
            var clampedDistance = MathF.Max(distance, NearClipPlane * 2.0f);
            Owner.Position = worldPosition - Owner.Forward.Normalized() * clampedDistance;
            _viewCenterDistance = clampedDistance;
            UpdateViewMatrix();
        }

        public void UpdateViewMatrix()
        {
            Vector3 forward = Owner.Forward.Normalized();
            Vector3 up = Owner.Up.Normalized();
            _ViewMatrix = Matrix4x4.CreateLookAt(Owner.Position, Owner.Position + forward, up);
        }

        // When set, this camera stops flying and rides a boom instead. The camera still owns its own
        // pose; the arm only supplies where that pose should be (see ApplyAttachedPose, which runs
        // after the scene graph has moved so it reads the target's position from this frame, not last).
        public ManaSpringArmComponent? AttachedSpringArm { get; private set; }

        public bool IsAttached => AttachedSpringArm != null;

        public void AttachToSpringArm(ManaSpringArmComponent springArm)
        {
            AttachedSpringArm = springArm;
            springArm.SnapToTarget();
            ApplyAttachedPose(0.0f);
        }

        // Leaves the camera exactly where the arm last put it, so releasing a character drops you into
        // free flight from that view instead of snapping back somewhere else.
        public void DetachFromSpringArm()
        {
            AttachedSpringArm = null;
            UpdateProjection();

            // Detaching mid-drag is a real path -- stopping play releases the pawn, which detaches the
            // boom -- and nothing else would ever come back to give the pointer back.
            ReleasePointerCapture();
        }

        /// <summary>
        /// Gives the pointer back if this camera is holding it. Safe to call when it is not.
        /// </summary>
        public void ReleasePointerCapture()
        {
            if (!_pointerCaptureActive)
            {
                return;
            }

            _pointerCaptureActive = false;
            ManaPointerCapture.IsActive = false;

            try
            {
                var mouse = Context.Input?.Mice.FirstOrDefault();
                if (mouse != null && mouse.Cursor.IsSupported(CursorMode.Normal))
                {
                    mouse.Cursor.CursorMode = CursorMode.Normal;
                }
            }
            catch
            {
                // Same reasoning as UpdatePointerCapture: never fail a frame over the cursor.
            }
        }

        // Called from ManaWorld.Update after the scenes have updated. Split out from UpdateComponent
        // because the camera node is updated before the scene graph: evaluating the arm there would
        // follow the target's previous-frame position and visibly trail it.
        public void ApplyAttachedPose(float deltaSeconds)
        {
            var springArm = AttachedSpringArm;
            if (springArm == null)
            {
                return;
            }

            springArm.Evaluate(deltaSeconds);
            UpdateProjection();

            Owner.Position = springArm.CameraPosition;

            // The arm's yaw/pitch are in ManaSceneNode.Forward's own convention, so they go straight
            // onto Rotation with no conversion.
            Owner.Rotation = new Vector3(springArm.PitchRadians, springArm.YawRadians, 0.0f);

            // Matrices written directly rather than via Owner.UpdateTransform(), which would tick this
            // component again and apply a second frame of mouse input -- orbiting at double speed.
            // Same direct-write pattern ManaCollisionComponent uses for its physics sync.
            var localTransform =
                Matrix4x4.CreateScale(Owner.Scale) *
                Matrix4x4.CreateFromQuaternion(Owner.Rotation.ToQuaternionFromEuler()) *
                Matrix4x4.CreateTranslation(Owner.Position);

            Owner.LocalTransform = localTransform;
            Owner.WorldTransform = Owner.Parent != null ? localTransform * Owner.Parent.WorldTransform : localTransform;

            UpdateViewMatrix();
        }

        // Same gestures as free flight, redirected at the boom: either mouse button looks around, scroll
        // changes the arm length instead of the fly speed. The pose itself is applied later, in
        // ApplyAttachedPose.
        //
        // Right and left are both fed to the boom and differ only in what they tell the character to do
        // about it -- see ManaSpringArmComponent.IsSteerGestureActive. Left is opt-in because in the
        // editor a left-press already means something (OrbitOnLeftDrag).
        private void UpdateAttachedInput()
        {
            var springArm = AttachedSpringArm!;
            var mouseState = Context.Input.Mice.First();

            var externalCapturesMouse = ShouldCaptureMouseInput?.Invoke() == true;
            var uiCapturesMouse = GetUiWantsCaptureMouse() || externalCapturesMouse;

            // Evaluated as two separate calls rather than one short-circuited expression, for the same
            // reason the free camera does: each button owns a latch that has to be advanced every frame.
            var steerActive = _rightMouseGesture.Update(mouseState.IsButtonPressed(MouseButton.Right), uiCapturesMouse);
            var leftDown = springArm.MouseLookControls && mouseState.IsButtonPressed(MouseButton.Left);
            var orbitActive = _leftMouseGesture.Update(leftDown, uiCapturesMouse);

            // Published even on the frames the mouse does not move: what reads these needs to know the
            // drag is still held, not that it moved.
            springArm.IsSteerGestureActive = steerActive;
            springArm.IsOrbitGestureActive = orbitActive;

            var lookActive = steerActive || orbitActive;

            // The pointer is grabbed for the whole drag so a long turn is not cut short by the edge of
            // the screen. Grabbing and releasing both teleport the OS pointer, so the frame either
            // happens on contributes one enormous bogus delta -- hence dropping that frame's motion,
            // which costs nothing perceptible at the start or end of a drag.
            var captureChanged = UpdatePointerCapture(mouseState, lookActive && springArm.MouseLookControls);

            const float delta = 0.001f;
            float dxAvg = 0, dyAvg = 0;
            UpdateAverage(delta, lookActive && !captureChanged, ref dxAvg, ref dyAvg);

            if (lookActive)
            {
                // Same 0.4 scaling as the free camera's look, so the two modes feel identical.
                springArm.AddLookInput(dxAvg * 0.4f, dyAvg * 0.4f);
            }

            if (!uiCapturesMouse)
            {
                var scroll = mouseState.ScrollWheels[0];
                if (scroll.Y != 0.0f)
                {
                    springArm.AddZoomInput(scroll.Y);
                }
            }
        }

        /// <summary>
        /// Hides and locks the pointer for the duration of a look drag, and puts it back afterwards.
        /// Returns whether the state changed this frame, which is the caller's cue to discard this
        /// frame's mouse delta.
        /// </summary>
        private bool UpdatePointerCapture(IMouse mouseState, bool wanted)
        {
            if (wanted == _pointerCaptureActive)
            {
                return false;
            }

            _pointerCaptureActive = wanted;

            // Set before touching the cursor: the UI layer writes the cursor mode from its own
            // state every frame, and this is what tells them to leave it alone (see ManaPointerCapture).
            ManaPointerCapture.IsActive = wanted;

            try
            {
                if (wanted)
                {
                    // Raw first for unscaled motion; Disabled is the same lock without that, and Hidden
                    // at least takes the cursor out of the way on a platform offering neither.
                    foreach (var mode in new[] { CursorMode.Raw, CursorMode.Disabled, CursorMode.Hidden })
                    {
                        if (mouseState.Cursor.IsSupported(mode))
                        {
                            mouseState.Cursor.CursorMode = mode;
                            break;
                        }
                    }
                }
                else if (mouseState.Cursor.IsSupported(CursorMode.Normal))
                {
                    mouseState.Cursor.CursorMode = CursorMode.Normal;
                }
            }
            catch
            {
                // A backend with no cursor control at all (the browser) still gets a working look drag,
                // just without the grab -- so this is not worth failing a frame over.
            }

            return true;
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            if (_isFramingTransitionActive)
            {
                // Manual control reclaims the camera immediately rather than fighting the lerp -- anything
                // that flies or reattaches it while "F to frame selected" is still easing in should just win.
                var cancelKeyboard = Context.Input.Keyboards.FirstOrDefault();
                var cancelMouse = Context.Input.Mice.FirstOrDefault();
                bool manualMovementActive = AttachedSpringArm != null
                    || (cancelMouse != null && (cancelMouse.IsButtonPressed(MouseButton.Right) || cancelMouse.IsButtonPressed(MouseButton.Middle)))
                    || (cancelKeyboard != null && (cancelKeyboard.IsKeyPressed(Key.W) || cancelKeyboard.IsKeyPressed(Key.A)
                        || cancelKeyboard.IsKeyPressed(Key.S) || cancelKeyboard.IsKeyPressed(Key.D)
                        || cancelKeyboard.IsKeyPressed(Key.Q) || cancelKeyboard.IsKeyPressed(Key.E)));

                if (manualMovementActive)
                {
                    _isFramingTransitionActive = false;
                }
                else
                {
                    UpdateFramingTransition();
                }
            }

            if (AttachedSpringArm != null)
            {
                UpdateAttachedInput();
                return;
            }

            var MouseState = Context.Input.Mice.First();
            var KeyboardState = Context.Input.Keyboards.First();
            bool externalCapturesMouse = ShouldCaptureMouseInput?.Invoke() == true;
            bool uiCapturesMouse = GetUiWantsCaptureMouse() || externalCapturesMouse;
            // Without this the WASD/QE block below fires while typing into any UI text field.
            bool uiCapturesKeyboard = GetUiWantsCaptureKeyboard();

            // Only the WASD ground-plane keys are surrenderable, not the whole fly block: something
            // else can want WASD (a possessed character) while the camera still needs Q/E to change
            // height and look at it.
            bool horizontalFlyKeysAvailable = !uiCapturesKeyboard && ShouldCaptureKeyboardInput?.Invoke() != true;

            var position = Owner.Position;
            var rotation = Owner.Rotation;
            var rotationEuler = rotation;

            float pitch = rotationEuler.X;
            float yaw = rotationEuler.Y;
            float roll = rotationEuler.Z;

            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
            Vector3 forward = Vector3.TransformNormal(-Vector3.UnitZ, rotationMatrix);
            Vector3 right = Vector3.TransformNormal(Vector3.UnitX, rotationMatrix);

            var worldUp = new Vector3(0, 1, 0);

            float delta = 0.001f;
            float dxAvg = 0, dyAvg = 0;

            // Evaluated as two separate calls rather than one short-circuited "||" expression: each button
            // owns its own latch, and short-circuiting after the first true result would skip clearing the
            // other one on the frame it releases, leaving it stuck active for whatever pressed it next.
            var rightGestureActive = _rightMouseGesture.Update(MouseState.IsButtonPressed(MouseButton.Right), uiCapturesMouse);
            var middleGestureActive = _middleMouseGesture.Update(MouseState.IsButtonPressed(MouseButton.Middle), uiCapturesMouse);
            bool cameraMouseActive = rightGestureActive || middleGestureActive;
            Vector2 mouseDelta = UpdateAverage(delta, cameraMouseActive, ref dxAvg, ref dyAvg);

            if (horizontalFlyKeysAvailable)
            {
                if (KeyboardState.IsKeyPressed(Key.W))
                {
                    position += forward * +delta * _MovementSpeed;
                }

                if (KeyboardState.IsKeyPressed(Key.S))
                {
                    position += forward * -delta * _MovementSpeed;
                }

                if (KeyboardState.IsKeyPressed(Key.A))
                {
                    position += right * -delta * _MovementSpeed;
                }

                if (KeyboardState.IsKeyPressed(Key.D))
                {
                    position += right * delta * _MovementSpeed;
                }
            }

            if (!uiCapturesKeyboard && (!VerticalFlyKeysNeedLook || cameraMouseActive))
            {
                if (KeyboardState.IsKeyPressed(Key.E))
                {
                    position += worldUp * +delta * _MovementSpeed;
                }

                if (KeyboardState.IsKeyPressed(Key.Q))
                {
                    position += worldUp * -delta * _MovementSpeed;
                }
            }

            if (middleGestureActive)
            {
                position += GetPanTranslation(mouseDelta);
            }

            if (!uiCapturesMouse && OrthoGraphic && !MouseState.IsButtonPressed(MouseButton.Right))
            {
                var scroll = MouseState.ScrollWheels[0];
                if (scroll.Y != 0.0f)
                {
                    ZoomOrthographicAtMouse(scroll.Y, MouseState.Position, ref position);
                }

                if (TouchpadPanInOrthographic && scroll.X != 0.0f)
                {
                    const float touchpadPanPixelsPerScrollUnit = 48.0f;
                    position += GetPanTranslation(new Vector2(scroll.X, 0.0f) * touchpadPanPixelsPerScrollUnit);
                }
            }

            var transform = Owner.LocalTransform;
            transform.Translation = Vector3.Zero;
            transform *= Matrix4x4.CreateTranslation(position);
            Owner.LocalTransform = transform;

            if (rightGestureActive)
            {
                rotationEuler.Y += dxAvg * 0.4f;
                rotationEuler.X += dyAvg * 0.4f;
                rotationEuler.X = rotationEuler.X.Clamp(-89.0f.ToRadians(), 89.0f.ToRadians());
            }

            if (rightGestureActive && MouseState.ScrollWheels[0].Y != 0)
            {
                const float scrollSpeedMultiplierPerNotch = 1.5f;
                _MovementSpeed *= MathF.Pow(scrollSpeedMultiplierPerNotch, MouseState.ScrollWheels[0].Y);
                _MovementSpeed = _MovementSpeed.Clamp(0.25f, 25000.0f);
            }

            Owner.Position = position;
            Owner.Rotation = rotationEuler;
            UpdateViewMatrix();

        }

        /// <summary>
        /// Whether the UI owns the mouse. Routed through the game mode rather than asked of the UI
        /// directly, so a camera running without one (a test harness) simply reports no capture.
        /// </summary>
        private bool GetUiWantsCaptureMouse()
        {
            try
            {
                return Context?.GameMode?.UiWantsMouse ?? false;
            }
            catch
            {
                return false;
            }
        }

        private bool GetUiWantsCaptureKeyboard()
        {
            try
            {
                return Context?.GameMode?.UiWantsKeyboard ?? false;
            }
            catch
            {
                return false;
            }
        }

        public float[] GetMouseDeltaHistoryXSnapshot() => _deltaHistoryX.ToArray();

        public float[] GetMouseDeltaHistoryYSnapshot() => _deltaHistoryY.ToArray();

        private Vector3 GetPanTranslation(Vector2 screenDelta)
        {
            float unitsPerPixel = GetPanUnitsPerPixel();
            return (-Owner.Right * screenDelta.X + Owner.Up * screenDelta.Y) * unitsPerPixel * PanSpeed;
        }

        private void ZoomOrthographicAtMouse(float scrollY, Vector2 mousePosition, ref Vector3 position)
        {
            const float zoomMultiplierPerNotch = 1.15f;
            float oldOrthographicHeight = MathF.Max(1.0f, OrthographicHeight);
            Vector3 worldPointBeforeZoom = GetOrthographicWorldPoint(mousePosition, oldOrthographicHeight, position);

            OrthographicHeight = oldOrthographicHeight / MathF.Pow(zoomMultiplierPerNotch, scrollY);
            OrthographicHeight = OrthographicHeight.Clamp(1.0f, 1000.0f);
            UpdateProjection();

            Vector3 worldPointAfterZoom = GetOrthographicWorldPoint(mousePosition, OrthographicHeight, position);
            position += worldPointBeforeZoom - worldPointAfterZoom;
        }

        private float GetPanUnitsPerPixel()
        {
            float viewportHeight = MathF.Max(1.0f, Context.Window.Size.Y);
            if (OrthoGraphic)
            {
                return MathF.Max(1.0f, OrthographicHeight) / viewportHeight;
            }

            float targetDistance = GetViewCenterDistance();
            float visibleHeight = 2.0f * targetDistance * MathF.Tan(EffectiveFieldOfViewDegrees.ToRadians() * 0.5f);
            return visibleHeight / viewportHeight;
        }

        private float GetViewCenterDistance()
        {
            if (_viewCenterDistance <= NearClipPlane)
            {
                _viewCenterDistance = EstimateInitialViewCenterDistance(Owner);
            }

            return MathF.Max(NearClipPlane, _viewCenterDistance);
        }

        private float EstimateInitialViewCenterDistance(ManaSceneNode owner)
        {
            float originDistance = Vector3.Distance(owner.Position, Vector3.Zero);
            return MathF.Max(originDistance, MathF.Max(OrthographicHeight, 1.0f));
        }

        private Vector3 GetOrthographicWorldPoint(Vector2 screenPosition, float orthographicHeight, Vector3 cameraPosition)
        {
            float viewportWidth = MathF.Max(1.0f, Context.Window.Size.X);
            float viewportHeight = MathF.Max(1.0f, Context.Window.Size.Y);
            float aspectRatio = viewportWidth / viewportHeight;

            float normalizedX = (screenPosition.X / viewportWidth) * 2.0f - 1.0f;
            float normalizedY = 1.0f - (screenPosition.Y / viewportHeight) * 2.0f;

            float orthographicWidth = orthographicHeight * aspectRatio;
            Vector3 cameraRight = Owner.Right.Normalized();
            Vector3 cameraUp = Owner.Up.Normalized();

            return cameraPosition +
                   cameraRight * (normalizedX * orthographicWidth * 0.5f) +
                   cameraUp * (normalizedY * orthographicHeight * 0.5f);
        }

        Vector2 UpdateAverage(float deltaTime, bool acceptMouseDelta, ref float dxAvg, ref float dyAvg)
        {
            var MouseState = Context.Input.Mice.First();

            var mouseDelta = MouseState.Position - _LastMousePos;
            _LastMousePos = MouseState.Position;
            if (!acceptMouseDelta)
            {
                dxAvg = 0.0f;
                dyAvg = 0.0f;
                return Vector2.Zero;
            }

            float dx = -mouseDelta.X * deltaTime * _MouseSensitivity.X;
            float dy = -mouseDelta.Y * deltaTime * _MouseSensitivity.Y;
            _deltaHistoryX.Enqueue(dx);
            _deltaHistoryY.Enqueue(dy);

            int avgSize = Math.Min(_MouseAverageSize, _deltaHistoryX.Count);
            dxAvg = dx;
            dyAvg = dy;
            _deltaAvgHistoryX.Enqueue(dx);
            _deltaAvgHistoryY.Enqueue(dy);

            if (_deltaHistoryX.Count > 90)
            {
                _deltaHistoryX.Dequeue();
                _deltaHistoryY.Dequeue();
                _deltaAvgHistoryX.Dequeue();
                _deltaAvgHistoryY.Dequeue();
            }

            return mouseDelta;
        }

        public Ray ProjectScreenToWorld(in Vector3 MousePositionStart,  in Vector3 MousePositionEnd)
        {            
            Matrix4x4.Invert(_ViewMatrix * _ProjectionMatrix, out Matrix4x4 inverseWorldMatrix);
            Matrix4x4.Invert(_ProjectionMatrix, out Matrix4x4 inverseProjectionMatrix);
            Matrix4x4.Invert(_ViewMatrix, out Matrix4x4 inverseViewMatrix);

            Matrix4x4 world = _ViewMatrix * _ProjectionMatrix;

            var windowSize = GetProjectionViewportSize();
            var mouseStart = ConvertMousePositionForProjection(MousePositionStart);
            var mouseEnd = ConvertMousePositionForProjection(MousePositionEnd);

            Vector4 viewportCoordinate;
            viewportCoordinate.X = 2.0f * mouseStart.X / (float)windowSize.X - 1;
            viewportCoordinate.Y = -(2.0f * mouseStart.Y / (float)windowSize.Y - 1);
            viewportCoordinate.Z = mouseStart.Z;
            viewportCoordinate.W = 1.0f;

            Vector4 viewportCoordinateEnd;
            viewportCoordinateEnd.X = 2.0f * mouseEnd.X / (float)windowSize.X - 1;
            viewportCoordinateEnd.Y = -(2.0f * mouseEnd.Y / (float)windowSize.Y - 1);
            viewportCoordinateEnd.Z = mouseEnd.Z;
            viewportCoordinateEnd.W = 1.0f;
            
            Vector4 eyeSpaceCoordinate = Vector4.Transform(viewportCoordinate, inverseProjectionMatrix);
            Vector4 eyeSpaceCoordinateEnd = Vector4.Transform(viewportCoordinateEnd, inverseProjectionMatrix);

            Vector4 worldCoordinate = Vector4.Transform(eyeSpaceCoordinate, inverseViewMatrix);            
            Vector4 worldCoordinateEnd = Vector4.Transform(eyeSpaceCoordinateEnd, inverseViewMatrix);

            worldCoordinate /= worldCoordinate.W;
            worldCoordinateEnd /= worldCoordinateEnd.W;
            
            return new Ray(worldCoordinate.ToVector3(), (worldCoordinateEnd - worldCoordinate).ToVector3().Normalized());
        }

        private Vector2D<int> GetProjectionViewportSize()
        {
            if (Context.GraphicsBackend == ManaGraphicsBackendKind.WebGpu &&
                Context.Window.FramebufferSize.X > 0 &&
                Context.Window.FramebufferSize.Y > 0)
            {
                return Context.Window.FramebufferSize;
            }

            return Context.Window.Size;
        }

        private Vector3 ConvertMousePositionForProjection(Vector3 mousePosition)
        {
            if (Context.GraphicsBackend != ManaGraphicsBackendKind.WebGpu)
            {
                return mousePosition;
            }

            var windowSize = Context.Window.Size;
            var framebufferSize = Context.Window.FramebufferSize;
            if (windowSize.X <= 0 || windowSize.Y <= 0 ||
                framebufferSize.X <= 0 || framebufferSize.Y <= 0 ||
                (windowSize.X == framebufferSize.X && windowSize.Y == framebufferSize.Y))
            {
                return mousePosition;
            }

            if (mousePosition.X > windowSize.X + 1.0f || mousePosition.Y > windowSize.Y + 1.0f)
            {
                return mousePosition;
            }

            return new Vector3(
                mousePosition.X * framebufferSize.X / windowSize.X,
                mousePosition.Y * framebufferSize.Y / windowSize.Y,
                mousePosition.Z);
        }

        public void AddDebugCameraRayTest(ManaShapeFactory shapes)
        {
            var transform = Owner.WorldTransform;

            float pegSize = 0.25f * 0.5f;
 
            shapes.AddCube(Vector3.UnitX * 0.5f, Colors.Red, new Vector3(1, pegSize, pegSize)).Name = "Camera Debug X Axis";
            shapes.AddCube(Vector3.UnitY * 0.5f, Colors.Green, new Vector3(pegSize, 1, pegSize)).Name = "Camera Debug Y Axis";
            shapes.AddCube(Vector3.UnitZ * 0.5f, Colors.Blue, new Vector3(pegSize, pegSize, 1)).Name = "Camera Debug Z Axis";

            var xArrow = shapes.AddPyramid(Vector3.UnitX * 1f, Colors.Red, 0.25f);
            xArrow.Name = "Camera Debug X Arrow";
            xArrow.Rotation = new Vector3(0, 0, -90.0f.ToRadians());

            var yArrow = shapes.AddPyramid(Vector3.UnitY * 1f, Colors.Green, 0.25f);
            yArrow.Name = "Camera Debug Y Arrow";
            yArrow.Rotation = new Vector3(0, 0, 0);

            var yMidArrow = shapes.AddPyramid(Vector3.UnitY * 0.75f, Colors.Green, 0.25f);
            yMidArrow.Name = "Camera Debug Y Mid Arrow";
            yMidArrow.Rotation = new Vector3(0, 0, 0);

            var zArrow = shapes.AddPyramid(Vector3.UnitZ * 1f, Colors.Blue, 0.25f);
            zArrow.Name = "Camera Debug Z Arrow";
            zArrow.Rotation = new Vector3(90.0f.ToRadians(), 0, 0);

            shapes.AddQuad(Vector3.Zero, Colors.Silver, Vector3.One).Name = "Camera Debug View Plane";

            shapes.AddSphere(transform.Translation, Colors.Red, 0.004f).Name = "Camera Debug Ray Origin";
            {
                float planeHeight = NearClipPlane * MathF.Tan(EffectiveFieldOfViewDegrees.ToRadians() * 0.5f) * 2.0f;
                float planeWidth = planeHeight * AspectRatio;

                ViewParams = new Vector3(planeWidth, planeHeight, NearClipPlane);
                ViewAxes[0] = Owner.Right;
                ViewAxes[1] = Owner.Up;
                ViewAxes[2] = Owner.Forward;

                Vector3 bottomLeftLocal = new Vector3(-planeWidth / 2, -planeHeight / 2, NearClipPlane);

                for (int x = 0; x < DebugPointCount.X; x++)
                {
                    for (int y = 0; y < DebugPointCount.Y; y++)
                    {
                        float tx = x / (DebugPointCount.X - 1.0f);
                        float ty = y / (DebugPointCount.Y - 1.0f);

                        Vector3 pointLocal = bottomLeftLocal + new Vector3(planeWidth * tx, planeHeight * ty, 0);
                        //Vector3 point = Owner.Position + pointLocal.TransformNormal(Owner.WorldTransform);
                        Vector3 point = Owner.Position + Owner.Right * pointLocal.X + Owner.Up * pointLocal.Y + Owner.Forward * pointLocal.Z;

                        //Game.Instance._World.AddSphere(point, new Vector4(tx, ty, 1.0f, 1.0f), 0.09f);
                    }
                }
            }

        }

    }
}
