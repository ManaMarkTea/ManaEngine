using Silk.NET.Assimp;
using Mana.Code.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Nodes
{
    public class ManaVoxelsNode : ManaSceneNode
    {
        private readonly ManaVoxelShape shape;
        private readonly MeshRendererComponent mergedVoxelRenderer;
        public bool AutoCreateKinematicVoxelBodies { get; private set; } = true;
        public bool IsBakedToDynamicPhysics { get; private set; }

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

            var bakeSpawnOffset = new Vector3(0, 2.0f, 0);
            var random = Random.Shared;
            const float bakeImpulseStrength = 4.5f;

            foreach (var voxel in shape.CommittedVoxels)
            {
                var voxelNode = new ManaSceneNode(Context, Matrix4x4.CreateTranslation(voxel.Position + bakeSpawnOffset));
                voxelNode.Components.Add(new MeshRendererComponent(voxelNode, new ManaCube(Context, voxel.Color, Vector3.One)));
                AddChild(voxelNode);

                var randomDirection = new Vector3(
                    random.NextSingle() * 2.0f - 1.0f,
                    random.NextSingle() * 0.5f + 0.1f,
                    random.NextSingle() * 2.0f - 1.0f);
                if (randomDirection.LengthSquared() < 1e-5f)
                {
                    randomDirection = Vector3.UnitY;
                }
                else
                {
                    randomDirection = Vector3.Normalize(randomDirection);
                }

                Context.Physics.AddDynamicBoxBody(
                    voxelNode,
                    Vector3.One,
                    Vector3.Zero,
                    massPerVoxel,
                    randomDirection * bakeImpulseStrength);
            }

            return shape.CommittedVoxels.Count;
        }
    }
}
