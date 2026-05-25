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

        private readonly ManaVoxelShape shape;
        private readonly MeshRendererComponent mergedVoxelRenderer;
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

        private List<BakedVoxelBody>? _bakedBodies;

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
                var spawnPos = voxel.Position + bakeSpawnOffset;
                var handle = Context.Physics.AddDynamicBoxBodyDirect(spawnPos, Vector3.One, massPerVoxel);
                _bakedBodies.Add(new BakedVoxelBody(handle, voxel.Color, spawnPos));
            }

            ExplodeBakedVoxels();

            // Build a single merged mesh from the initial spawn positions
            var geom = new ManaDynamicGeometryContainer();
            foreach (var bakedBody in _bakedBodies)
            {
                var pose = Context.Physics.Simulation.Bodies[bakedBody.Handle].Pose;
                geom.AddOrientedCube(pose.Position, pose.Orientation, Vector3.One, bakedBody.Color);
            }

            var bakedMesh = new ManaMesh(Context);
            var sharedMaterial = new ManaMaterial(Context);
            sharedMaterial.Albedo.Value = Colors.White;
            bakedMesh.Materials = new ManaMaterial[] { sharedMaterial };
            bakedMesh.VertexBufferObject = geom.CreateVertexBufferObject(Context, BufferUsageARB.DynamicDraw);

            // Add updater (runs first) then renderer — order matters
            Components.Add(new BakedVoxelsMeshUpdater(this, _bakedBodies, bakedMesh));
            var renderer = new MeshRendererComponent(this, bakedMesh);
            Components.Add(renderer);

            return shape.CommittedVoxels.Count;
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

            public override void DrawComponent()
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

                _mesh.VertexBufferObject?.UpdateDynamicData(
                    _positions,
                    _normals,
                    _colors);
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

                for (var face = 0; face < 6; face++)
                {
                    var scale = ManaDynamicGeometryContainer.CubeFaceScales[face];
                    var cR = Vector3.Transform(_cubeSize * 0.5f, scale);
                    var combinedRotation = ManaDynamicGeometryContainer.CubeFaceRotations[face] * bodyMatrix;

                    var faceOffset = writeOffset + face * 4;
                    _positions[faceOffset + 0] = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), combinedRotation) + pose.Position;
                    _positions[faceOffset + 1] = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), combinedRotation) + pose.Position;
                    _positions[faceOffset + 2] = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), combinedRotation) + pose.Position;
                    _positions[faceOffset + 3] = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), combinedRotation) + pose.Position;

                    var normal = new Vector3(0, 0, 1).TransformNormal(combinedRotation);
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
    }
}
