using BepuPhysics;
using Mana.Code.Components;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mana.Code.Nodes
{
    public class ManaVoxelsNode : ManaSceneNode
    {
        public enum LockOverlapPolicy
        {
            Closest = 0,
            Brightest = 1,
            FirstSeen = 2
        }

        private readonly struct BakedVoxelBody
        {
            public BodyHandle Handle { get; }
            public Vector4 Color { get; }
            public Vector3 TargetPosition { get; }

            public BakedVoxelBody(BodyHandle handle, Vector4 color, Vector3 targetPosition)
            {
                Handle = handle;
                Color = color;
                TargetPosition = targetPosition;
            }
        }

        private struct SnappedVoxelCandidate
        {
            public ManaVoxelData Voxel;
            public float DistanceSquared;
            public float ColorPriority;
        }

        private readonly ManaVoxelShape shape;
        private readonly MeshRendererComponent mergedVoxelRenderer;
        public ManaVoxelShape VoxelShape => shape;
        public string Source { get; set; } = string.Empty;
        public bool ShowOutline { get; set; }
        public bool AutoCreateKinematicVoxelBodies { get; private set; } = true;
        public bool IsBakedToDynamicPhysics { get; private set; }
        public bool IsReassembling { get; private set; }
        public float ExplosionStrength { get; set; } = 70.5f;
        public float ExplosionRadiusPadding { get; set; } = 1.0f;
        public float ReassembleSettleDistance { get; set; } = 0.06f;
        public float ReassembleSettleVelocity { get; set; } = 0.18f;
        public float ReassemblePullGain { get; set; } = 9.5f;
        public float ReassembleVelocityDamping { get; set; } = 0.86f;
        public float ReassembleRotationLerp { get; set; } = 0.18f;
        public bool IsLockPreviewEnabled { get; private set; }
        public int LockPreviewVoxelCount { get; private set; }
        public int LockPreviewOverlapCount { get; private set; }
        public LockOverlapPolicy OverlapPolicy { get; set; } = LockOverlapPolicy.Closest;

        private List<BakedVoxelBody>? _bakedBodies;
        private BakedVoxelsMeshUpdater? _bakedMeshUpdater;
        private MeshRendererComponent? _bakedMeshRenderer;
        private LockPreviewMeshUpdater? _lockPreviewMeshUpdater;
        private MeshRendererComponent? _lockPreviewRenderer;

        public ManaVoxelsNode(ManaContext context, Matrix4x4 localTransform) : base(context, localTransform)
        {
            shape = new ManaVoxelShape(Context);
            shape.OnVoxelAdded += Shape_OnVoxelAdded;
            mergedVoxelRenderer = new MeshRendererComponent(this, shape);
            Components.Add(mergedVoxelRenderer);
        }

        private void Shape_OnVoxelAdded(ManaVoxelData obj)
        {
            if (!AutoCreateKinematicVoxelBodies)
            {
                return;
            }

            Context.Physics.AddBoxBody(this, Vector3.One, obj.Position);
        }

        public int BakeVoxelsToDynamicPhysics(float massPerVoxel = 1.0f)
        {
            if (IsBakedToDynamicPhysics)
            {
                return 0;
            }

            AutoCreateKinematicVoxelBodies = false;
            IsBakedToDynamicPhysics = true;

            var existingVoxelBodies = Components.OfType<ManaCollisionComponent>().ToArray();
            foreach (var collision in existingVoxelBodies)
            {
                Context.Physics.RemoveBody(collision.Body);
            }

            Components.Remove(mergedVoxelRenderer);

            _bakedBodies = new List<BakedVoxelBody>();
            var bakeSpawnOffset = new Vector3(0, 2.0f, 0);

            foreach (var voxel in shape.CommittedVoxels)
            {
                var spawnPos = LocalToWorldPosition(voxel.Position) + bakeSpawnOffset;
                var handle = Context.Physics.AddDynamicBoxBodyDirect(spawnPos, Vector3.One, massPerVoxel);
                _bakedBodies.Add(new BakedVoxelBody(handle, voxel.Color, spawnPos));
            }

            ExplodeBakedVoxels();

            // Build a single merged mesh from the initial spawn positions
            var geom = new ManaDynamicGeometryContainer();
            foreach (var bakedBody in _bakedBodies)
            {
                var pose = Context.Physics.Simulation.Bodies[bakedBody.Handle].Pose;
                geom.AddOrientedCube(WorldToLocalPosition(pose.Position), pose.Orientation, Vector3.One, bakedBody.Color);
            }

            var bakedMesh = new ManaMesh(Context);
            var sharedMaterial = new ManaMaterial(Context);
            sharedMaterial.Name = $"{Name} Baked Voxels Material";
            sharedMaterial.Albedo.Value = Colors.White;
            ManaVoxelShape.ConfigureVoxelPbrMaterial(Context, sharedMaterial);
            bakedMesh.Materials = new ManaMaterial[] { sharedMaterial };
            bakedMesh.SetCpuData(geom.CreateCpuData());
            if (Context.OpenGl != null)
            {
                bakedMesh.VertexBufferObject = geom.CreateVertexBufferObject(Context, BufferUsageARB.DynamicDraw);
            }

            // Add updater (runs first) then renderer — order matters
            _bakedMeshUpdater = new BakedVoxelsMeshUpdater(this, _bakedBodies, bakedMesh);
            Components.Add(_bakedMeshUpdater);
            _bakedMeshRenderer = new MeshRendererComponent(this, bakedMesh);
            Components.Add(_bakedMeshRenderer);

            return shape.CommittedVoxels.Count;
        }

        public int LockBakedVoxelsForEditing()
        {
            if (!IsBakedToDynamicPhysics || _bakedBodies == null)
            {
                return 0;
            }

            IsReassembling = false;

            var rebakedVoxels = BuildLockPreviewVoxels();

            foreach (var bakedBody in _bakedBodies)
            {
                Context.Physics.RemoveBody(bakedBody.Handle);
            }

            _bakedBodies.Clear();

            if (_bakedMeshUpdater != null)
            {
                Components.Remove(_bakedMeshUpdater);
                _bakedMeshUpdater = null;
            }

            if (_bakedMeshRenderer != null)
            {
                Components.Remove(_bakedMeshRenderer);
                _bakedMeshRenderer = null;
            }

            if (!Components.Contains(mergedVoxelRenderer))
            {
                Components.Add(mergedVoxelRenderer);
            }

            AutoCreateKinematicVoxelBodies = true;
            shape.ReplaceCommittedVoxels(rebakedVoxels);
            foreach (var voxel in shape.CommittedVoxels)
            {
                Context.Physics.AddBoxBody(this, Vector3.One, voxel.Position);
            }

            _bakedBodies = null;
            IsBakedToDynamicPhysics = false;
            SetLockPreviewEnabled(false);
            return shape.CommittedVoxels.Count;
        }

        public void SetLockPreviewEnabled(bool enabled)
        {
            if (enabled == IsLockPreviewEnabled)
            {
                return;
            }

            IsLockPreviewEnabled = enabled;

            if (!enabled)
            {
                RemoveLockPreviewComponents();
                return;
            }

            if (!IsBakedToDynamicPhysics || _bakedBodies == null || _bakedBodies.Count == 0)
            {
                IsLockPreviewEnabled = false;
                return;
            }

            EnsureLockPreviewComponents();
        }

        public int GetLockPreviewVoxelCount()
        {
            if (!IsBakedToDynamicPhysics || _bakedBodies == null)
            {
                LockPreviewVoxelCount = 0;
                LockPreviewOverlapCount = 0;
                return 0;
            }

            BuildLockPreviewVoxels();
            return LockPreviewVoxelCount;
        }

        private static Vector3 SnapToVoxelCenter(Vector3 position)
        {
            return new Vector3(
                MathF.Round(position.X - 0.5f) + 0.5f,
                MathF.Round(position.Y - 0.5f) + 0.5f,
                MathF.Round(position.Z - 0.5f) + 0.5f);
        }

        private static (int X, int Y, int Z) ToVoxelKey(Vector3 position)
        {
            return (
                (int)MathF.Round(position.X * 2.0f),
                (int)MathF.Round(position.Y * 2.0f),
                (int)MathF.Round(position.Z * 2.0f));
        }

        private Vector3 LocalToWorldPosition(Vector3 localPosition)
        {
            return Vector3.Transform(localPosition, WorldTransform);
        }

        private Vector3 WorldToLocalPosition(Vector3 worldPosition)
        {
            if (Matrix4x4.Invert(WorldTransform, out var worldToLocal))
            {
                return Vector3.Transform(worldPosition, worldToLocal);
            }

            return worldPosition - WorldTransform.Translation;
        }

        private List<ManaVoxelData> BuildLockPreviewVoxels()
        {
            var preview = new List<ManaVoxelData>(_bakedBodies?.Count ?? 0);
            if (_bakedBodies == null)
            {
                LockPreviewVoxelCount = 0;
                LockPreviewOverlapCount = 0;
                return preview;
            }

            var candidates = new Dictionary<(int X, int Y, int Z), SnappedVoxelCandidate>(_bakedBodies.Count);
            var overlapCount = 0;

            foreach (var bakedBody in _bakedBodies)
            {
                var bodyPosition = Context.Physics.Simulation.Bodies.BodyExists(bakedBody.Handle)
                    ? Context.Physics.Simulation.Bodies[bakedBody.Handle].Pose.Position
                    : bakedBody.TargetPosition;

                var snappedPosition = SnapToVoxelCenter(bodyPosition);
                var key = ToVoxelKey(snappedPosition);
                var candidate = new SnappedVoxelCandidate
                {
                    Voxel = new ManaVoxelData
                    {
                        Position = WorldToLocalPosition(snappedPosition),
                        Color = bakedBody.Color
                    },
                    DistanceSquared = Vector3.DistanceSquared(bodyPosition, snappedPosition),
                    ColorPriority = ComputeColorPriority(bakedBody.Color)
                };

                if (candidates.TryGetValue(key, out var existing))
                {
                    overlapCount++;
                    if (!ShouldReplaceSnappedCandidate(existing, candidate, OverlapPolicy))
                    {
                        continue;
                    }
                }

                candidates[key] = candidate;
            }

            preview.AddRange(candidates.Values.Select(candidate => candidate.Voxel));
            LockPreviewVoxelCount = preview.Count;
            LockPreviewOverlapCount = overlapCount;
            return preview;
        }

        private static bool ShouldReplaceSnappedCandidate(
            SnappedVoxelCandidate existing,
            SnappedVoxelCandidate incoming,
            LockOverlapPolicy overlapPolicy)
        {
            const float epsilon = 0.0001f;
            if (overlapPolicy == LockOverlapPolicy.FirstSeen)
            {
                return false;
            }

            if (overlapPolicy == LockOverlapPolicy.Brightest)
            {
                if (incoming.ColorPriority > existing.ColorPriority + epsilon)
                {
                    return true;
                }

                if (existing.ColorPriority > incoming.ColorPriority + epsilon)
                {
                    return false;
                }

                if (incoming.DistanceSquared + epsilon < existing.DistanceSquared)
                {
                    return true;
                }

                if (existing.DistanceSquared + epsilon < incoming.DistanceSquared)
                {
                    return false;
                }

                return incoming.Voxel.Position.Y > existing.Voxel.Position.Y;
            }

            if (incoming.DistanceSquared + epsilon < existing.DistanceSquared)
            {
                return true;
            }

            if (existing.DistanceSquared + epsilon < incoming.DistanceSquared)
            {
                return false;
            }

            if (incoming.ColorPriority > existing.ColorPriority + epsilon)
            {
                return true;
            }

            if (existing.ColorPriority > incoming.ColorPriority + epsilon)
            {
                return false;
            }

            return incoming.Voxel.Position.Y > existing.Voxel.Position.Y;
        }

        private static float ComputeColorPriority(Vector4 color)
        {
            return color.W * 4.0f + color.X * 0.2126f + color.Y * 0.7152f + color.Z * 0.0722f;
        }

        private void EnsureLockPreviewComponents()
        {
            if (_bakedBodies == null || _bakedBodies.Count == 0)
            {
                return;
            }

            if (_lockPreviewMeshUpdater != null && _lockPreviewRenderer != null)
            {
                return;
            }

            var previewGeom = new ManaDynamicGeometryContainer();
            for (var i = 0; i < _bakedBodies.Count; i++)
            {
                previewGeom.AddOrientedCube(Vector3.Zero, Quaternion.Identity, Vector3.One * 1.03f, new Vector4(1, 1, 1, 0));
            }

            var previewMesh = new ManaMesh(Context);
            var previewMaterial = new ManaMaterial(Context);
            previewMaterial.Name = $"{Name} Lock Preview Material";
            previewMaterial.Albedo.Value = Colors.White;
            previewMaterial.Emission.Value = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
            previewMaterial.Metallic.Value = 0.0f;
            previewMaterial.Roughness.Value = ManaVoxelShape.DefaultVoxelRoughness;
            previewMaterial.Occlusion.Value = 1.0f;
            previewMesh.Materials = new[] { previewMaterial };
            previewMesh.SetCpuData(previewGeom.CreateCpuData());
            if (Context.OpenGl != null)
            {
                previewMesh.VertexBufferObject = previewGeom.CreateVertexBufferObject(Context, BufferUsageARB.DynamicDraw);
            }

            _lockPreviewMeshUpdater = new LockPreviewMeshUpdater(this, _bakedBodies, previewMesh);
            Components.Add(_lockPreviewMeshUpdater);
            _lockPreviewRenderer = new MeshRendererComponent(this, previewMesh);
            Components.Add(_lockPreviewRenderer);
        }

        private void RemoveLockPreviewComponents()
        {
            if (_lockPreviewMeshUpdater != null)
            {
                Components.Remove(_lockPreviewMeshUpdater);
                _lockPreviewMeshUpdater = null;
            }

            if (_lockPreviewRenderer != null)
            {
                Components.Remove(_lockPreviewRenderer);
                _lockPreviewRenderer = null;
            }

            LockPreviewVoxelCount = 0;
            LockPreviewOverlapCount = 0;
        }
    
        public int ExplodeBakedVoxels(float? strength = null, float? minRadiusPadding = null)
        {
            if (!IsBakedToDynamicPhysics || _bakedBodies == null || _bakedBodies.Count == 0)
            {
                return 0;
            }

            IsReassembling = false;

            var simulation = Context.Physics.Simulation;
            var centerOfMass = Vector3.Zero;
            int liveBodyCount = 0;
            foreach (var bakedBody in _bakedBodies)
            {
                if (!simulation.Bodies.BodyExists(bakedBody.Handle))
                {
                    continue;
                }

                centerOfMass += simulation.Bodies[bakedBody.Handle].Pose.Position;
                liveBodyCount++;
            }

            if (liveBodyCount == 0)
            {
                return 0;
            }

            centerOfMass /= liveBodyCount;

            var maxDistance = 0.0f;
            foreach (var bakedBody in _bakedBodies)
            {
                if (!simulation.Bodies.BodyExists(bakedBody.Handle))
                {
                    continue;
                }

                var bodyPos = simulation.Bodies[bakedBody.Handle].Pose.Position;
                var dist = Vector3.Distance(centerOfMass, bodyPos);
                if (dist > maxDistance)
                {
                    maxDistance = dist;
                }
            }

            var appliedStrength = strength ?? ExplosionStrength;
            var appliedRadiusPadding = minRadiusPadding ?? ExplosionRadiusPadding;
            var explosionRadius = MathF.Max(2.0f, maxDistance + appliedRadiusPadding);
            return Context.Physics.ApplyExplosion(
                centerOfMass,
                explosionRadius,
                appliedStrength,
                _bakedBodies.Select(b => b.Handle));
        }

        public int ExplodeBakedVoxelsAt(Vector3 center, float radius = 6.0f, float? strength = null, bool preserveReassembly = false)
        {
            if (!IsBakedToDynamicPhysics || _bakedBodies == null || _bakedBodies.Count == 0)
            {
                return 0;
            }

            if (!preserveReassembly)
            {
                IsReassembling = false;
            }

            var appliedStrength = strength ?? ExplosionStrength;
            var appliedRadius = MathF.Max(0.1f, radius);
            return Context.Physics.ApplyExplosion(
                center,
                appliedRadius,
                appliedStrength,
                _bakedBodies.Select(b => b.Handle));
        }

        public bool BeginReassembleBakedVoxels()
        {
            if (!IsBakedToDynamicPhysics || _bakedBodies == null || _bakedBodies.Count == 0)
            {
                return false;
            }

            IsReassembling = true;
            return true;
        }

        public int GetBakedBodyCount()
        {
            return _bakedBodies?.Count ?? 0;
        }

        private sealed class BakedVoxelsMeshUpdater : ManaComponent
        {
            private readonly ManaVoxelsNode _voxelsNode;
            private readonly List<BakedVoxelBody> _bodies;
            private readonly ManaMesh _mesh;
            private readonly Vector3[] _positions;
            private readonly Vector3[] _normals;
            private readonly Vector4[] _colors;
            private readonly Vector3 _cubeSize = Vector3.One;

            public BakedVoxelsMeshUpdater(
                ManaSceneNode owner,
                List<BakedVoxelBody> bodies,
                ManaMesh mesh) : base(owner)
            {
                _voxelsNode = (ManaVoxelsNode)owner;
                _bodies = bodies;
                _mesh = mesh;

                var vertexCount = _bodies.Count * 24;
                _positions = new Vector3[vertexCount];
                _normals = new Vector3[vertexCount];
                _colors = new Vector4[vertexCount];

                for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
                {
                    var color = _bodies[bodyIndex].Color;
                    var start = bodyIndex * 24;
                    for (var i = 0; i < 24; i++)
                    {
                        _colors[start + i] = color;
                    }
                }
            }

            public override void UpdateComponent()
            {
                var physics = Context.Physics;

                if (_voxelsNode.IsReassembling)
                {
                    var allSettled = SteerBodiesTowardsTargets(physics);
                    if (allSettled)
                    {
                        _voxelsNode.IsReassembling = false;
                    }
                }

                if (_bodies.Count > 256)
                {
                    Parallel.For(0, _bodies.Count, bodyIndex => UpdateBodyGeometry(physics, bodyIndex));
                }
                else
                {
                    for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
                    {
                        UpdateBodyGeometry(physics, bodyIndex);
                    }
                }

                UpdateMeshGeometry();
            }

            private void UpdateMeshGeometry()
            {
                var currentData = _mesh.GetOrCreateCpuData();
                _mesh.SetCpuData(new ManaCore.Graphics.ManaMeshCpuData
                {
                    Indexes = currentData.Indexes,
                    Positions = _positions,
                    Normals = _normals,
                    Colors = _colors,
                    TextureCoordinates0 = currentData.TextureCoordinates0,
                    TextureCoordinates1 = currentData.TextureCoordinates1,
                    AlbedoTexturePath = currentData.AlbedoTexturePath,
                    AlbedoFactor = currentData.AlbedoFactor
                });

                _mesh.ImmediateGpuMesh?.UpdateDynamicData(_positions, _normals, _colors);
            }

            private void UpdateBodyGeometry(ManaPhysics physics, int bodyIndex)
            {
                var handle = _bodies[bodyIndex].Handle;
                var writeOffset = bodyIndex * 24;

                if (!physics.Simulation.Bodies.BodyExists(handle))
                {
                    for (var i = 0; i < 24; i++)
                    {
                        _positions[writeOffset + i] = Vector3.Zero;
                        _normals[writeOffset + i] = Vector3.UnitY;
                        _colors[writeOffset + i].W = 0.0f;
                    }
                    return;
                }

                var pose = physics.Simulation.Bodies[handle].Pose;
                var bodyMatrix = Matrix4x4.CreateFromQuaternion(pose.Orientation);
                var localPosePosition = _voxelsNode.WorldToLocalPosition(pose.Position);

                for (var face = 0; face < 6; face++)
                {
                    var scale = ManaDynamicGeometryContainer.CubeFaceScales[face];
                    var cR = Vector3.Transform(_cubeSize * 0.5f, scale);
                    var combinedRotation = ManaDynamicGeometryContainer.CubeFaceRotations[face] * bodyMatrix;

                    var faceOffset = writeOffset + face * 4;
                    _positions[faceOffset + 0] = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), combinedRotation) + localPosePosition;
                    _positions[faceOffset + 1] = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), combinedRotation) + localPosePosition;
                    _positions[faceOffset + 2] = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), combinedRotation) + localPosePosition;
                    _positions[faceOffset + 3] = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), combinedRotation) + localPosePosition;

                    var faceCenter = (_positions[faceOffset + 0] + _positions[faceOffset + 1] + _positions[faceOffset + 2] + _positions[faceOffset + 3]) * 0.25f;
                    var normal = new Vector3(0, 0, 1).TransformNormal(combinedRotation);
                    if (Vector3.Dot(faceCenter - localPosePosition, normal) < 0.0f)
                    {
                        normal = -normal;
                    }
                    normal = Vector3.Normalize(normal);
                    _normals[faceOffset + 0] = normal;
                    _normals[faceOffset + 1] = normal;
                    _normals[faceOffset + 2] = normal;
                    _normals[faceOffset + 3] = normal;

                    _colors[faceOffset + 0].W = 1.0f;
                    _colors[faceOffset + 1].W = 1.0f;
                    _colors[faceOffset + 2].W = 1.0f;
                    _colors[faceOffset + 3].W = 1.0f;
                }
            }

            private bool SteerBodiesTowardsTargets(ManaPhysics physics)
            {
                bool allSettled = true;
                var settleDistance = MathF.Max(0.001f, _voxelsNode.ReassembleSettleDistance);
                var settleVelocity = MathF.Max(0.001f, _voxelsNode.ReassembleSettleVelocity);
                var pullGain = MathF.Max(0.0f, _voxelsNode.ReassemblePullGain);
                var velocityDamping = Math.Clamp(_voxelsNode.ReassembleVelocityDamping, 0.0f, 0.999f);
                var rotationLerp = Math.Clamp(_voxelsNode.ReassembleRotationLerp, 0.0f, 1.0f);

                for (int i = 0; i < _bodies.Count; i++)
                {
                    var bakedBody = _bodies[i];
                    if (!physics.Simulation.Bodies.BodyExists(bakedBody.Handle))
                    {
                        continue;
                    }

                    var bodyReference = physics.Simulation.Bodies[bakedBody.Handle];
                    var pose = bodyReference.Pose;
                    var toTarget = bakedBody.TargetPosition - pose.Position;
                    var distance = toTarget.Length();

                    var velocity = bodyReference.Velocity;
                    var speed = velocity.Linear.Length();

                    if (distance <= settleDistance && speed <= settleVelocity)
                    {
                        pose.Position = bakedBody.TargetPosition;
                        pose.Orientation = Quaternion.Identity;
                        bodyReference.Pose = pose;

                        velocity.Linear = Vector3.Zero;
                        velocity.Angular = Vector3.Zero;
                        bodyReference.Velocity = velocity;
                        bodyReference.Awake = true;
                        continue;
                    }

                    allSettled = false;

                    var pull = toTarget * pullGain;
                    velocity.Linear = velocity.Linear * velocityDamping + pull * (1.0f - velocityDamping);
                    velocity.Angular *= 0.5f;
                    bodyReference.Velocity = velocity;

                    pose.Orientation = Quaternion.Slerp(pose.Orientation, Quaternion.Identity, rotationLerp);
                    bodyReference.Pose = pose;
                    bodyReference.Awake = true;
                }

                return allSettled;
            }
        }

        private sealed class LockPreviewMeshUpdater : ManaComponent
        {
            private readonly ManaVoxelsNode _voxelsNode;
            private readonly List<BakedVoxelBody> _bodies;
            private readonly ManaMesh _mesh;
            private readonly Vector3[] _positions;
            private readonly Vector3[] _normals;
            private readonly Vector4[] _colors;

            public LockPreviewMeshUpdater(ManaSceneNode owner, List<BakedVoxelBody> bodies, ManaMesh mesh) : base(owner)
            {
                _voxelsNode = (ManaVoxelsNode)owner;
                _bodies = bodies;
                _mesh = mesh;

                var vertexCount = _bodies.Count * 24;
                _positions = new Vector3[vertexCount];
                _normals = new Vector3[vertexCount];
                _colors = new Vector4[vertexCount];

                for (var i = 0; i < vertexCount; i++)
                {
                    _normals[i] = Vector3.UnitY;
                    _colors[i] = new Vector4(1, 1, 1, 0);
                }
            }

            public override void UpdateComponent()
            {
                if (!_voxelsNode.IsLockPreviewEnabled)
                {
                    ClearUnusedSlots(0);
                    UpdateMeshGeometry();
                    return;
                }

                var previewVoxels = _voxelsNode.BuildLockPreviewVoxels();
                var previewCount = previewVoxels.Count;

                for (var voxelIndex = 0; voxelIndex < previewCount; voxelIndex++)
                {
                    WritePreviewCube(voxelIndex, previewVoxels[voxelIndex]);
                }

                ClearUnusedSlots(previewCount);
                UpdateMeshGeometry();
            }

            private void UpdateMeshGeometry()
            {
                var currentData = _mesh.GetOrCreateCpuData();
                _mesh.SetCpuData(new ManaCore.Graphics.ManaMeshCpuData
                {
                    Indexes = currentData.Indexes,
                    Positions = _positions,
                    Normals = _normals,
                    Colors = _colors,
                    TextureCoordinates0 = currentData.TextureCoordinates0,
                    TextureCoordinates1 = currentData.TextureCoordinates1,
                    AlbedoTexturePath = currentData.AlbedoTexturePath,
                    AlbedoFactor = currentData.AlbedoFactor
                });

                _mesh.ImmediateGpuMesh?.UpdateDynamicData(_positions, _normals, _colors);
            }

            private void WritePreviewCube(int voxelIndex, ManaVoxelData voxel)
            {
                var writeOffset = voxelIndex * 24;
                var previewColor = new Vector4(voxel.Color.X, voxel.Color.Y, voxel.Color.Z, 0.55f);

                for (var face = 0; face < 6; face++)
                {
                    var scale = ManaDynamicGeometryContainer.CubeFaceScales[face];
                    var cR = Vector3.Transform(Vector3.One * 0.515f, scale);
                    var combinedRotation = ManaDynamicGeometryContainer.CubeFaceRotations[face];
                    var faceOffset = writeOffset + face * 4;

                    _positions[faceOffset + 0] = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), combinedRotation) + voxel.Position;
                    _positions[faceOffset + 1] = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), combinedRotation) + voxel.Position;
                    _positions[faceOffset + 2] = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), combinedRotation) + voxel.Position;
                    _positions[faceOffset + 3] = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), combinedRotation) + voxel.Position;

                    var faceCenter = (_positions[faceOffset + 0] + _positions[faceOffset + 1] + _positions[faceOffset + 2] + _positions[faceOffset + 3]) * 0.25f;
                    var normal = new Vector3(0, 0, 1).TransformNormal(combinedRotation);
                    if (Vector3.Dot(faceCenter - voxel.Position, normal) < 0.0f)
                    {
                        normal = -normal;
                    }
                    normal = Vector3.Normalize(normal);
                    _normals[faceOffset + 0] = normal;
                    _normals[faceOffset + 1] = normal;
                    _normals[faceOffset + 2] = normal;
                    _normals[faceOffset + 3] = normal;

                    _colors[faceOffset + 0] = previewColor;
                    _colors[faceOffset + 1] = previewColor;
                    _colors[faceOffset + 2] = previewColor;
                    _colors[faceOffset + 3] = previewColor;
                }
            }

            private void ClearUnusedSlots(int usedVoxelCount)
            {
                for (var voxelIndex = usedVoxelCount; voxelIndex < _bodies.Count; voxelIndex++)
                {
                    var writeOffset = voxelIndex * 24;
                    for (var i = 0; i < 24; i++)
                    {
                        _positions[writeOffset + i] = Vector3.Zero;
                        _normals[writeOffset + i] = Vector3.UnitY;
                        _colors[writeOffset + i] = new Vector4(1, 1, 1, 0);
                    }
                }
            }
        }
    }
}
