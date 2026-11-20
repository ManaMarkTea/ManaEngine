using System.Runtime.CompilerServices;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// One cell's lines on a planet, sorted into classes (road kinds, border kinds): each line's points on the ground (the
    /// arrays its line mesh reads) and, fixed for good, each point's direction from the planet's centre and its cube-face
    /// (u, v), so laying them on the ground again is a height lookup.
    /// </summary>
    internal sealed class PlanetDrapedLineSet
    {
        public Vector3[][][] Points { get; }
        public int ClassCount { get; }
        public int PointCount { get; private set; }

        /// <summary>For areas rather than lines: each piece's triangles, as indices into its points (by class, then piece).</summary>
        public uint[][][]? Triangles { get; set; }

        /// <summary>
        /// Pieces laid level rather than on the ground below each point (a lake's surface): by class, then piece, the
        /// index its points inside the shore start at (-1 to follow the ground, as a river does).
        /// </summary>
        public int[][]? LevelFrom { get; set; }

        private readonly Vector3[][][] _directions;
        private readonly (int Face, float U, float V)[][][] _faceUvs;
        private readonly Dictionary<int, (float MinU, float MaxU, float MinV, float MaxV)> _boxes = new();

        private PlanetDrapedLineSet(int classCount)
        {
            ClassCount = classCount;
            Points = new Vector3[classCount][][];
            _directions = new Vector3[classCount][][];
            _faceUvs = new (int, float, float)[classCount][][];
        }

        /// <param name="stepAngle">The longest gap between points, as an angle at the planet's centre: longer ones are cut finer so the line follows the ground between.</param>
        public static PlanetDrapedLineSet From(IEnumerable<(int Class, IReadOnlyList<(double Longitude, double Latitude)> Points)> lines,
            int classCount, float stepAngle)
        {
            var set = new PlanetDrapedLineSet(classCount);
            var directions = new List<Vector3[]>[classCount];
            for (var c = 0; c < classCount; c++)
            {
                directions[c] = new List<Vector3[]>();
            }

            var dense = new List<Vector3>();
            foreach (var (lineClass, points) in lines)
            {
                dense.Clear();
                var previous = Vector3.Zero;
                foreach (var (longitude, latitude) in points)
                {
                    var direction = LatLong.FromLongitudeAndLatitude((float)longitude, (float)latitude).ToUnitSphere();
                    if (dense.Count > 0)
                    {
                        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(previous, direction), -1.0f, 1.0f));
                        var steps = Math.Min(256, (int)MathF.Ceiling(angle / MathF.Max(stepAngle, 1e-9f)));
                        for (var k = 1; k < steps; k++)
                        {
                            dense.Add(Vector3.Normalize(Vector3.Lerp(previous, direction, (float)k / steps)));
                        }
                    }
                    dense.Add(direction);
                    previous = direction;
                }
                if (dense.Count >= 2)
                {
                    directions[lineClass].Add(dense.ToArray());
                }
            }

            for (var c = 0; c < classCount; c++)
            {
                var classLines = directions[c].ToArray();
                set._directions[c] = classLines;
                set.Points[c] = new Vector3[classLines.Length][];
                set._faceUvs[c] = new (int, float, float)[classLines.Length][];
                for (var i = 0; i < classLines.Length; i++)
                {
                    var line = classLines[i];
                    set.Points[c][i] = new Vector3[line.Length];
                    var uvs = new (int Face, float U, float V)[line.Length];
                    for (var p = 0; p < line.Length; p++)
                    {
                        if (CubeSphere.TryUnwarpSphereToFaceUv(line[p], out var face, out var u, out var v))
                        {
                            uvs[p] = (face, u, v);
                            set.Grow(face, u, v);
                        }
                        else
                        {
                            uvs[p] = (-1, 0.0f, 0.0f);
                        }
                    }
                    set._faceUvs[c][i] = uvs;
                    set.PointCount += line.Length;
                }
            }
            return set;
        }

        private void Grow(int face, float u, float v)
        {
            if (_boxes.TryGetValue(face, out var box))
            {
                _boxes[face] = (MathF.Min(box.MinU, u), MathF.Max(box.MaxU, u), MathF.Min(box.MinV, v), MathF.Max(box.MaxV, v));
            }
            else
            {
                _boxes[face] = (u, u, v, v);
            }
        }

        public Vector3[][][] EmptyCopy()
        {
            var copy = new Vector3[ClassCount][][];
            for (var c = 0; c < ClassCount; c++)
            {
                copy[c] = new Vector3[Points[c].Length][];
                for (var i = 0; i < Points[c].Length; i++)
                {
                    copy[c][i] = new Vector3[Points[c][i].Length];
                }
            }
            return copy;
        }

        // The patches drawn under these lines.
        private List<(int Face, PlanetPatchMesh Mesh)> Under(PlanetPatchMesh[] leaves)
        {
            var under = new List<(int, PlanetPatchMesh)>();
            foreach (var mesh in leaves)
            {
                foreach (var (face, box) in _boxes)
                {
                    if (mesh.FaceNormal == CubeSphere.FaceNormals[face]
                        && mesh.MinU <= box.MaxU && mesh.MaxU >= box.MinU && mesh.MinV <= box.MaxV && mesh.MaxV >= box.MinV)
                    {
                        under.Add((face, mesh));
                    }
                }
            }
            return under;
        }

        /// <summary>Changes whenever a patch under the lines is replaced or its ground rebuilt.</summary>
        public int Signature(PlanetPatchMesh[] leaves)
        {
            var signature = 17;
            foreach (var (_, mesh) in Under(leaves))
            {
                var cpu = 0;
                if (mesh.CpuData != null)
                {
                    cpu = RuntimeHelpers.GetHashCode(mesh.CpuData);
                }
                signature ^= (RuntimeHelpers.GetHashCode(mesh) * 31) + cpu;
            }
            return signature;
        }

        /// <summary>
        /// Each point at the drawn ground's height below it, into <paramref name="into"/>. Never below sea level: the
        /// ground there is the sea floor, and a bridge or a coast laid on it would sink out of sight.
        /// </summary>
        public void Drape(PlanetPatchMesh[] leaves, float seaRadius, Vector3[][][] into)
        {
            var under = Under(leaves);
            for (var c = 0; c < ClassCount; c++)
            {
                for (var i = 0; i < _directions[c].Length; i++)
                {
                    var directions = _directions[c][i];
                    var uvs = _faceUvs[c][i];
                    var points = into[c][i];
                    for (var p = 0; p < directions.Length; p++)
                    {
                        var radius = seaRadius;
                        var (face, u, v) = uvs[p];
                        foreach (var (meshFace, mesh) in under)
                        {
                            if (meshFace != face || u < mesh.MinU || u > mesh.MaxU || v < mesh.MinV || v > mesh.MaxV)
                            {
                                continue;
                            }
                            if (PlanetNode.GroundRadius(mesh, u, v) is { } ground)
                            {
                                radius = MathF.Max(ground, seaRadius);
                            }
                            break;
                        }
                        points[p] = directions[p] * radius;
                    }
                    if (LevelFrom?[c][i] is { } inside && inside >= 0)
                    {
                        Level(points, directions, inside, seaRadius);
                    }
                }
            }
        }

        // A lake is flat: the drawn ground under it rises and falls between its points (the lake bed, or noise in the
        // heights), and laid on that the water let it through in patches. One level for the whole piece, from the
        // ground across its middle and along its shore, whichever is higher.
        private static void Level(Vector3[] points, Vector3[] directions, int inside, float seaRadius)
        {
            var shore = new List<float>(Math.Min(inside, points.Length));
            var middle = new List<float>(Math.Max(0, points.Length - inside));
            for (var p = 0; p < points.Length; p++)
            {
                if (p < inside)
                {
                    shore.Add(points[p].Length());
                }
                else
                {
                    middle.Add(points[p].Length());
                }
            }
            var level = seaRadius;
            if (shore.Count > 0)
            {
                shore.Sort();
                level = MathF.Max(level, shore[shore.Count / 2]);
            }
            if (middle.Count > 0)
            {
                middle.Sort();
                level = MathF.Max(level, middle[(int)(middle.Count * 0.9f)]);
            }
            for (var p = 0; p < points.Length; p++)
            {
                points[p] = directions[p] * level;
            }
        }
    }
}
