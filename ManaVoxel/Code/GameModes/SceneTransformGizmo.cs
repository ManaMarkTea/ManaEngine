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
        private static readonly Vector4 XColor = new(0.92f, 0.18f, 0.16f, 1.0f);
        private static readonly Vector4 YColor = new(0.22f, 0.82f, 0.28f, 1.0f);
        private static readonly Vector4 ZColor = new(0.18f, 0.46f, 0.96f, 1.0f);
        private static readonly Vector4 HoverColor = new(1.0f, 0.88f, 0.20f, 1.0f);

        private readonly ManaWorld _world;
        private readonly ManaSceneNode _root;
        private readonly ManaScene _pickScene;
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
        private float _dragStartAxisParameter;
        private Vector3 _dragStartPlaneVector;
        private Vector3 _dragStartPosition;
        private Vector3 _dragStartRotation;
        private Vector3 _dragStartScale;

        private enum GizmoAxis
        {
            X,
            Y,
            Z
        }

        public SceneTransformGizmo(ManaWorld world)
        {
            _world = world;
            _root = new ManaSceneNode(world.Context, Matrix4x4.Identity)
            {
                Name = "Transform Gizmo (Transient)",
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true
            };
            _pickScene = new ManaScene(world.Context);
            _pickScene.Children.Add(_root);
        }

        /// <summary>Returns true while a handle owns or is under the pointer.</summary>
        public bool Update(
            ManaSceneNode? selectedNode,
            SceneTransformGizmoMode mode,
            bool visible,
            bool inputCaptured,
            IMouse mouse)
        {
            var pointerDown = mouse.IsButtonPressed(MouseButton.Left);

            if (!visible || selectedNode == null || selectedNode.IsEditorTransient)
            {
                Hide();
                _pointerWasDown = pointerDown;
                return false;
            }

            if (!ReferenceEquals(_target, selectedNode))
            {
                EndDrag();
                _target = selectedNode;
            }

            EnsureBuilt(mode);
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
                    ApplyDrag(ray.Origin, ray.Direction, mode);
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
            SetHoveredAxis(null);
            _target = null;

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

            var camera = _world.MainCamera;
            _displayScale = camera.OrthoGraphic
                ? MathF.Max(0.05f, camera.OrthographicHeight * 0.11f)
                : Math.Clamp(Vector3.Distance(camera.Owner.WorldTransform.Translation, _pivotWorld) * 0.11f, 0.05f, 100.0f);

            _root.Position = _pivotWorld;
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

            foreach (var axis in Enum.GetValues<GizmoAxis>())
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

            foreach (var axis in Enum.GetValues<GizmoAxis>())
            {
                var normal = AxisWorld(axis);
                var denominator = Vector3.Dot(rayDirection, normal);
                if (MathF.Abs(denominator) < 0.001f)
                {
                    continue;
                }

                var rayDistance = Vector3.Dot(_pivotWorld - rayOrigin, normal) / denominator;
                if (rayDistance < 0.0f)
                {
                    continue;
                }

                var point = rayOrigin + rayDirection * rayDistance;
                var error = MathF.Abs(Vector3.Distance(point, _pivotWorld) - _displayScale * ringRadius);
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
            var axisWorld = AxisWorld(axis);
            _dragAxisWorld = axisWorld;

            if (mode == SceneTransformGizmoMode.Rotate)
            {
                if (!TryPlaneVector(rayOrigin, rayDirection, _pivotWorld, axisWorld, out _dragStartPlaneVector))
                {
                    return;
                }
            }
            else if (!TryBuildAxisDragPlane(axisWorld, out _dragPlaneNormal)
                || !TryAxisParameterOnPlane(
                    rayOrigin,
                    rayDirection,
                    _pivotWorld,
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

            if (mode == SceneTransformGizmoMode.Rotate)
            {
                if (!TryPlaneVector(rayOrigin, rayDirection, _pivotWorld, axisWorld, out var currentVector))
                {
                    return;
                }

                var angle = MathF.Atan2(
                    Vector3.Dot(axisWorld, Vector3.Cross(_dragStartPlaneVector, currentVector)),
                    Vector3.Dot(_dragStartPlaneVector, currentVector));
                var rotation = _dragStartRotation;
                SetAxis(ref rotation, axis, ReadAxis(rotation, axis) + angle);
                _target.Rotation = rotation;
            }
            else
            {
                if (!TryAxisParameterOnPlane(
                    rayOrigin,
                    rayDirection,
                    _pivotWorld,
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
                    SetAxis(ref scale, axis, MathF.Max(0.01f, value));
                    _target.Scale = scale;
                }
                else
                {
                    var worldDelta = axisWorld * delta;
                    var localDelta = worldDelta;
                    if (_target.Parent != null && Matrix4x4.Invert(_target.Parent.WorldTransform, out var worldToParent))
                    {
                        localDelta = Vector3.TransformNormal(worldDelta, worldToParent);
                    }

                    _target.Position = _dragStartPosition + localDelta;
                }
            }

            ApplyCurrentTransform(_target);
            SyncTransform(_target);
        }

        private void EndDrag()
        {
            _activeAxis = null;
            UpdateHighlight();
        }

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

        private bool TryBuildAxisDragPlane(Vector3 axisDirection, out Vector3 planeNormal)
        {
            var axis = Vector3.Normalize(axisDirection);
            var cameraPosition = _world.MainCamera.Owner.WorldTransform.Translation;

            // The plane contains the drag axis and faces the camera as squarely as that constraint allows.
            // Intersecting both mouse rays with this one fixed plane makes a handle follow the pointer at
            // scene scale; the old closest-lines denominator approached zero and launched nodes away.
            var candidates = new[]
            {
                cameraPosition - _pivotWorld,
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

            var distance = Vector3.Dot(axisOrigin - rayOrigin, planeNormal) / denominator;
            if (distance < 0.0f || !float.IsFinite(distance))
            {
                parameter = 0.0f;
                return false;
            }

            var point = rayOrigin + rayDirection * distance;
            parameter = Vector3.Dot(point - axisOrigin, Vector3.Normalize(axisDirection));
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

            var distance = Vector3.Dot(planeOrigin - rayOrigin, planeNormal) / denominator;
            if (distance < 0.0f)
            {
                vector = Vector3.Zero;
                return false;
            }

            vector = rayOrigin + rayDirection * distance - planeOrigin;
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
