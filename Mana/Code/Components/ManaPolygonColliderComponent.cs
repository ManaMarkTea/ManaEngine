using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// A 2D collider: an outline in the node's XY plane, pushed out <see cref="Depth"/> along Z into a solid the
    /// physics can stand on. Any simple outline works, concave ones included -- it is cut into triangles, not
    /// wrapped in a hull. <see cref="Points"/> is x, y pairs in node units, in order round the outline.
    /// </summary>
    public sealed class ManaPolygonColliderComponent : ManaComponent
    {
        /// <summary>The outline as x, y pairs: [x0, y0, x1, y1, ...], in order round it.</summary>
        public float[] Points { get; set; } = [-0.5f, 0.0f, 0.5f, 0.0f, 0.5f, 1.0f, -0.5f, 1.0f];

        /// <summary>How thick the solid is along Z, centred on the node.</summary>
        public float Depth { get; set; } = 1.0f;

        private ManaCollisionComponent? _body;
        private string? _builtKey;

        public ManaPolygonColliderComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Polygon Collider";
        }

        /// <summary>The body in use, once built.</summary>
        public ManaCollisionComponent? Body => _body;

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            Rebuild();
        }

        /// <summary>Builds the body, or rebuilds it when the outline, depth or node scale changed.</summary>
        public void Rebuild()
        {
            var physics = Owner.Context.Physics;
            if (physics == null)
            {
                return;
            }

            var scale = Owner.Scale;
            var key = $"{string.Join(",", Points)}|{Depth}|{scale}";
            if (key == _builtKey)
            {
                return;
            }

            _builtKey = key;
            if (_body != null)
            {
                physics.RemoveBody(_body.Body);
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.Components.Remove(_body);
                }
                _body = null;
            }

            var triangles = Extrude(Points, Depth, scale);
            if (triangles.Length > 0)
            {
                _body = physics.AddTriangleMeshBody(Owner, triangles);
                // Its own collider, not the node's "fit the model" box: saved as this script, never as collision.
                _body.FollowsVisualBounds = false;
            }
        }

        // Points are worked on as plain floats, x then y: the iPhone's AOT code got Vector2 values passed between
        // methods wrong (X and Y shifted), which put the phone's colliders where there was nothing to stand on.

        /// <summary>
        /// The outline cut into triangles by ear clipping, then made a prism: both faces, and a pair of triangles per
        /// edge for the sides. Three vertices a triangle, scale applied, as a triangle-mesh body takes them.
        /// </summary>
        public static Vector3[] Extrude(IReadOnlyList<Vector2> outline, float depth, Vector3 scale) => Extrude(Flatten(outline), depth, scale);

        public static Vector3[] Extrude(float[] points, float depth, Vector3 scale)
        {
            var count = points.Length / 2;
            if (count < 3)
            {
                return [];
            }

            // Counter-clockwise, which the ear clipping wants.
            var xy = new float[count * 2];
            var reverse = SignedArea(points, count) < 0.0f;
            for (var i = 0; i < count; i++)
            {
                var from = reverse ? count - 1 - i : i;
                xy[i * 2] = points[from * 2];
                xy[i * 2 + 1] = points[from * 2 + 1];
            }

            var faces = Triangulate(xy);
            var half = MathF.Max(depth, 0.001f) * 0.5f;
            Vector3 At(int i, float z) => new(xy[i * 2] * scale.X, xy[i * 2 + 1] * scale.Y, z * scale.Z);

            var result = new List<Vector3>();
            foreach (var (a, b, c) in faces)
            {
                // Front faces +Z, back faces -Z.
                result.Add(At(a, half)); result.Add(At(b, half)); result.Add(At(c, half));
                result.Add(At(a, -half)); result.Add(At(c, -half)); result.Add(At(b, -half));
            }

            for (var i = 0; i < count; i++)
            {
                var q = (i + 1) % count;
                result.Add(At(i, -half)); result.Add(At(q, -half)); result.Add(At(q, half));
                result.Add(At(i, -half)); result.Add(At(q, half)); result.Add(At(i, half));
            }

            return result.ToArray();
        }

        private static float[] Flatten(IReadOnlyList<Vector2> outline)
        {
            var xy = new float[outline.Count * 2];
            for (var i = 0; i < outline.Count; i++)
            {
                xy[i * 2] = outline[i].X;
                xy[i * 2 + 1] = outline[i].Y;
            }
            return xy;
        }

        private static float SignedArea(float[] xy, int count)
        {
            var area = 0.0f;
            for (var i = 0; i < count; i++)
            {
                var q = (i + 1) % count;
                area += xy[i * 2] * xy[q * 2 + 1] - xy[q * 2] * xy[i * 2 + 1];
            }
            return area * 0.5f;
        }

        /// <summary>Ear clipping on a counter-clockwise simple polygon: triangles as index triples.</summary>
        public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> polygon) => Triangulate(Flatten(polygon));

        public static List<(int A, int B, int C)> Triangulate(float[] xy)
        {
            var count = xy.Length / 2;
            var result = new List<(int, int, int)>();
            var remaining = Enumerable.Range(0, count).ToList();
            var guard = 0;
            while (remaining.Count > 3 && guard++ < 10000)
            {
                var clipped = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    var b = remaining[i];
                    var c = remaining[(i + 1) % remaining.Count];
                    if (Cross(xy, a, b, c) <= 0.0f)
                    {
                        continue;
                    }

                    var contains = false;
                    foreach (var other in remaining)
                    {
                        if (other != a && other != b && other != c && Inside(xy, other, a, b, c))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains)
                    {
                        continue;
                    }

                    result.Add((a, b, c));
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // Not a simple polygon (it crosses itself): fan what is left rather than loop forever.
                if (!clipped)
                {
                    break;
                }
            }

            for (var i = 1; i + 1 < remaining.Count; i++)
            {
                result.Add((remaining[0], remaining[i], remaining[i + 1]));
            }
            return result;
        }

        // Points by index into the x,y array.
        private static float Cross(float[] xy, int a, int b, int c) =>
            (xy[b * 2] - xy[a * 2]) * (xy[c * 2 + 1] - xy[a * 2 + 1]) - (xy[b * 2 + 1] - xy[a * 2 + 1]) * (xy[c * 2] - xy[a * 2]);

        private static bool Inside(float[] xy, int p, int a, int b, int c) =>
            Cross(xy, a, b, p) >= 0.0f && Cross(xy, b, c, p) >= 0.0f && Cross(xy, c, a, p) >= 0.0f;
    }
}
