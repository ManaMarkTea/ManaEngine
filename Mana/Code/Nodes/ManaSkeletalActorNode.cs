using Mana.Code.Animation;

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
