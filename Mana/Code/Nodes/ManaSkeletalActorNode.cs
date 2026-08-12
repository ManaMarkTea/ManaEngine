using Mana.Code.Animation;
using ManaCore.Graphics;

namespace Mana.Code.Nodes
{
    // Root of an imported rigged model. Exists to own the per-frame ordering that skinning needs and
    // that the plain component tick can't express: a component on a bone node would run after that
    // node's world transform was already computed, and a component on the root would run after the
    // whole subtree had. So the actor drives all three phases itself.
    public class ManaSkeletalActorNode : ManaSceneNode
    {
        private readonly List<ManaSkinnedMeshComponent> skinnedMeshes = new();

        public ManaSkeletalAnimationComponent Animation { get; }
        public ManaSkeleton? Skeleton { get; set; }
        public IReadOnlyList<ManaSkinnedMeshComponent> SkinnedMeshes => skinnedMeshes;

        // Skinning rewrites vertex buffers every frame the pose changes; off by default the actor is
        // a plain static model frozen in its bind pose.
        public bool SkinningEnabled { get; set; } = true;

        public ManaSkeletalActorNode(ManaContext context, Matrix4x4 localTransform) : base(context, localTransform)
        {
            Animation = new ManaSkeletalAnimationComponent(this);
            Components.Add(Animation);
        }

        public void RegisterSkinnedMesh(ManaSkinnedMeshComponent skinnedMesh)
        {
            skinnedMeshes.Add(skinnedMesh);
        }

        // True for a rig that animates by moving whole meshes around the node tree rather than by
        // deforming vertices -- the Kenney blocky characters are built this way, one mesh per body
        // part parented to the node the animation drives. Such a rig has no skeleton and no skin
        // bindings, so anything that reads those has to have a path for it.
        public bool IsRigidRig => Skeleton == null || SkinnedMeshes.Count == 0;

        // Composes a descendant's local transforms up to (but not including) this actor. The actor's
        // own transform is excluded so the result is in the actor's own space, independent of where it
        // has been placed in the world.
        public Matrix4x4 GetNodeToActorTransform(ManaSceneNode node)
        {
            var result = Matrix4x4.Identity;
            for (var current = node; current != null && !ReferenceEquals(current, this); current = current.Parent)
            {
                result *= current.LocalTransform;
            }

            return result;
        }

        // Bounds of the model in this actor's own space, ignoring the actor's own transform. Uses the
        // untouched bind pose for a skinned rig so an in-progress animation cannot skew the result,
        // and the mesh geometry in place for a rigid one.
        public bool TryGetLocalBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);

            foreach (var skinnedMesh in skinnedMeshes)
            {
                var toActor = GetNodeToActorTransform(skinnedMesh.Owner);
                foreach (var bindPosition in skinnedMesh.Binding.BindPositions)
                {
                    var local = Vector3.Transform(bindPosition, toActor);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            if (skinnedMeshes.Count == 0)
            {
                AccumulateRigidBounds(this, ref min, ref max);
            }

            return min.X <= max.X;
        }

        private void AccumulateRigidBounds(ManaSceneNode node, ref Vector3 min, ref Vector3 max)
        {
            foreach (var renderer in node.GetComponents<MeshRendererComponent>())
            {
                if (renderer.Mesh == null)
                {
                    continue;
                }

                ManaMeshCpuData cpuData;
                try
                {
                    cpuData = renderer.Mesh.CpuData ?? renderer.Mesh.GetOrCreateCpuData();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                var toActor = GetNodeToActorTransform(node);
                foreach (var position in cpuData.Positions)
                {
                    var local = Vector3.Transform(position, toActor);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            foreach (var child in node.Children)
            {
                AccumulateRigidBounds(child, ref min, ref max);
            }
        }

        public override void UpdateTransform()
        {
            // 1. Pose the bones (writes bone LocalTransforms).
            Animation.Advance(ManaTime.GlobalTime.DeltaTime);

            // 2. Propagate to world space, and tick every component as usual.
            base.UpdateTransform();

            // 3. Skin against the world transforms step 2 just produced.
            if (!SkinningEnabled || Skeleton == null || skinnedMeshes.Count == 0)
            {
                return;
            }

            var poseChanged = Skeleton.UpdateBindSpaceToWorld();
            foreach (var skinnedMesh in skinnedMeshes)
            {
                skinnedMesh.ApplySkinning(poseChanged);
            }
        }
    }
}
