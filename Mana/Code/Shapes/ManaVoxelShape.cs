using System.Diagnostics.CodeAnalysis;

using System.Diagnostics;

namespace Mana
{
    public struct ManaVoxelData : IEqualityComparer<ManaVoxelData>
    {
        public Vector3 Position;
        public Vector4 Color;

        bool Equals(ManaVoxelData x, ManaVoxelData y)
        {
            return x.Position.AlmostEquals(y.Position);
        }

        public int GetHashCode([DisallowNull] ManaVoxelData obj)
        {
            return obj.Position.GetHashCode();
        }

        bool IEqualityComparer<ManaVoxelData>.Equals(ManaVoxelData x, ManaVoxelData y)
        {
            return x.Equals(y);
        }
    }

    public class ManaVoxelShape : ManaProceduralMesh
    {
        public const float DefaultVoxelRoughness = 0.72f;

        protected List<ManaVoxelData> Voxels = new List<ManaVoxelData>();

        protected List<ManaVoxelData> AddVoxels = new List<ManaVoxelData>();
        private readonly HashSet<(int X, int Y, int Z)> _voxelKeys = new HashSet<(int X, int Y, int Z)>();
        private readonly HashSet<(int X, int Y, int Z)> _pendingVoxelKeys = new HashSet<(int X, int Y, int Z)>();
        private static readonly (int X, int Y, int Z)[] FaceNeighborKeyOffsets =
        {
            (0, 0, -2),
            (2, 0, 0),
            (0, 0, 2),
            (-2, 0, 0),
            (0, 2, 0),
            (0, -2, 0)
        };

        public int VoxelCount => Voxels.Count;
        public int PendingVoxelCount => AddVoxels.Count;
        public int DuplicateVoxelRejectCount { get; private set; }
        public int LastInputVoxelCount { get; private set; }
        public int LastDuplicateVoxelCount { get; private set; }
        public int LastVisibleFaceCount { get; private set; }
        public int LastCulledFaceCount { get; private set; }
        public int LastGeneratedVertexCount { get; private set; }
        public int LastNaiveVertexCount { get; private set; }
        public int LastSavedVertexCount { get; private set; }
        public IReadOnlyList<ManaVoxelData> CommittedVoxels => Voxels;
        public IReadOnlyList<ManaVoxelData> PendingVoxels => AddVoxels;

        private static (int X, int Y, int Z) ToVoxelKey(Vector3 position)
        {
            return (
                (int)MathF.Round(position.X * 2.0f),
                (int)MathF.Round(position.Y * 2.0f),
                (int)MathF.Round(position.Z * 2.0f)
            );
        }


        public ManaVoxelShape(ManaContext context) : base(context)
        {
            DefaultVertexColor = Colors.White;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = base.GenerateMaterial();
            ConfigureVoxelPbrMaterial(Context, material);
            return material;
        }

        public static void ConfigureVoxelPbrMaterial(ManaContext context, ManaMaterial material)
        {
            material.Shader = new ManaPbrShader(context);
            material.Metallic.Value = 0.0f;
            material.Roughness.Value = DefaultVoxelRoughness;
            material.Occlusion.Value = 1.0f;
        }

        public event Action<ManaVoxelData>? OnVoxelAdded;

        /// <summary>
        /// Fired once per voxel actually removed by EraseRadius. ManaVoxelsNode listens for
        /// this to remove the matching kinematic collision body -- without it, an erased voxel's collider
        /// stays behind in the physics world, invisible but still blocking the raycast every other tool
        /// (including a second erase) picks against.
        /// </summary>
        public event Action<ManaVoxelData>? OnVoxelRemoved;

        public void AddVoxel(Vector3 position, Vector4 color)
        {
            var newVoxelData = new ManaVoxelData { Position = position, Color = color };
            var key = ToVoxelKey(position);

            if (_voxelKeys.Contains(key) || _pendingVoxelKeys.Contains(key))
            {
                DuplicateVoxelRejectCount++;
                return;
            }

            AddVoxels.Add(newVoxelData);
            _pendingVoxelKeys.Add(key);
            OnVoxelAdded?.Invoke(newVoxelData);
        }

        public void CommitData()
        {
            GenerateData();
        }

        /// <summary>Shortest squared distance from a point to the segment [a, b] (a degenerate a == b segment is just a point).</summary>
        private static float DistanceSquaredToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var segment = b - a;
            var lengthSquared = segment.LengthSquared();
            if (lengthSquared < 1e-8f)
            {
                return Vector3.DistanceSquared(point, a);
            }

            var t = Math.Clamp(Vector3.Dot(point - a, segment) / lengthSquared, 0.0f, 1.0f);
            return Vector3.DistanceSquared(point, a + segment * t);
        }

        /// <summary>Recolours every committed voxel within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public int PaintRadius(Vector3 center, float radius, Vector4 newColor)
        {
            return PaintRadius(center, center, radius, newColor);
        }

        /// <summary>
        /// Recolours every committed voxel within <paramref name="radius"/> of the segment from
        /// <paramref name="from"/> to <paramref name="to"/>, not just of a single point -- a brush dragged
        /// across more than one voxel in a frame (a fast mouse move, or a hitch) must still paint the cells
        /// in between rather than leaving a gap. Rebuilds geometry once, not per voxel. Returns the number
        /// of voxels changed.
        /// </summary>
        public int PaintRadius(Vector3 from, Vector3 to, float radius, Vector4 newColor)
        {
            var radiusSquared = radius * radius;
            var changed = 0;

            for (var i = 0; i < Voxels.Count; i++)
            {
                var voxel = Voxels[i];
                if (DistanceSquaredToSegment(voxel.Position, from, to) > radiusSquared)
                {
                    continue;
                }

                voxel.Color = newColor;
                Voxels[i] = voxel;
                changed++;
            }

            if (changed > 0)
            {
                RebuildGeometryFromCommittedVoxels("paint", Voxels.Count, 0, logStats: false);
            }

            return changed;
        }

        /// <summary>Removes every committed voxel within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public int EraseRadius(Vector3 center, float radius)
        {
            return EraseRadius(center, center, radius);
        }

        /// <summary>
        /// Removes every committed voxel within <paramref name="radius"/> of the segment from
        /// <paramref name="from"/> to <paramref name="to"/> -- see <see cref="PaintRadius(Vector3, Vector3, float, Vector4)"/>
        /// for why a segment rather than just a point. Fires <see cref="OnVoxelRemoved"/> for each voxel
        /// removed, so the node holding this shape can drop the matching physics collider. Returns the
        /// number of voxels removed.
        /// </summary>
        public int EraseRadius(Vector3 from, Vector3 to, float radius)
        {
            var radiusSquared = radius * radius;
            var removed = 0;

            for (var i = Voxels.Count - 1; i >= 0; i--)
            {
                var voxel = Voxels[i];
                if (DistanceSquaredToSegment(voxel.Position, from, to) > radiusSquared)
                {
                    continue;
                }

                _voxelKeys.Remove(ToVoxelKey(voxel.Position));
                Voxels.RemoveAt(i);
                removed++;
                OnVoxelRemoved?.Invoke(voxel);
            }

            if (removed > 0)
            {
                RebuildGeometryFromCommittedVoxels("erase", Voxels.Count, 0, logStats: false);
            }

            return removed;
        }

        /// <summary>
        /// Recolours every voxel face-connected to the one at <paramref name="seedPosition"/> that shares its
        /// exact starting colour, stopping at any colour boundary. Returns the number changed, or 0 if there
        /// is no voxel at the seed position (or it is already <paramref name="newColor"/>).
        /// </summary>
        public int FloodFill(Vector3 seedPosition, Vector4 newColor)
        {
            var seedKey = ToVoxelKey(seedPosition);
            var indexByKey = new Dictionary<(int X, int Y, int Z), int>(Voxels.Count);
            for (var i = 0; i < Voxels.Count; i++)
            {
                indexByKey[ToVoxelKey(Voxels[i].Position)] = i;
            }

            if (!indexByKey.TryGetValue(seedKey, out var seedIndex))
            {
                return 0;
            }

            var targetColor = Voxels[seedIndex].Color;
            if (targetColor == newColor)
            {
                return 0;
            }

            var visited = new HashSet<(int X, int Y, int Z)> { seedKey };
            var queue = new Queue<(int X, int Y, int Z)>();
            queue.Enqueue(seedKey);
            var changed = 0;

            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                var index = indexByKey[key];
                var voxel = Voxels[index];
                voxel.Color = newColor;
                Voxels[index] = voxel;
                changed++;

                foreach (var offset in FaceNeighborKeyOffsets)
                {
                    var neighborKey = (key.X + offset.X, key.Y + offset.Y, key.Z + offset.Z);
                    if (!visited.Add(neighborKey) || !indexByKey.TryGetValue(neighborKey, out var neighborIndex))
                    {
                        continue;
                    }

                    if (Voxels[neighborIndex].Color != targetColor)
                    {
                        continue;
                    }

                    queue.Enqueue(neighborKey);
                }
            }

            if (changed > 0)
            {
                RebuildGeometryFromCommittedVoxels("flood fill", Voxels.Count, 0, logStats: false);
            }

            return changed;
        }

        public void ReplaceCommittedVoxels(IEnumerable<ManaVoxelData> voxels, bool logStats = true)
        {
            Voxels.Clear();
            AddVoxels.Clear();
            _voxelKeys.Clear();
            _pendingVoxelKeys.Clear();

            var inputVoxelCount = 0;
            var duplicateVoxelCount = 0;

            foreach (var voxel in voxels)
            {
                inputVoxelCount++;
                var key = ToVoxelKey(voxel.Position);
                if (!_voxelKeys.Add(key))
                {
                    DuplicateVoxelRejectCount++;
                    duplicateVoxelCount++;
                    continue;
                }

                Voxels.Add(voxel);
            }

            RebuildGeometryFromCommittedVoxels(
                "replace",
                inputVoxelCount,
                duplicateVoxelCount,
                logStats);
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var inputVoxelCount = Voxels.Count + AddVoxels.Count;
            var duplicateVoxelCount = 0;

            foreach (var voxel in AddVoxels)
            {
                var key = ToVoxelKey(voxel.Position);
                if (!_voxelKeys.Add(key))
                {
                    DuplicateVoxelRejectCount++;
                    duplicateVoxelCount++;
                    continue;
                }

                Voxels.Add(voxel);
            }

            AddVoxels.Clear();
            _pendingVoxelKeys.Clear();
            RebuildGeometryFromCommittedVoxels(
                "commit",
                inputVoxelCount,
                duplicateVoxelCount,
                inputVoxelCount > 0 || duplicateVoxelCount > 0);

            // TODO: Reuse the existing buffers better
        }

        private void RebuildGeometryFromCommittedVoxels(
            string reason,
            int inputVoxelCount,
            int duplicateVoxelCount,
            bool logStats)
        {
            DynamicGeometryContainer.Clear();

            var visibleFaceCount = 0;
            foreach (var voxel in Voxels)
            {
                visibleFaceCount += AddExposedVoxelFaces(voxel);
            }

            LastInputVoxelCount = inputVoxelCount;
            LastDuplicateVoxelCount = duplicateVoxelCount;
            LastVisibleFaceCount = visibleFaceCount;
            LastCulledFaceCount = Math.Max(0, Voxels.Count * 6 - visibleFaceCount);
            LastGeneratedVertexCount = DynamicGeometryContainer.Verts.Count;
            LastNaiveVertexCount = inputVoxelCount * 24;
            LastSavedVertexCount = Math.Max(0, LastNaiveVertexCount - LastGeneratedVertexCount);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
                if (VertexBufferObject != null)
                {
                    var meshData = DynamicGeometryContainer.CreateCpuData();
                    VertexBufferObject.indexes = meshData.Indexes;
                    VertexBufferObject.positions = meshData.Positions;
                    VertexBufferObject.normals = meshData.Normals;
                    VertexBufferObject.colors = meshData.Colors;
                    VertexBufferObject.textureCoordinates0 = meshData.TextureCoordinates0;
                    VertexBufferObject.textureCoordinates1 = meshData.TextureCoordinates1;
                    VertexBufferObject.ConstructVBO();
                }
                else
                {
                    VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
                }
            }

            if (logStats && (inputVoxelCount > 0 || duplicateVoxelCount > 0))
            {
                Trace.WriteLine(
                    $"ManaVoxelShape {reason}: inputVoxels={inputVoxelCount}, uniqueVoxels={Voxels.Count}, " +
                    $"duplicateVoxels={duplicateVoxelCount}, visibleFaces={LastVisibleFaceCount}, " +
                    $"culledFaces={LastCulledFaceCount}, verts={LastGeneratedVertexCount}/{LastNaiveVertexCount}, " +
                    $"dedupedVerts={LastSavedVertexCount}");
            }
        }

        private int AddExposedVoxelFaces(ManaVoxelData voxel)
        {
            var key = ToVoxelKey(voxel.Position);
            var addedFaceCount = 0;

            for (var faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                var neighborOffset = FaceNeighborKeyOffsets[faceIndex];
                var neighborKey = (
                    key.X + neighborOffset.X,
                    key.Y + neighborOffset.Y,
                    key.Z + neighborOffset.Z);

                if (_voxelKeys.Contains(neighborKey))
                {
                    continue;
                }

                DynamicGeometryContainer.AddQuad(
                    voxel.Position,
                    Vector3.One,
                    ManaDynamicGeometryContainer.CubeFaceRotations[faceIndex],
                    ManaDynamicGeometryContainer.CubeFaceScales[faceIndex],
                    voxel.Color);
                addedFaceCount++;
            }

            return addedFaceCount;
        }

    }
}
