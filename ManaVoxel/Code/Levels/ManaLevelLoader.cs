using System.IO.Compression;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Services;

namespace ManaVoxel.Code.Levels
{
    /// <summary>What loading a level produced, so the host can re-attach the pieces it holds references to.</summary>
    public sealed class ManaLevelLoadResult
    {
        public List<ManaSceneNode> Nodes { get; } = new();

        /// <summary>The voxel node the level's grid went into, whether it was reused or freshly made.</summary>
        public ManaVoxelsNode? VoxelsNode { get; set; }

        public ManaPlayerStartNode? PlayerStart { get; set; }

        /// <summary>Non-fatal problems -- a missing mesh file, an unknown shape name -- surfaced to the user.</summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Rebuilds a world's authored content from a <see cref="ManaLevel"/>.
    /// <para>
    /// The voxel node is reused rather than replaced when the world already has one. That is not just an
    /// optimisation: the editor's view model, the voxel tools and every kinematic collider in the physics
    /// world all hold onto that one node, and swapping it out from under them would leave the palette
    /// editing a shape nobody can see. Clearing and refilling the node it already has keeps all of those
    /// pointing at the thing on screen.
    /// </para>
    /// </summary>
    public static class ManaLevelLoader
    {
        /// <summary>
        /// Replaces the world's authored content with <paramref name="level"/>. Scenery the game mode built
        /// in code is left alone -- see <see cref="ManaLevelSource"/> for where that line is drawn.
        /// </summary>
        public static ManaLevelLoadResult Load(ManaLevel level, ManaContext context, ManaScene scene)
        {
            var result = new ManaLevelLoadResult();
            var world = context.World;

            // The world's existing voxel node is kept and refilled rather than rebuilt -- see the class
            // comment. A level with several voxel layers gets fresh nodes for the rest.
            var reusableVoxelsNode = FindAuthoredVoxelsNode(world);
            RemoveAuthoredNodes(context, keep: reusableVoxelsNode);

            // Cleared even when the level has no voxels at all: opening an empty level should not leave the
            // previous one's model standing in the new one.
            reusableVoxelsNode?.VoxelShape.ClearVoxels(logStats: false);

            var voxelNodes = new List<ManaVoxelsNode>();

            foreach (var entry in level.Nodes)
            {
                try
                {
                    if (entry.Kind == ManaLevelSourceKind.Voxels)
                    {
                        var target = reusableVoxelsNode ?? CreateVoxelsNode(context, scene, entry);
                        reusableVoxelsNode = null;
                        LoadVoxels(entry, target);
                        voxelNodes.Add(target);
                        continue;
                    }

                    var node = LoadNode(entry, context, scene);
                    if (node == null)
                    {
                        continue;
                    }

                    result.Nodes.Add(node);
                    if (node is ManaPlayerStartNode start)
                    {
                        result.PlayerStart = start;
                    }
                }
                catch (Exception exception)
                {
                    result.Warnings.Add($"'{entry.Name}' ({entry.Kind}) could not be loaded: {exception.Message}");
                }
            }

            // An untouched reusable node still needs committing: it was cleared above, and without this the
            // previous level's geometry stays on screen with no voxels behind it.
            if (reusableVoxelsNode != null)
            {
                voxelNodes.Add(reusableVoxelsNode);
            }

            foreach (var voxelNode in voxelNodes)
            {
                voxelNode.VoxelShape.CommitData();
                result.Nodes.Add(voxelNode);
            }

            result.VoxelsNode = voxelNodes.FirstOrDefault();

            if (level.Camera != null && world.CameraNode != null)
            {
                world.CameraNode.Position = ToVector3(level.Camera.Position);
                world.CameraNode.Rotation = ToVector3(level.Camera.Rotation);
                world.CameraNode.UpdateTransform();
            }

            return result;
        }

        /// <summary>
        /// Strips every authored node from the world, physics bodies included, leaving code-built scenery
        /// in place. This is also "New Level" on its own.
        /// </summary>
        public static void RemoveAuthoredNodes(ManaContext context, ManaSceneNode? keep = null)
        {
            var world = context.World;
            var doomed = new List<(ManaScene Scene, ManaSceneNode Node)>();

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectAuthored(scene, child, keep, doomed);
                    }
                }

                foreach (var (scene, node) in doomed)
                {
                    RemoveBodies(context, node);
                    node.Parent?.RemoveChild(node);
                    scene.Children.Remove(node);
                }
            }
        }

        private static void CollectAuthored(
            ManaScene scene,
            ManaSceneNode node,
            ManaSceneNode? keep,
            List<(ManaScene, ManaSceneNode)> output)
        {
            if (node.LevelSource != null)
            {
                if (!ReferenceEquals(node, keep))
                {
                    output.Add((scene, node));
                }

                return;
            }

            foreach (var child in node.Children)
            {
                CollectAuthored(scene, child, keep, output);
            }
        }

        private static void RemoveBodies(ManaContext context, ManaSceneNode node)
        {
            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                context.Physics.RemoveBody(collision.Body);
            }

            foreach (var child in node.Children)
            {
                RemoveBodies(context, child);
            }
        }

        private static ManaVoxelsNode? FindAuthoredVoxelsNode(ManaWorld world)
        {
            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        if (child is ManaVoxelsNode voxels && voxels.LevelSource?.Kind == ManaLevelSourceKind.Voxels)
                        {
                            return voxels;
                        }
                    }
                }
            }

            return null;
        }

        private static ManaSceneNode? LoadNode(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            switch (entry.Kind)
            {
                case ManaLevelSourceKind.Shape:
                    return LoadShape(entry, context, scene);

                case ManaLevelSourceKind.Mesh:
                    return LoadMesh(entry, context);

                case ManaLevelSourceKind.PlayerStart:
                    var start = new ManaPlayerStartNode(context, ToVector3(entry.Position));
                    start.Name = string.IsNullOrWhiteSpace(entry.Name) ? start.Name : entry.Name;
                    start.Rotation = ToVector3(entry.Rotation);
                    start.UpdateTransform();
                    lock (context.World.SceneLock)
                    {
                        scene.Children.Add(start);
                    }

                    return start;

                default:
                    return null;
            }
        }

        private static ManaSceneNode LoadShape(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            var shapes = new ManaShapeFactory(context, scene);
            var position = ToVector3(entry.Position);
            var color = ToVector4(entry.Color);
            var size = entry.Size != null ? ToVector3(entry.Size) : Vector3.One;

            ManaSceneNode node;
            lock (context.World.SceneLock)
            {
                node = entry.Shape switch
                {
                    "Cube" => shapes.AddCube(position, color, size),
                    "Sphere" => shapes.AddSphere(position, color, size),
                    "Pyramid" => shapes.AddPyramid(position, color, size),
                    "Torus" => shapes.AddTorus(position, color, size),
                    "Icosahedron" => shapes.AddIcosahedron(position, color, size),
                    "Icosphere" => shapes.AddIsoSphere(position, color, size),
                    "Quad" => shapes.AddQuad(position, color, size),
                    _ => throw new InvalidDataException($"Unknown shape '{entry.Shape}'.")
                };
            }

            ApplyTransform(node, entry);

            if (entry.Collision is { } collision)
            {
                context.Physics.AddBoxBody(node, ToVector3(collision.Size), ToVector3(collision.Offset));
            }

            return node;
        }

        private static ManaSceneNode LoadMesh(ManaLevelNode entry, ManaContext context)
        {
            var asset = entry.Asset ?? throw new InvalidDataException("Mesh entry has no asset reference.");
            var node = LoadMeshAsset(asset, context.World);
            node.LevelSource = ManaLevelSource.ForMesh(asset);
            node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
            ApplyTransform(node, entry);

            if (entry.Collision is { } collision)
            {
                context.Physics.AddBoxBody(node, ToVector3(collision.Size), ToVector3(collision.Offset));
            }

            return node;
        }

        /// <summary>
        /// Loads a mesh from the same asset reference the picker uses -- a plain path, or a zip plus the
        /// entry inside it. Shared with the editor's own placement path rather than re-decoded here; two
        /// copies of that encoding is what broke mesh import once already.
        /// </summary>
        public static ManaSceneNode LoadMeshAsset(string assetReference, ManaWorld world)
        {
            if (MeshAssetLibrary.TryDecodeZip(assetReference, out var zipPath, out var entryPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryPath)
                    ?? throw new FileNotFoundException($"Entry '{entryPath}' is no longer inside '{zipPath}'.", zipPath);

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);

                var sidecarResolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entryPath);
                return world.LoadFromGltfMemory(memory.ToArray(), entry.Name, sidecarResolver);
            }

            if (!File.Exists(assetReference))
            {
                throw new FileNotFoundException($"Mesh asset '{assetReference}' is missing.", assetReference);
            }

            return world.LoadFromGltf(assetReference);
        }

        private static ManaVoxelsNode CreateVoxelsNode(ManaContext context, ManaScene scene, ManaLevelNode entry)
        {
            lock (context.World.SceneLock)
            {
                var shapes = new ManaShapeFactory(context, scene);
                var node = (ManaVoxelsNode)shapes.AddVoxels(ToVector3(entry.Position));
                node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
                return node;
            }
        }

        private static void LoadVoxels(ManaLevelNode entry, ManaVoxelsNode node)
        {
            if (entry.Voxels == null)
            {
                return;
            }

            node.Position = ToVector3(entry.Position);
            node.Rotation = ToVector3(entry.Rotation);
            node.Scale = ToVector3(entry.Scale);
            node.UpdateTransform();

            // AddVoxel rather than ReplaceCommittedVoxels, so each voxel gets its kinematic collider the
            // same way one placed by hand does. CommitData is left to the caller, which does it once for
            // the whole level rather than once per entry.
            foreach (var voxel in ManaLevelVoxelCodec.Decode(entry.Voxels))
            {
                node.VoxelShape.AddVoxel(voxel.Position, voxel.Color);
            }
        }

        private static void ApplyTransform(ManaSceneNode node, ManaLevelNode entry)
        {
            node.Position = ToVector3(entry.Position);
            node.Rotation = ToVector3(entry.Rotation);
            node.Scale = ToVector3(entry.Scale);
            node.UpdateTransform();
        }

        private static Vector3 ToVector3(float[]? value) =>
            value is { Length: >= 3 } ? new Vector3(value[0], value[1], value[2]) : Vector3.Zero;

        private static Vector4 ToVector4(float[]? value) =>
            value is { Length: >= 4 } ? new Vector4(value[0], value[1], value[2], value[3]) : Vector4.One;
    }
}
