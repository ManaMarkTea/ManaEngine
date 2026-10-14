using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaVoxel.Code.UI;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// Three-dimensional editor handles attached to the current selection. The geometry is transient and
    /// picked separately from the scene so a handle always wins its own click without becoming selection.
    /// </summary>
    internal sealed class SceneTransformGizmo
    {
        // Arrow centre (0.94) plus half its height (0.34 / 2), measured from the pivot.
        internal const float TranslationTipDistance = 1.11f;
        private static readonly Vector4 XColor = new(0.92f, 0.18f, 0.16f, 1.0f);
        private static readonly Vector4 YColor = new(0.22f, 0.82f, 0.28f, 1.0f);
        private static readonly Vector4 ZColor = new(0.18f, 0.46f, 0.96f, 1.0f);
        private static readonly Vector4 HoverColor = new(1.0f, 0.88f, 0.20f, 1.0f);

        private readonly ManaWorld _world;
        private readonly ManaSceneNode _root;
        private readonly ManaScene _pickScene;
        private readonly EditorUndoHistory _history;
        private readonly EditorSnapSettings _snap;
        private readonly Dictionary<ManaSceneNode, GizmoAxis> _axisByNode = new();
        private readonly List<(GizmoAxis Axis, MeshRendererComponent Renderer)> _renderers = new();

        private ManaSceneNode? _target;
        private SceneTransformGizmoMode? _builtMode;
        private GizmoAxis? _hoveredAxis;
        private GizmoAxis? _activeAxis;
        private bool _pointerWasDown;
        private Vector3 _pivotWorld;
        private Quaternion _orientationWorld = Quaternion.Identity;
        private float _displayScale = 1.0f;
        private Vector3 _dragAxisWorld;
        private Vector3 _dragPlaneNormal;
        private Vector3 _dragPivotWorld;
        private float _dragStartAxisParameter;
        private Vector3 _dragStartPlaneVector;
        private Vector3 _dragStartPosition;
        private Vector3 _dragStartRotation;
        private Vector3 _dragStartScale;
        private SceneTransformGizmoMode _dragMode;
        private bool _dragInPlane;
        private Vector3 _dragPlaneStart;
        private Vector3 _dragPlaneU;
        private Vector3 _dragPlaneV;

        // Scale box: the selection's own bounds, with a handle on each face (edge) and corner.
        private Vector3 _boxMin;
        private Vector3 _boxMax;
        private SceneScaleBoxHandle? _boxHovered;
        private SceneScaleBoxHandle? _boxActive;
        private Matrix4x4 _boxStartWorld;
        private Matrix4x4 _boxWorldToLocal;
        private Vector3 _boxHandleWorld;
        private Vector3 _boxPlaneNormal;
        private Vector3 _boxStartHitLocal;

        private enum GizmoAxis
        {
            X,
            Y,
            Z,
            XY,
            YZ,
            XZ
        }

        public SceneTransformGizmo(ManaWorld world, EditorUndoHistory history, EditorSnapSettings snap)
        {
            _world = world;
            _history = history;
            _snap = snap;
            _root = new ManaSceneNode(world.Context, Matrix4x4.Identity)
            {
                Name = "Transform Gizmo (Transient)",
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true
            };
            _pickScene = new ManaScene(world.Context);
            _pickScene.Children.Add(_root);
        }

        /// <summary>The Scale box handle under the pointer or being dragged.</summary>
        public SceneScaleBoxHandle? HighlightedBoxHandle => _boxActive ?? _boxHovered;

        /// <summary>Returns true while a handle owns or is under the pointer.</summary>
        public bool Update(
            ManaSceneNode? selectedNode,
            SceneTransformGizmoMode mode,
            bool visible,
            bool inputCaptured,
            IMouse mouse)
        {
            var pointerDown = mouse.IsButtonPressed(MouseButton.Left);

            if (!visible || selectedNode == null)
            {
                Hide();
                _pointerWasDown = pointerDown;
                return false;
            }

            if (!ReferenceEquals(_target, selectedNode))
            {
                EndDrag();
                EndBoxDrag();
                _target = selectedNode;
            }

            var effectiveMode = _activeAxis.HasValue ? _dragMode : mode;
            // Scale works on the selection's box when it has one; an empty group keeps the axis handles.
            if (!_activeAxis.HasValue && (_boxActive.HasValue || effectiveMode == SceneTransformGizmoMode.Scale)
                && UpdateScaleBox(selectedNode, inputCaptured, mouse, pointerDown, out var boxCaptured))
            {
                _pointerWasDown = pointerDown;
                return boxCaptured;
            }

            _boxHovered = null;
            EnsureBuilt(effectiveMode);
            EnsureAttached();
            SyncTransform(selectedNode);

            var camera = _world.MainCamera;
            var ray = camera.ProjectScreenToWorld(
                new Vector3(mouse.Position, 0.0f),
                new Vector3(mouse.Position, 1.0f));

            if (_activeAxis.HasValue)
            {
                if (pointerDown)
                {
                    ApplyDrag(ray.Origin, ray.Direction, _dragMode);
                }
                else
                {
                    EndDrag();
                }

                _pointerWasDown = pointerDown;
                UpdateHighlight();
                return true;
            }

            if (inputCaptured)
            {
                SetHoveredAxis(null);
                _pointerWasDown = pointerDown;
                return false;
            }

            SetHoveredAxis(PickAxis(ray.Origin, ray.Direction));
            if (pointerDown && !_pointerWasDown && _hoveredAxis.HasValue)
            {
                BeginDrag(_hoveredAxis.Value, ray.Origin, ray.Direction, mode);
            }

            _pointerWasDown = pointerDown;
            return _hoveredAxis.HasValue || _activeAxis.HasValue;
        }

        public void Hide()
        {
            EndDrag();
            EndBoxDrag();
            SetHoveredAxis(null);
            _boxHovered = null;
            _target = null;
            DetachHandles();
        }

        private void DetachHandles()
        {
            lock (_world.SceneLock)
            {
                _world.DefaultScene.Children.Remove(_root);
            }
        }

        private void EnsureAttached()
        {
            lock (_world.SceneLock)
            {
                if (!_world.DefaultScene.Children.Contains(_root))
                {
                    _world.DefaultScene.Children.Add(_root);
                }
            }
        }

        private void EnsureBuilt(SceneTransformGizmoMode mode)
        {
            if (_builtMode == mode)
            {
                return;
            }

            _root.Children.Clear();
            _axisByNode.Clear();
            _renderers.Clear();
            _builtMode = mode;

            switch (mode)
            {
                case SceneTransformGizmoMode.Rotate:
                    BuildRotationHandles();
                    break;
                case SceneTransformGizmoMode.Scale:
                    BuildScaleHandles();
                    break;
                default:
                    BuildTranslationHandles();
                    break;
            }
        }

        private void BuildTranslationHandles()
        {
            AddBoxHandle("Move X shaft", GizmoAxis.X, new Vector3(0.46f, 0.0f, 0.0f), Vector3.Zero,
                new Vector3(0.72f, 0.055f, 0.055f), XColor);
            AddPyramidHandle("Move X arrow", GizmoAxis.X, new Vector3(0.94f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, -MathF.PI * 0.5f), XColor);

            AddBoxHandle("Move Y shaft", GizmoAxis.Y, new Vector3(0.0f, 0.46f, 0.0f), Vector3.Zero,
                new Vector3(0.055f, 0.72f, 0.055f), YColor);
            AddPyramidHandle("Move Y arrow", GizmoAxis.Y, new Vector3(0.0f, 0.94f, 0.0f), Vector3.Zero, YColor);

            AddBoxHandle("Move Z shaft", GizmoAxis.Z, new Vector3(0.0f, 0.0f, 0.46f), Vector3.Zero,
                new Vector3(0.055f, 0.055f, 0.72f), ZColor);
            AddPyramidHandle("Move Z arrow", GizmoAxis.Z, new Vector3(0.0f, 0.0f, 0.94f),
                new Vector3(MathF.PI * 0.5f, 0.0f, 0.0f), ZColor);

            // Each square moves within its own plane; its colour identifies the excluded axis.
            AddBoxHandle("Move XY plane", GizmoAxis.XY, new Vector3(0.32f, 0.32f, 0), Vector3.Zero,
                new Vector3(0.22f, 0.22f, 0.015f), ZColor);
            AddBoxHandle("Move YZ plane", GizmoAxis.YZ, new Vector3(0, 0.32f, 0.32f), Vector3.Zero,
                new Vector3(0.015f, 0.22f, 0.22f), XColor);
            AddBoxHandle("Move XZ plane", GizmoAxis.XZ, new Vector3(0.32f, 0, 0.32f), Vector3.Zero,
                new Vector3(0.22f, 0.015f, 0.22f), YColor);
        }

        private void BuildScaleHandles()
        {
            AddBoxHandle("Scale X shaft", GizmoAxis.X, new Vector3(0.46f, 0.0f, 0.0f), Vector3.Zero,
                new Vector3(0.72f, 0.045f, 0.045f), XColor);
            AddBoxHandle("Scale X grip", GizmoAxis.X, new Vector3(0.92f, 0.0f, 0.0f), Vector3.Zero,
                new Vector3(0.17f), XColor);

            AddBoxHandle("Scale Y shaft", GizmoAxis.Y, new Vector3(0.0f, 0.46f, 0.0f), Vector3.Zero,
                new Vector3(0.045f, 0.72f, 0.045f), YColor);
            AddBoxHandle("Scale Y grip", GizmoAxis.Y, new Vector3(0.0f, 0.92f, 0.0f), Vector3.Zero,
                new Vector3(0.17f), YColor);

            AddBoxHandle("Scale Z shaft", GizmoAxis.Z, new Vector3(0.0f, 0.0f, 0.46f), Vector3.Zero,
                new Vector3(0.045f, 0.045f, 0.72f), ZColor);
            AddBoxHandle("Scale Z grip", GizmoAxis.Z, new Vector3(0.0f, 0.0f, 0.92f), Vector3.Zero,
                new Vector3(0.17f), ZColor);
        }

        private void BuildRotationHandles()
        {
            AddHandle("Rotate X", GizmoAxis.X, Vector3.Zero, new Vector3(0.0f, 0.0f, MathF.PI * 0.5f),
                new ManaTorus(_world.Context, XColor, 0.78f, 0.045f));
            AddHandle("Rotate Y", GizmoAxis.Y, Vector3.Zero, Vector3.Zero,
                new ManaTorus(_world.Context, YColor, 0.78f, 0.045f));
            AddHandle("Rotate Z", GizmoAxis.Z, Vector3.Zero, new Vector3(MathF.PI * 0.5f, 0.0f, 0.0f),
                new ManaTorus(_world.Context, ZColor, 0.78f, 0.045f));
        }

        private void AddBoxHandle(
            string name,
            GizmoAxis axis,
            Vector3 position,
            Vector3 rotation,
            Vector3 size,
            Vector4 color)
        {
            AddHandle(name, axis, position, rotation, new ManaCube(_world.Context, color, size));
        }

        private void AddPyramidHandle(
            string name,
            GizmoAxis axis,
            Vector3 position,
            Vector3 rotation,
            Vector4 color)
        {
            AddHandle(name, axis, position, rotation, new ManaPyramid(_world.Context, color, new Vector3(0.24f, 0.34f, 0.24f)));
        }

        private void AddHandle(
            string name,
            GizmoAxis axis,
            Vector3 position,
            Vector3 rotation,
            ManaMesh mesh)
        {
            var node = new ManaSceneNode(_world.Context, Matrix4x4.Identity)
            {
                Name = $"{name} (Transient)",
                Position = position,
                Rotation = rotation,
                Scale = Vector3.One,
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true
            };
            var renderer = new MeshRendererComponent(node, mesh)
            {
                DrawAfterScene = true,
                AlwaysOnTop = true,
                OutlineColor = HoverColor,
                OutlineWidth = 3.0f
            };
            node.Components.Add(renderer);
            _root.AddChild(node);
            _axisByNode[node] = axis;
            _renderers.Add((axis, renderer));
        }

        private void SyncTransform(ManaSceneNode target)
        {
            var targetWorld = ComposeLocalTransform(target);
            if (target.Parent != null)
            {
                targetWorld *= target.Parent.WorldTransform;
            }

            if (!Matrix4x4.Decompose(targetWorld, out _, out _orientationWorld, out _pivotWorld))
            {
                _orientationWorld = Quaternion.Identity;
                _pivotWorld = targetWorld.Translation;
            }

            _root.Position = _pivotWorld;
            if (!_activeAxis.HasValue) _displayScale = _snap.GizmoSize / TranslationTipDistance;
            _root.Rotation = _orientationWorld.ToEuler();
            _root.Scale = new Vector3(_displayScale);
            _root.UpdateTransform();
        }

        private GizmoAxis? PickAxis(Vector3 rayOrigin, Vector3 rayDirection)
        {
            var analyticHit = _builtMode == SceneTransformGizmoMode.Rotate
                ? PickRotationAxis(rayOrigin, rayDirection)
                : PickLinearAxis(rayOrigin, rayDirection);
            if (analyticHit.HasValue)
            {
                return analyticHit;
            }

            // Handles are intentionally slender. Bounds picking keeps their whole visible silhouette easy
            // to grab instead of letting a near-edge triangle miss select the scene object behind it.
            var hit = ManaScenePicker.Pick(
                new[] { _pickScene },
                rayOrigin,
                rayDirection,
                useBoundsFallback: true);
            return hit.HasValue && _axisByNode.TryGetValue(hit.Value.Node, out var axis) ? axis : null;
        }

        private GizmoAxis? PickLinearAxis(Vector3 rayOrigin, Vector3 rayDirection)
        {
            const float handleStart = 0.12f;
            const float handleEnd = 1.12f;
            const float pickRadius = 0.13f;
            GizmoAxis? closestAxis = null;
            var closestDistance = float.PositiveInfinity;

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var axisWorld = AxisWorld(axis);
                var segmentStart = _pivotWorld + axisWorld * (_displayScale * handleStart);
                var segmentEnd = _pivotWorld + axisWorld * (_displayScale * handleEnd);
                var distance = DistanceFromRayToSegment(rayOrigin, rayDirection, segmentStart, segmentEnd);
                if (distance <= _displayScale * pickRadius && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestAxis = axis;
                }
            }

            return closestAxis;
        }

        private GizmoAxis? PickRotationAxis(Vector3 rayOrigin, Vector3 rayDirection)
        {
            const float ringRadius = 0.78f;
            const float pickWidth = 0.12f;
            GizmoAxis? closestAxis = null;
            var closestError = float.PositiveInfinity;

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var normal = AxisWorld(axis);
                var denominator = Vector3.Dot(rayDirection, normal);
                if (MathF.Abs(denominator) < 0.001f)
                {
                    continue;
                }

                var relativeRayOrigin = rayOrigin - _pivotWorld;
                var rayDistance = Vector3.Dot(-relativeRayOrigin, normal) / denominator;
                if (rayDistance < 0.0f)
                {
                    continue;
                }

                var pointFromPivot = relativeRayOrigin + rayDirection * rayDistance;
                var error = MathF.Abs(pointFromPivot.Length() - _displayScale * ringRadius);
                if (error <= _displayScale * pickWidth && error < closestError)
                {
                    closestError = error;
                    closestAxis = axis;
                }
            }

            return closestAxis;
        }

        private static float DistanceFromRayToSegment(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 segmentStart,
            Vector3 segmentEnd)
        {
            var ray = Vector3.Normalize(rayDirection);
            var segment = segmentEnd - segmentStart;
            var fromSegment = rayOrigin - segmentStart;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared < 0.000001f)
            {
                return Vector3.Distance(rayOrigin, segmentStart);
            }

            var raySegmentDot = Vector3.Dot(ray, segment);
            var rayOffsetDot = Vector3.Dot(ray, fromSegment);
            var segmentOffsetDot = Vector3.Dot(segment, fromSegment);
            var denominator = segmentLengthSquared - raySegmentDot * raySegmentDot;
            var segmentParameter = MathF.Abs(denominator) > 0.000001f
                ? (segmentOffsetDot - raySegmentDot * rayOffsetDot) / denominator
                : 0.5f;
            segmentParameter = Math.Clamp(segmentParameter, 0.0f, 1.0f);

            var rayParameter = MathF.Max(
                0.0f,
                raySegmentDot * segmentParameter - rayOffsetDot);
            segmentParameter = Math.Clamp(
                (raySegmentDot * rayParameter + segmentOffsetDot) / segmentLengthSquared,
                0.0f,
                1.0f);
            rayParameter = MathF.Max(
                0.0f,
                raySegmentDot * segmentParameter - rayOffsetDot);

            var rayPoint = rayOrigin + ray * rayParameter;
            var segmentPoint = segmentStart + segment * segmentParameter;
            return Vector3.Distance(rayPoint, segmentPoint);
        }

        private void BeginDrag(
            GizmoAxis axis,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            SceneTransformGizmoMode mode)
        {
            if (_target == null)
            {
                return;
            }

            _dragStartPosition = _target.Position;
            _dragStartRotation = _target.Rotation;
            _dragStartScale = _target.Scale;
            _dragPivotWorld = _pivotWorld;
            _dragMode = mode;
            var normalAxis = axis switch
            {
                GizmoAxis.XY => GizmoAxis.Z,
                GizmoAxis.YZ => GizmoAxis.X,
                GizmoAxis.XZ => GizmoAxis.Y,
                _ => axis
            };
            var axisWorld = AxisWorld(normalAxis);
            _dragAxisWorld = axisWorld;

            var keyboard = Keyboard;
            _dragInPlane = mode == SceneTransformGizmoMode.Translate &&
                ((int)axis >= (int)GizmoAxis.XY || keyboard?.IsKeyPressed(Key.ShiftLeft) == true || keyboard?.IsKeyPressed(Key.ShiftRight) == true);

            if (_dragInPlane)
            {
                _dragPlaneU = AxisWorld((GizmoAxis)(((int)normalAxis + 1) % 3));
                _dragPlaneV = AxisWorld((GizmoAxis)(((int)normalAxis + 2) % 3));
                if (!TryPlanePoint(rayOrigin, rayDirection, _dragPivotWorld, axisWorld, out _dragPlaneStart))
                    return;
            }
            else if (mode == SceneTransformGizmoMode.Rotate)
            {
                if (!TryPlaneVector(rayOrigin, rayDirection, _dragPivotWorld, axisWorld, out _dragStartPlaneVector))
                {
                    return;
                }
            }
            else if (!TryBuildAxisDragPlane(_dragPivotWorld, axisWorld, out _dragPlaneNormal)
                || !TryAxisParameterOnPlane(
                    rayOrigin,
                    rayDirection,
                    _dragPivotWorld,
                    axisWorld,
                    _dragPlaneNormal,
                    out _dragStartAxisParameter))
            {
                return;
            }

            _activeAxis = axis;
            UpdateHighlight();
        }

        private void ApplyDrag(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            SceneTransformGizmoMode mode)
        {
            if (_target == null || !_activeAxis.HasValue)
            {
                return;
            }

            var axis = _activeAxis.Value;
            var axisWorld = _dragAxisWorld;

            if (_dragInPlane)
            {
                if (!TryPlanePoint(rayOrigin, rayDirection, _dragPivotWorld, axisWorld, out var point))
                    return;

                var delta = point - _dragPlaneStart;
                var worldDelta = _dragPlaneU * _snap.SnapDragDistance(_dragPivotWorld, _dragPlaneU, Vector3.Dot(delta, _dragPlaneU))
                    + _dragPlaneV * _snap.SnapDragDistance(_dragPivotWorld, _dragPlaneV, Vector3.Dot(delta, _dragPlaneV));
                ApplyWorldTranslation(worldDelta);
            }
            else if (mode == SceneTransformGizmoMode.Rotate)
            {
                if (!TryPlaneVector(rayOrigin, rayDirection, _dragPivotWorld, axisWorld, out var currentVector))
                {
                    return;
                }

                var angle = MathF.Atan2(
                    Vector3.Dot(axisWorld, Vector3.Cross(_dragStartPlaneVector, currentVector)),
                    Vector3.Dot(_dragStartPlaneVector, currentVector));
                var rotation = _dragStartRotation;
                var rotated = ReadAxis(rotation, axis) + angle;
                if (_snap.Enabled)
                {
                    // Absolute, not relative to where the drag began: the result lands on a multiple of the
                    // step whatever angle the object started at.
                    rotated = float.DegreesToRadians(_snap.SnapRotationDegrees(float.RadiansToDegrees(rotated)));
                }

                SetAxis(ref rotation, axis, rotated);
                _target.Rotation = rotation;
            }
            else
            {
                if (!TryAxisParameterOnPlane(
                    rayOrigin,
                    rayDirection,
                    _dragPivotWorld,
                    axisWorld,
                    _dragPlaneNormal,
                    out var parameter))
                {
                    return;
                }

                var delta = parameter - _dragStartAxisParameter;
                if (mode == SceneTransformGizmoMode.Scale)
                {
                    var scale = _dragStartScale;
                    var value = ReadAxis(scale, axis) + delta / MathF.Max(_displayScale, 0.001f);
                    SetAxis(ref scale, axis, MathF.Max(0.01f, _snap.SnapScale(value)));
                    _target.Scale = scale;
                }
                else
                {
                    var worldDelta = axisWorld * _snap.SnapDragDistance(_dragPivotWorld, axisWorld, delta);
                    ApplyWorldTranslation(worldDelta);
                }
            }

            ApplyCurrentTransform(_target);
            SyncTransform(_target);
        }

        private void EndDrag()
        {
            if (_activeAxis.HasValue && _target != null)
            {
                var before = new SceneNodeTransformState(
                    _dragStartPosition,
                    _dragStartRotation,
                    _dragStartScale);
                var after = SceneNodeTransformState.Capture(_target);
                _history.RecordTransform(
                    _target,
                    before,
                    after,
                    $"{DescribeMode(_dragMode)} {_target.Name}",
                    _world.Selection);
            }

            _activeAxis = null;
            _dragInPlane = false;
            UpdateHighlight();
        }

        private void ApplyWorldTranslation(Vector3 worldDelta)
        {
            if (_target == null) return;
            if (_target.Parent != null)
            {
                if (!Matrix4x4.Invert(_target.Parent.WorldTransform, out var worldToParent)) return;
                worldDelta = Vector3.TransformNormal(worldDelta, worldToParent);
            }
            _target.Position = _dragStartPosition + worldDelta;
        }

        private static bool TryPlanePoint(Vector3 rayOrigin, Vector3 rayDirection,
            Vector3 origin, Vector3 normal, out Vector3 point)
        {
            point = Vector3.Zero;
            var denominator = Vector3.Dot(rayDirection, normal);
            if (MathF.Abs(denominator) < 0.001f) return false;
            var distance = Vector3.Dot(origin - rayOrigin, normal) / denominator;
            if (!float.IsFinite(distance) || distance < 0) return false;
            // Relative to the pivot to retain precision far from the world origin.
            point = rayOrigin - origin + rayDirection * distance;
            return float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
        }

        private static string DescribeMode(SceneTransformGizmoMode mode) => mode switch
        {
            SceneTransformGizmoMode.Rotate => "Rotate",
            SceneTransformGizmoMode.Scale => "Scale",
            _ => "Move"
        };

        private void SetHoveredAxis(GizmoAxis? axis)
        {
            if (_hoveredAxis == axis)
            {
                return;
            }

            _hoveredAxis = axis;
            UpdateHighlight();
        }

        private void UpdateHighlight()
        {
            var highlighted = _activeAxis ?? _hoveredAxis;
            foreach (var (axis, renderer) in _renderers)
            {
                renderer.OutlineEnabled = highlighted.HasValue && highlighted.Value == axis;
            }
        }

        private Vector3 AxisWorld(GizmoAxis axis)
        {
            var local = axis switch
            {
                GizmoAxis.X => Vector3.UnitX,
                GizmoAxis.Y => Vector3.UnitY,
                _ => Vector3.UnitZ
            };
            return Vector3.Normalize(Vector3.Transform(local, _orientationWorld));
        }

        private bool TryBuildAxisDragPlane(Vector3 origin, Vector3 axisDirection, out Vector3 planeNormal)
        {
            var axis = Vector3.Normalize(axisDirection);
            var cameraPosition = _world.MainCamera.Owner.WorldTransform.Translation;

            // The plane contains the drag axis and faces the camera as squarely as that constraint allows.
            // Intersecting both mouse rays with this one fixed plane makes a handle follow the pointer at
            // scene scale; the old closest-lines denominator approached zero and launched nodes away.
            var candidates = new[]
            {
                cameraPosition - origin,
                _world.MainCamera.Owner.Up,
                _world.MainCamera.Owner.Right
            };

            foreach (var candidate in candidates)
            {
                var rejected = candidate - axis * Vector3.Dot(candidate, axis);
                if (rejected.LengthSquared() > 0.0001f)
                {
                    planeNormal = Vector3.Normalize(rejected);
                    return true;
                }
            }

            planeNormal = Vector3.Zero;
            return false;
        }

        private static bool TryAxisParameterOnPlane(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 axisOrigin,
            Vector3 axisDirection,
            Vector3 planeNormal,
            out float parameter)
        {
            var denominator = Vector3.Dot(rayDirection, planeNormal);
            if (MathF.Abs(denominator) < 0.001f)
            {
                parameter = 0.0f;
                return false;
            }

            // Stay relative to the drag origin. Reconstructing a large world-space point and then
            // subtracting the pivot loses low bits as the scene moves farther from world origin.
            var relativeRayOrigin = rayOrigin - axisOrigin;
            var distance = Vector3.Dot(-relativeRayOrigin, planeNormal) / denominator;
            if (distance < 0.0f || !float.IsFinite(distance))
            {
                parameter = 0.0f;
                return false;
            }

            var pointFromAxisOrigin = relativeRayOrigin + rayDirection * distance;
            parameter = Vector3.Dot(pointFromAxisOrigin, Vector3.Normalize(axisDirection));
            return float.IsFinite(parameter);
        }

        private static bool TryPlaneVector(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 planeOrigin,
            Vector3 planeNormal,
            out Vector3 vector)
        {
            var denominator = Vector3.Dot(rayDirection, planeNormal);
            if (MathF.Abs(denominator) < 0.0001f)
            {
                vector = Vector3.Zero;
                return false;
            }

            var relativeRayOrigin = rayOrigin - planeOrigin;
            var distance = Vector3.Dot(-relativeRayOrigin, planeNormal) / denominator;
            if (distance < 0.0f)
            {
                vector = Vector3.Zero;
                return false;
            }

            vector = relativeRayOrigin + rayDirection * distance;
            if (vector.LengthSquared() < 0.000001f)
            {
                return false;
            }

            vector = Vector3.Normalize(vector);
            return true;
        }

        private static float ReadAxis(Vector3 value, GizmoAxis axis)
        {
            return axis switch
            {
                GizmoAxis.X => value.X,
                GizmoAxis.Y => value.Y,
                _ => value.Z
            };
        }

        private static void SetAxis(ref Vector3 value, GizmoAxis axis, float component)
        {
            switch (axis)
            {
                case GizmoAxis.X:
                    value.X = component;
                    break;
                case GizmoAxis.Y:
                    value.Y = component;
                    break;
                default:
                    value.Z = component;
                    break;
            }
        }

        private IKeyboard? Keyboard => _world.Context.Input.Keyboards.FirstOrDefault();

        private bool IsHeld(Key left, Key right) => Keyboard is { } keyboard && (keyboard.IsKeyPressed(left) || keyboard.IsKeyPressed(right));

        /// <summary>False when the selection has no box, so Scale falls back to its axis handles.</summary>
        private bool UpdateScaleBox(ManaSceneNode target, bool inputCaptured, IMouse mouse, bool pointerDown, out bool captured)
        {
            captured = false;
            if (!_boxActive.HasValue && !SceneScaleBox.TryLocalBounds(target, out _boxMin, out _boxMax))
            {
                return false;
            }

            DetachHandles();
            SetHoveredAxis(null);
            var ray = _world.MainCamera.ProjectScreenToWorld(new Vector3(mouse.Position, 0.0f), new Vector3(mouse.Position, 1.0f));
            if (_boxActive.HasValue)
            {
                if (pointerDown)
                {
                    ApplyBoxDrag(target, ray.Origin, ray.Direction);
                }
                else
                {
                    EndBoxDrag();
                }

                captured = true;
                return true;
            }

            _boxHovered = inputCaptured ? null : PickBoxHandle(target, ray.Origin);
            if (pointerDown && !_pointerWasDown && _boxHovered is { } handle)
            {
                BeginBoxDrag(target, handle, ray.Origin, ray.Direction);
            }

            captured = _boxHovered.HasValue || _boxActive.HasValue;
            return true;
        }

        // Picked on screen, in window pixels: an edge anywhere along its length, a corner or face by its dot.
        private SceneScaleBoxHandle? PickBoxHandle(ManaSceneNode target, Vector3 pointerNearPoint)
        {
            const float dotRadius = 10.0f;
            const float edgeRadius = 7.0f;
            var camera = _world.MainCamera;
            var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
            var halfWindow = new Vector2(_world.Context.Window.Size.X, _world.Context.Window.Size.Y) * 0.5f;
            bool Screen(Vector3 world, out Vector2 screen)
            {
                var clip = Vector4.Transform(new Vector4(world, 1.0f), viewProjection);
                screen = clip.W > 1e-5f ? new Vector2(clip.X / clip.W * halfWindow.X, -clip.Y / clip.W * halfWindow.Y) : Vector2.Zero;
                return clip.W > 1e-5f;
            }

            // The pointer ray starts on the near plane, so projecting its start gives the pointer in the same terms.
            if (!Screen(pointerNearPoint, out var pointer))
            {
                return null;
            }

            var world = target.WorldTransform;
            SceneScaleBoxHandle? best = null;
            var bestScore = float.PositiveInfinity;
            foreach (var handle in SceneScaleBox.Handles(_boxMin, _boxMax))
            {
                float distance;
                float radius;
                if (SceneScaleBox.TryEdge(handle, _boxMin, _boxMax, out var start, out var end))
                {
                    if (!Screen(Vector3.Transform(start, world), out var a) || !Screen(Vector3.Transform(end, world), out var b))
                    {
                        continue;
                    }

                    var segment = b - a;
                    var t = segment.LengthSquared() > 1e-6f ? Math.Clamp(Vector2.Dot(pointer - a, segment) / segment.LengthSquared(), 0.0f, 1.0f) : 0.0f;
                    distance = Vector2.Distance(pointer, a + segment * t);
                    radius = edgeRadius;
                }
                else
                {
                    if (!Screen(Vector3.Transform(SceneScaleBox.Point(handle, _boxMin, _boxMax), world), out var dot))
                    {
                        continue;
                    }

                    distance = Vector2.Distance(pointer, dot);
                    radius = dotRadius;
                }

                // A corner sits where two edges meet; it wins there.
                var score = distance - (handle.AxisCount > 1 ? dotRadius : 0.0f);
                if (distance <= radius && score < bestScore)
                {
                    bestScore = score;
                    best = handle;
                }
            }

            return best;
        }

        private void BeginBoxDrag(ManaSceneNode target, SceneScaleBoxHandle handle, Vector3 rayOrigin, Vector3 rayDirection)
        {
            _boxStartWorld = ComposeLocalTransform(target) * (target.Parent?.WorldTransform ?? Matrix4x4.Identity);
            if (!Matrix4x4.Invert(_boxStartWorld, out _boxWorldToLocal))
            {
                return;
            }

            _boxHandleWorld = Vector3.Transform(SceneScaleBox.Point(handle, _boxMin, _boxMax), _boxStartWorld);
            var flatAxis = -1;
            for (var axis = 0; axis < 3 && SceneScaleBox.SolidAxes(_boxMin, _boxMax) == 2; axis++)
            {
                if (SceneScaleBox.IsFlat(_boxMin, _boxMax, axis))
                {
                    flatAxis = axis;
                }
            }

            // A flat box is dragged across its own plane; a solid one's face along its axis, a corner across the view.
            if (flatAxis >= 0)
            {
                _boxPlaneNormal = Vector3.Normalize(LocalAxisWorld(flatAxis));
            }
            else if (handle.AxisCount == 1)
            {
                var axis = handle.X != 0 ? 0 : handle.Y != 0 ? 1 : 2;
                if (!TryBuildAxisDragPlane(_boxHandleWorld, Vector3.Normalize(LocalAxisWorld(axis)), out _boxPlaneNormal))
                {
                    return;
                }
            }
            else
            {
                _boxPlaneNormal = Vector3.Normalize(_world.MainCamera.Owner.WorldTransform.Translation - _boxHandleWorld);
            }

            if (!TryPlanePoint(rayOrigin, rayDirection, _boxHandleWorld, _boxPlaneNormal, out var hit))
            {
                return;
            }

            _boxStartHitLocal = Vector3.Transform(_boxHandleWorld + hit, _boxWorldToLocal);
            _dragStartPosition = target.Position;
            _dragStartRotation = target.Rotation;
            _dragStartScale = target.Scale;
            _dragMode = SceneTransformGizmoMode.Scale;
            _boxActive = handle;
        }

        private Vector3 LocalAxisWorld(int axis) =>
            Vector3.TransformNormal(axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ, _boxStartWorld);

        /// <summary>
        /// Moves the grabbed side and keeps the opposite one where it was (Alt: the middle stays, both sides move; Shift:
        /// the proportions stay). A 9-slice steps a whole tile at a time, the rest snap their edge to the grid.
        /// </summary>
        private void ApplyBoxDrag(ManaSceneNode target, Vector3 rayOrigin, Vector3 rayDirection)
        {
            if (_boxActive is not { } handle ||
                !TryPlanePoint(rayOrigin, rayDirection, _boxHandleWorld, _boxPlaneNormal, out var hit))
            {
                return;
            }

            var handleLocal = SceneScaleBox.Point(handle, _boxMin, _boxMax);
            var moved = handleLocal + Vector3.Transform(_boxHandleWorld + hit, _boxWorldToLocal) - _boxStartHitLocal;
            var fromMiddle = IsHeld(Key.AltLeft, Key.AltRight);
            var centre = (_boxMin + _boxMax) * 0.5f;
            var anchor = Vector3.Zero;
            var lengths = Vector3.Zero;
            for (var axis = 0; axis < 3; axis++)
            {
                var side = handle[axis];
                anchor = SceneScaleBox.Set(anchor, axis, fromMiddle || side == 0 ? SceneScaleBox.Get(centre, axis)
                    : side > 0 ? SceneScaleBox.Get(_boxMin, axis) : SceneScaleBox.Get(_boxMax, axis));
                lengths = SceneScaleBox.Set(lengths, axis, LocalAxisWorld(axis).Length());
            }

            var factors = Vector3.One;
            // A solid box's corner scales evenly -- three free axes would follow the pointer nowhere useful.
            if (handle.AxisCount > 1 && (IsHeld(Key.ShiftLeft, Key.ShiftRight) || handle.AxisCount == 3))
            {
                var startSpan = (handleLocal - anchor) * lengths;
                var factor = Vector3.Dot((moved - anchor) * lengths, startSpan) / MathF.Max(startSpan.LengthSquared(), 1e-12f);
                if (_snap.Enabled)
                {
                    factor = EditorSnapSettings.SnapTo(factor, _snap.ScaleStep);
                }

                factors = new Vector3(handle.X != 0 ? factor : 1.0f, handle.Y != 0 ? factor : 1.0f, handle.Z != 0 ? factor : 1.0f);
            }
            else
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    if (handle[axis] == 0)
                    {
                        continue;
                    }

                    var length = MathF.Max(SceneScaleBox.Get(lengths, axis), 1e-12f);
                    var delta = (SceneScaleBox.Get(moved, axis) - SceneScaleBox.Get(handleLocal, axis)) * length;
                    // The edge itself lands on the grid, as a move handle's pivot does.
                    delta = _snap.SnapDragDistance(_boxHandleWorld, Vector3.Normalize(LocalAxisWorld(axis)), delta);
                    var edge = SceneScaleBox.Get(handleLocal, axis) + delta / length;
                    var span = SceneScaleBox.Get(handleLocal, axis) - SceneScaleBox.Get(anchor, axis);
                    factors = SceneScaleBox.Set(factors, axis, MathF.Abs(span) > 1e-12f ? (edge - SceneScaleBox.Get(anchor, axis)) / span : 1.0f);
                }
            }

            var startScale = _dragStartScale;
            var scale = startScale;
            for (var axis = 0; axis < 3; axis++)
            {
                var start = SceneScaleBox.Get(startScale, axis);
                // Never through zero: a side dragged past its anchor stops there rather than flipping the object.
                var factor = MathF.Max(SceneScaleBox.Get(factors, axis), 0.01f / MathF.Max(MathF.Abs(start), 1e-6f));
                scale = SceneScaleBox.Set(scale, axis, start * factor);
            }

            var newAnchor = anchor;
            if (target.Components.OfType<ManaSprite9SliceComponent>().FirstOrDefault() is { TileSize: { } tile } slice)
            {
                // Scaled so the block is exactly whole tiles, its quad is exactly Size about its pivot again.
                for (var axis = 0; axis < 2; axis++)
                {
                    var size = axis == 0 ? slice.Size.X : slice.Size.Y;
                    var tileSize = axis == 0 ? tile.X : tile.Y;
                    var pivot = axis == 0 ? slice.Pivot.X : slice.Pivot.Y;
                    if (handle[axis] == 0 || size <= 0.0f || tileSize <= 0.0f)
                    {
                        continue;
                    }

                    var start = SceneScaleBox.Get(startScale, axis);
                    var tiles = MathF.Max(1.0f, MathF.Round(MathF.Abs(size * SceneScaleBox.Get(scale, axis)) / tileSize));
                    scale = SceneScaleBox.Set(scale, axis, MathF.CopySign(tiles * tileSize / size, start));
                    var low = -pivot * size;
                    var high = (1.0f - pivot) * size;
                    newAnchor = SceneScaleBox.Set(newAnchor, axis, fromMiddle ? (low + high) * 0.5f : handle[axis] > 0 ? low : high);
                }
            }

            var rotation = _dragStartRotation.ToQuaternionFromEuler();
            target.Scale = scale;
            target.Position = _dragStartPosition + Vector3.Transform(startScale * anchor - scale * newAnchor, rotation);
            ApplyCurrentTransform(target);
        }

        /// <summary>For scripts: drags a box handle from where it is to a world point, as the pointer would from the camera.</summary>
        public bool DragBoxHandle(ManaSceneNode target, SceneScaleBoxHandle handle, Vector3 to)
        {
            Hide();
            if (!SceneScaleBox.TryLocalBounds(target, out _boxMin, out _boxMax) ||
                !SceneScaleBox.Handles(_boxMin, _boxMax).Contains(handle))
            {
                return false;
            }

            _target = target;
            var eye = _world.MainCamera.Owner.WorldTransform.Translation;
            var from = Vector3.Transform(SceneScaleBox.Point(handle, _boxMin, _boxMax), target.WorldTransform);
            BeginBoxDrag(target, handle, eye, Vector3.Normalize(from - eye));
            var dragged = _boxActive.HasValue;
            ApplyBoxDrag(target, eye, Vector3.Normalize(to - eye));
            EndBoxDrag();
            _target = null;
            return dragged;
        }

        private void EndBoxDrag()
        {
            if (_boxActive.HasValue && _target != null)
            {
                _history.RecordTransform(
                    _target,
                    new SceneNodeTransformState(_dragStartPosition, _dragStartRotation, _dragStartScale),
                    SceneNodeTransformState.Capture(_target),
                    $"Scale {_target.Name}",
                    _world.Selection);
            }

            _boxActive = null;
        }

        private static Matrix4x4 ComposeLocalTransform(ManaSceneNode node)
        {
            return Matrix4x4.CreateScale(node.Scale)
                * Matrix4x4.CreateFromQuaternion(node.Rotation.ToQuaternionFromEuler())
                * Matrix4x4.CreateTranslation(node.Position);
        }

        private static void ApplyCurrentTransform(ManaSceneNode node)
        {
            var local = ComposeLocalTransform(node);
            node.LocalTransform = local;
            node.WorldTransform = node.Parent != null ? local * node.Parent.WorldTransform : local;
        }
    }
}
