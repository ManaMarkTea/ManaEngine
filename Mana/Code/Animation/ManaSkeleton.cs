using Mana.Code.Nodes;

namespace Mana.Code.Animation
{
    // A bone is just a scene node plus the inverse bind matrix that takes a vertex from the mesh's
    // bind-pose space into that node's local space. Keeping the bone hierarchy as ordinary
    // ManaSceneNodes (rather than a parallel skeleton tree) means the existing transform propagation,
    // outliner and inspector all work on rigs for free.
    public sealed class ManaBone
    {
        public required string Name { get; init; }
        public required ManaSceneNode Node { get; init; }
        public required Matrix4x4 OffsetMatrix { get; init; }

        // Local transform the importer gave this node, used as the fallback for animation channels
        // that omit a track (the Kenney "idle" clip, for example, only ships rotation keys).
        public Vector3 BindPosition { get; init; }
        public Quaternion BindRotation { get; init; } = Quaternion.Identity;
        public Vector3 BindScale { get; init; } = Vector3.One;
    }

    public sealed class ManaSkeleton : ManaObject
    {
        private readonly Dictionary<string, int> indexByName = new(StringComparer.Ordinal);
        private readonly List<ManaBone> bones = new();

        // bindSpaceToWorld[i] == OffsetMatrix[i] * Node[i].WorldTransform, i.e. mesh bind space to
        // world. Rebuilt once per frame for the whole skeleton and shared by every mesh bound to it --
        // a Kenney character has two skinned meshes (body + head) over the same seven joints.
        private Matrix4x4[] bindSpaceToWorld = Array.Empty<Matrix4x4>();
        private bool hasBindSpaceToWorld;

        public ManaSkeleton(ManaContext context) : base(context)
        {
        }

        public IReadOnlyList<ManaBone> Bones => bones;
        public int BoneCount => bones.Count;

        public int AddBone(ManaBone bone)
        {
            var index = bones.Count;
            bones.Add(bone);
            indexByName.TryAdd(bone.Name, index);

            if (bindSpaceToWorld.Length < bones.Count)
            {
                Array.Resize(ref bindSpaceToWorld, bones.Count);
            }

            return index;
        }

        public bool TryGetBoneIndex(string name, out int index)
        {
            return indexByName.TryGetValue(name, out index);
        }

        public ManaBone? FindBone(string name)
        {
            return TryGetBoneIndex(name, out var index) ? bones[index] : null;
        }

        // Call after the node tree's world transforms have been refreshed for the frame. Returns false
        // when nothing moved, which lets callers skip re-skinning -- a paused or unanimated rig would
        // otherwise re-upload identical vertex buffers every single frame.
        public bool UpdateBindSpaceToWorld()
        {
            if (bindSpaceToWorld.Length < bones.Count)
            {
                Array.Resize(ref bindSpaceToWorld, bones.Count);
                hasBindSpaceToWorld = false;
            }

            var changed = !hasBindSpaceToWorld;
            for (var i = 0; i < bones.Count; i++)
            {
                var matrix = bones[i].OffsetMatrix * bones[i].Node.WorldTransform;
                if (!changed && !bindSpaceToWorld[i].Equals(matrix))
                {
                    changed = true;
                }

                bindSpaceToWorld[i] = matrix;
            }

            hasBindSpaceToWorld = true;
            return changed;
        }

        // Fills destination with the matrices that skin a vertex from bind space straight into the
        // space of meshWorldTransform (the mesh node's own world matrix, which the renderer applies
        // again downstream -- hence the inverse).
        public void GetSkinMatrices(Matrix4x4 meshWorldTransform, Matrix4x4[] destination)
        {
            if (!Matrix4x4.Invert(meshWorldTransform, out var inverseMeshWorld))
            {
                inverseMeshWorld = Matrix4x4.Identity;
            }

            var count = Math.Min(bones.Count, destination.Length);
            for (var i = 0; i < count; i++)
            {
                destination[i] = bindSpaceToWorld[i] * inverseMeshWorld;
            }
        }
    }
}
