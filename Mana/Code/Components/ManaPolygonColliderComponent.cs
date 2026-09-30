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

            var triangles = Extrude(Outline(), Depth, scale);
            if (triangles.Length > 0)
            {
                _body = physics.AddTriangleMeshBody(Owner, triangles);
                // Its own collider, not the node's "fit the model" box: saved as this script, never as collision.
                _body.FollowsVisualBounds = false;
            }
        }

        private List<Vector2> Outline()
        {
            var outline = new List<Vector2>();
            for (var i = 0; i + 1 < Points.Length; i += 2)
            {
                outline.Add(new Vector2(Points[i], Points[i + 1]));
            }
            return outline;
        }

        /// <summary>
        /// The outline cut into triangles by ear clipping, then made a prism: both faces, and a pair of triangles per
        /// edge for the sides. Three vertices a triangle, scale applied, as a triangle-mesh body takes them.
        /// </summary>
        public static Vector3[] Extrude(IReadOnlyList<Vector2> outline, float depth, Vector3 scale)
        {
            if (outline.Count < 3)
            {
                return [];
            }

            var polygon = outline.ToList();
            if (SignedArea(polygon) < 0.0f)
            {
                polygon.Reverse();
            }

            var faces = Triangulate(polygon);
            var half = MathF.Max(depth, 0.001f) * 0.5f;
            Vector3 At(Vector2 p, float z) => new(p.X * scale.X, p.Y * scale.Y, z * scale.Z);

            var result = new List<Vector3>();
            foreach (var (a, b, c) in faces)
            {
                // Front faces +Z, back faces -Z.
                result.Add(At(polygon[a], half)); result.Add(At(polygon[b], half)); result.Add(At(polygon[c], half));
                result.Add(At(polygon[a], -half)); result.Add(At(polygon[c], -half)); result.Add(At(polygon[b], -half));
            }

            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                result.Add(At(p, -half)); result.Add(At(q, -half)); result.Add(At(q, half));
                result.Add(At(p, -half)); result.Add(At(q, half)); result.Add(At(p, half));
            }

            return result.ToArray();
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            var area = 0.0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                area += p.X * q.Y - q.X * p.Y;
            }
            return area * 0.5f;
        }

        /// <summary>Ear clipping on a counter-clockwise simple polygon: triangles as index triples.</summary>
        public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> polygon)
        {
            var result = new List<(int, int, int)>();
            var remaining = Enumerable.Range(0, polygon.Count).ToList();
            var guard = 0;
            while (remaining.Count > 3 && guard++ < 10000)
            {
                var clipped = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    var b = remaining[i];
                    var c = remaining[(i + 1) % remaining.Count];
                    if (Cross(polygon[a], polygon[b], polygon[c]) <= 0.0f)
                    {
                        continue;
                    }

                    var contains = false;
                    foreach (var other in remaining)
                    {
                        if (other != a && other != b && other != c && Inside(polygon[other], polygon[a], polygon[b], polygon[c]))
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

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(a, b, p) >= 0.0f && Cross(b, c, p) >= 0.0f && Cross(c, a, p) >= 0.0f;
    }
}
