using Mana.Code.Components;
using Mana.Code.Nodes;

namespace Mana.Code.GameModes
{
    /// <summary>One handle on the selection's box: -1, 0 or 1 per local axis (a face is one non-zero, a corner two or three).</summary>
    public readonly record struct SceneScaleBoxHandle(int X, int Y, int Z)
    {
        public int this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;

        public int AxisCount => (X != 0 ? 1 : 0) + (Y != 0 ? 1 : 0) + (Z != 0 ? 1 : 0);

        /// <summary>A handle by name for scripts: "+x", "-y", "+x-y" (a corner), "-x+y+z".</summary>
        public static SceneScaleBoxHandle? Parse(string? name)
        {
            var axes = new int[3];
            var text = (name ?? "").Replace(" ", "").ToLowerInvariant();
            for (var i = 0; i + 1 < text.Length; i += 2)
            {
                var sign = text[i] == '+' ? 1 : text[i] == '-' ? -1 : 0;
                var axis = text[i + 1] - 'x';
                if (sign == 0 || axis is < 0 or > 2)
                {
                    return null;
                }

                axes[axis] = sign;
            }

            var handle = new SceneScaleBoxHandle(axes[0], axes[1], axes[2]);
            return text.Length % 2 == 0 && handle.AxisCount > 0 ? handle : null;
        }

        public override string ToString() =>
            (X != 0 ? (X > 0 ? "+x" : "-x") : "") + (Y != 0 ? (Y > 0 ? "+y" : "-y") : "") + (Z != 0 ? (Z > 0 ? "+z" : "-z") : "");
    }

    /// <summary>
    /// The Scale tool's handles: the selection's own box in its local frame, with a handle on each face (an edge, on a
    /// flat sprite) and corner. Shared by the gizmo, which picks and drags them, and the overlay, which draws them.
    /// </summary>
    public static class SceneScaleBox
    {
        /// <summary>The selection's bounds in its own frame (before its scale), from every mesh under it.</summary>
        public static bool TryLocalBounds(ManaSceneNode node, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.Zero;
            if (!Matrix4x4.Invert(node.WorldTransform, out var worldToLocal))
            {
                return false;
            }

            var found = false;
            var lo = Vector3.Zero;
            var hi = Vector3.Zero;
            void Include(ManaSceneNode current)
            {
                foreach (var renderer in current.Components.OfType<MeshRendererComponent>())
                {
                    if (CpuData(renderer) is not { Positions.Length: > 0 } data)
                    {
                        continue;
                    }

                    var toLocal = current.WorldTransform * worldToLocal;
                    var meshMin = data.Positions[0];
                    var meshMax = meshMin;
                    foreach (var position in data.Positions)
                    {
                        meshMin = Vector3.Min(meshMin, position);
                        meshMax = Vector3.Max(meshMax, position);
                    }

                    for (var corner = 0; corner < 8; corner++)
                    {
                        var point = Vector3.Transform(Corner(meshMin, meshMax, corner), toLocal);
                        lo = found ? Vector3.Min(lo, point) : point;
                        hi = found ? Vector3.Max(hi, point) : point;
                        found = true;
                    }
                }

                foreach (var child in current.Children)
                {
                    if (!child.IsEditorTransient)
                    {
                        Include(child);
                    }
                }
            }

            Include(node);
            min = lo;
            max = hi;
            return found && IsFinite(lo) && IsFinite(hi) && (hi - lo).LengthSquared() > 1e-10f;
        }

        public static Vector3 Corner(Vector3 min, Vector3 max, int index) => new(
            (index & 1) == 0 ? min.X : max.X,
            (index & 2) == 0 ? min.Y : max.Y,
            (index & 4) == 0 ? min.Z : max.Z);

        /// <summary>An axis the box has no depth along -- a sprite's Z. It gets no handles, and its faces become edges.</summary>
        public static bool IsFlat(Vector3 min, Vector3 max, int axis)
        {
            var size = max - min;
            var largest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            return Get(size, axis) <= MathF.Max(largest * 1e-3f, 1e-5f);
        }

        public static List<SceneScaleBoxHandle> Handles(Vector3 min, Vector3 max)
        {
            var handles = new List<SceneScaleBoxHandle>();
            int[] Range(int axis) => IsFlat(min, max, axis) ? [0] : [-1, 0, 1];
            foreach (var x in Range(0))
            foreach (var y in Range(1))
            foreach (var z in Range(2))
            {
                var handle = new SceneScaleBoxHandle(x, y, z);
                // Faces scale one axis, edges two, a solid's corners all three.
                if (handle.AxisCount > 0)
                {
                    handles.Add(handle);
                }
            }

            return handles;
        }

        public static int SolidAxes(Vector3 min, Vector3 max) =>
            (IsFlat(min, max, 0) ? 0 : 1) + (IsFlat(min, max, 1) ? 0 : 1) + (IsFlat(min, max, 2) ? 0 : 1);

        /// <summary>The handle's spot: on its face, centred along the axes it leaves alone.</summary>
        public static Vector3 Point(SceneScaleBoxHandle handle, Vector3 min, Vector3 max)
        {
            var centre = (min + max) * 0.5f;
            return new Vector3(
                handle.X < 0 ? min.X : handle.X > 0 ? max.X : centre.X,
                handle.Y < 0 ? min.Y : handle.Y > 0 ? max.Y : centre.Y,
                handle.Z < 0 ? min.Z : handle.Z > 0 ? max.Z : centre.Z);
        }

        /// <summary>
        /// A face of a flat box is an edge, grabbed anywhere along it: its two ends. False for a dot handle
        /// (a corner, or a face of a solid box).
        /// </summary>
        public static bool TryEdge(SceneScaleBoxHandle handle, Vector3 min, Vector3 max, out Vector3 start, out Vector3 end)
        {
            start = end = Point(handle, min, max);
            if (handle.AxisCount != 1 || SolidAxes(min, max) != 2)
            {
                return false;
            }

            for (var axis = 0; axis < 3; axis++)
            {
                if (handle[axis] == 0 && !IsFlat(min, max, axis))
                {
                    start = Set(start, axis, Get(min, axis));
                    end = Set(end, axis, Get(max, axis));
                }
            }

            return true;
        }

        public static float Get(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

        public static Vector3 Set(Vector3 value, int axis, float component)
        {
            switch (axis)
            {
                case 0:
                    value.X = component;
                    break;
                case 1:
                    value.Y = component;
                    break;
                default:
                    value.Z = component;
                    break;
            }

            return value;
        }

        private static ManaMeshCpuData? CpuData(MeshRendererComponent renderer)
        {
            try
            {
                return renderer.Mesh is { } mesh ? mesh.CpuData ?? mesh.GetOrCreateCpuData() : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
