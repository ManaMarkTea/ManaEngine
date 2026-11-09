using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaEditor.Code.UI;
using Silk.NET.Input;
using System.Runtime.CompilerServices;

namespace Mana.Code.GameModes
{
    // Editing a PolygonCollider's outline in the viewport: while its node is selected, each point has a handle.
    // Drag one to move it, Ctrl+click an edge to add a point there, Alt+click a point to remove it.
    internal sealed class PolygonEditingController(ManaContext context, VoxelEditorViewModel viewModel, HashSet<ManaSceneNode> pickingExclusions)
    {
        private static readonly Vector4 PolygonHandleColor = new(0.3f, 0.9f, 1.0f, 1.0f);

        private ManaSceneNode? _polygonOverlay;
        private string? _polygonOverlayKey;
        private ManaPolygonColliderComponent? _polygonDragTarget;
        private int _polygonDragIndex = -1;
        private float[]? _polygonDragBefore;
        private bool _polygonLeftWasDown;

        /// <summary>True while the pointer is working a handle, so selection and the gizmo leave the click alone.</summary>
        public bool UpdatePolygonEditing(bool inputCaptured, IMouse mouse, bool isPlaying)
        {
            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            var pressed = leftDown && !_polygonLeftWasDown;
            _polygonLeftWasDown = leftDown;

            var collider = !isPlaying && ManaEditorGizmos.Visible
                ? context.World.Selection.SelectedNode?.GetComponent<ManaPolygonColliderComponent>()
                : null;
            if (collider == null)
            {
                EndPolygonDrag();
                HidePolygonOverlay();
                return false;
            }

            var radius = PolygonHandleRadius(collider.Owner);
            var at = inputCaptured && _polygonDragTarget == null ? null : PolygonPlanePoint(collider.Owner, mouse);
            var points = collider.Points;
            var hovered = at is { } over ? NearestPolygonPoint(points, over, radius) : -1;
            var captured = false;

            if (_polygonDragTarget != null)
            {
                captured = true;
                if (!leftDown)
                {
                    EndPolygonDrag();
                }
                else if (at is { } to && _polygonDragIndex * 2 + 1 < points.Length)
                {
                    var moved = (float[])points.Clone();
                    moved[_polygonDragIndex * 2] = SnapPolygonCoordinate(to.X);
                    moved[_polygonDragIndex * 2 + 1] = SnapPolygonCoordinate(to.Y);
                    collider.Points = moved;
                    hovered = _polygonDragIndex;
                }
            }
            else if (pressed && at is { } click)
            {
                var keyboard = context.Input.Keyboards.FirstOrDefault();
                var control = keyboard != null && (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
                var alt = keyboard != null && (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight));

                if (hovered >= 0 && alt)
                {
                    // Three points is the least that still encloses anything.
                    if (points.Length > 6)
                    {
                        var before = points;
                        var after = points.Where((_, i) => i / 2 != hovered).ToArray();
                        collider.Points = after;
                        viewModel.History.Record("Remove polygon point", () => collider.Points = before, () => collider.Points = after);
                        hovered = -1;
                    }
                    captured = true;
                }
                else if (hovered >= 0)
                {
                    BeginPolygonDrag(collider, hovered);
                    captured = true;
                }
                else if (control && InsertPolygonPoint(points, click, radius * 3.0f) is { } inserted)
                {
                    BeginPolygonDrag(collider, inserted.Index);
                    _polygonDragBefore = points;
                    collider.Points = inserted.Points;
                    hovered = inserted.Index;
                    captured = true;
                }
            }

            ShowPolygonOverlay(collider, hovered, radius);
            return captured;
        }

        private void BeginPolygonDrag(ManaPolygonColliderComponent collider, int index)
        {
            _polygonDragTarget = collider;
            _polygonDragIndex = index;
            _polygonDragBefore = collider.Points;
        }

        private void EndPolygonDrag()
        {
            var collider = _polygonDragTarget;
            var before = _polygonDragBefore;
            _polygonDragTarget = null;
            _polygonDragIndex = -1;
            _polygonDragBefore = null;
            if (collider == null || before == null || before.SequenceEqual(collider.Points))
            {
                return;
            }

            var after = collider.Points;
            viewModel.History.Record("Edit polygon", () => collider.Points = before, () => collider.Points = after);
        }

        private float SnapPolygonCoordinate(float value)
        {
            var snap = viewModel.Snap;
            return snap.Enabled ? EditorSnapSettings.SnapTo(value, snap.TranslateStep) : MathF.Round(value * 100.0f) / 100.0f;
        }

        /// <summary>Where the pointer meets the collider's own plane (its local z = 0), in its local x, y.</summary>
        private Vector2? PolygonPlanePoint(ManaSceneNode node, IMouse mouse)
        {
            var camera = context.World.MainCamera;
            if (camera == null || !Matrix4x4.Invert(node.WorldTransform, out var worldToLocal))
            {
                return null;
            }

            var ray = camera.ProjectScreenToWorld(new Vector3(mouse.Position, 0.0f), new Vector3(mouse.Position, 1.0f));
            var origin = Vector3.Transform(ray.Origin, worldToLocal);
            var direction = Vector3.TransformNormal(ray.Direction, worldToLocal);
            if (MathF.Abs(direction.Z) < 1e-5f)
            {
                return null;
            }

            var travel = -origin.Z / direction.Z;
            if (travel <= 0.0f)
            {
                return null;
            }

            var point = origin + direction * travel;
            return new Vector2(point.X, point.Y);
        }

        /// <summary>About a hundredth of the view's height at the node, in its local units, so handles stay a steady size on screen.</summary>
        private float PolygonHandleRadius(ManaSceneNode node)
        {
            var camera = context.World.MainCamera;
            var worldScale = MathF.Max(new Vector3(node.WorldTransform.M11, node.WorldTransform.M12, node.WorldTransform.M13).Length(), 1e-4f);
            if (camera == null)
            {
                return 0.1f / worldScale;
            }

            var viewHeight = camera.OrthoGraphic
                ? camera.OrthographicHeight
                : 2.0f * Vector3.Distance(camera.Owner.WorldTransform.Translation, node.WorldTransform.Translation)
                    * MathF.Tan(camera.EffectiveFieldOfViewDegrees * MathF.PI / 360.0f);
            return MathF.Max(viewHeight * 0.012f, 0.01f) / worldScale;
        }

        public static int NearestPolygonPoint(float[] points, Vector2 at, float radius)
        {
            var best = -1;
            var bestDistance = radius;
            for (var i = 0; i + 1 < points.Length; i += 2)
            {
                var distance = Vector2.Distance(new Vector2(points[i], points[i + 1]), at);
                if (distance <= bestDistance)
                {
                    best = i / 2;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// The outline with a point added where <paramref name="at"/> is nearest an edge -- if it is within
        /// <paramref name="reach"/> of one -- and the new point's index.
        /// </summary>
        public static (float[] Points, int Index)? InsertPolygonPoint(float[] points, Vector2 at, float reach)
        {
            var count = points.Length / 2;
            var bestEdge = -1;
            var bestDistance = reach;
            var bestPoint = at;
            for (var i = 0; i < count; i++)
            {
                var a = new Vector2(points[i * 2], points[i * 2 + 1]);
                var j = (i + 1) % count;
                var b = new Vector2(points[j * 2], points[j * 2 + 1]);
                var along = b - a;
                var t = along.LengthSquared() < 1e-8f ? 0.0f : Math.Clamp(Vector2.Dot(at - a, along) / along.LengthSquared(), 0.0f, 1.0f);
                var onEdge = a + along * t;
                var distance = Vector2.Distance(onEdge, at);
                if (distance <= bestDistance)
                {
                    bestEdge = i;
                    bestDistance = distance;
                    bestPoint = onEdge;
                }
            }

            if (bestEdge < 0)
            {
                return null;
            }

            var index = bestEdge + 1;
            var result = new List<float>(points);
            result.InsertRange(index * 2, new[] { bestPoint.X, bestPoint.Y });
            return (result.ToArray(), index);
        }

        private void ShowPolygonOverlay(ManaPolygonColliderComponent collider, int hovered, float radius)
        {
            var points = collider.Points;
            // Rebuilt only when something it shows changed; the size in steps of 5%, so zooming does not rebuild every frame.
            var key = $"{RuntimeHelpers.GetHashCode(collider)}|{string.Join(',', points)}|{hovered}|{(int)MathF.Round(MathF.Log(radius) * 20.0f)}|{collider.Owner.WorldTransform}";
            if (_polygonOverlay != null && _polygonOverlayKey == key)
            {
                return;
            }

            HidePolygonOverlay();
            if (points.Length < 6)
            {
                return;
            }

            var corners = new List<Vector3>();
            var lines = new List<ManaLinePolyline>();
            for (var i = 0; i + 1 < points.Length; i += 2)
            {
                var p = new Vector3(points[i], points[i + 1], 0.0f);
                corners.Add(p);
                // The one under the pointer (or being dragged) is drawn larger, so it is clear what a click takes.
                var half = radius * (i / 2 == hovered ? 0.9f : 0.55f);
                lines.Add(new ManaLinePolyline(
                    new[] { p + new Vector3(-half, -half, 0), p + new Vector3(half, -half, 0), p + new Vector3(half, half, 0), p + new Vector3(-half, half, 0) },
                    closed: true));
            }
            lines.Add(new ManaLinePolyline(corners, closed: true));

            var mesh = new ManaLineMesh(context, lines, thickness: radius * 0.22f, PolygonHandleColor, emissionStrength: 0.8f);
            var node = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = "Polygon Points",
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true
            };
            node.Components.Add(new MeshRendererComponent(node, mesh) { DrawAfterScene = true, AlwaysOnTop = true });
            lock (context.World.SceneLock)
            {
                context.World.DefaultScene.Children.Add(node);
            }

            node.SetLocalTransform(collider.Owner.WorldTransform);
            pickingExclusions.Add(node);
            _polygonOverlay = node;
            _polygonOverlayKey = key;
        }

        public void HidePolygonOverlay()
        {
            _polygonOverlayKey = null;
            if (_polygonOverlay == null)
            {
                return;
            }

            lock (context.World.SceneLock)
            {
                context.World.DefaultScene.Children.Remove(_polygonOverlay);
            }

            pickingExclusions.Remove(_polygonOverlay);
            _polygonOverlay = null;
        }
    }
}
