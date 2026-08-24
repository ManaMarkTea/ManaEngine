using System.IO.Compression;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Geo;
using ManaGeoTools.Code.GeoTiff;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Shapefile;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Loaders;
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

        public PlanetNode? Planet { get; set; }

        public ManaWaterNode? Water { get; set; }

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
        private static readonly object WaterMaskLock = new();
        private static bool _waterMaskLoadAttempted;

        /// <summary>
        /// Loads <paramref name="level"/> into <paramref name="scene"/>. By default this replaces the world's
        /// authored content; callers can opt out when composing separate level-backed scenes.
        /// </summary>
        public static ManaLevelLoadResult Load(
            ManaLevel level,
            ManaContext context,
            ManaScene scene,
            bool replaceAuthoredNodes = true)
        {
            var result = new ManaLevelLoadResult();
            var world = context.World;

            // The world's existing voxel node is kept and refilled rather than rebuilt -- see the class
            // comment. A level with several voxel layers gets fresh nodes for the rest.
            var reusableVoxelsNode = replaceAuthoredNodes ? FindAuthoredVoxelsNode(world) : null;
            if (replaceAuthoredNodes)
            {
                RemoveAuthoredNodes(context, keep: reusableVoxelsNode);
            }

            // Cleared even when the level has no voxels at all: opening an empty level should not leave the
            // previous one's model standing in the new one.
            reusableVoxelsNode?.VoxelShape.ClearVoxels(logStats: false);

            var state = new LoadState(context, scene, result, reusableVoxelsNode);

            foreach (var entry in level.Nodes)
            {
                TryLoadNode(entry, state, parent: null);
            }

            // An untouched reusable node still needs committing: it was cleared above, and without this the
            // previous level's geometry stays on screen with no voxels behind it.
            if (state.ReusableVoxelsNode != null)
            {
                state.VoxelNodes.Add(state.ReusableVoxelsNode);
            }

            foreach (var voxelNode in state.VoxelNodes)
            {
                voxelNode.VoxelShape.CommitData();
                if (!result.Nodes.Contains(voxelNode))
                {
                    result.Nodes.Add(voxelNode);
                }
            }

            result.VoxelsNode = state.VoxelNodes.FirstOrDefault();

            if (level.Camera != null && world.CameraNode != null)
            {
                world.CameraNode.Position = ToVector3(level.Camera.Position);
                world.CameraNode.Rotation = ToVector3(level.Camera.Rotation);
                world.CameraNode.UpdateTransform();
            }

            return result;
        }

        private sealed class LoadState
        {
            public LoadState(
                ManaContext context,
                ManaScene scene,
                ManaLevelLoadResult result,
                ManaVoxelsNode? reusableVoxelsNode)
            {
                Context = context;
                Scene = scene;
                Result = result;
                ReusableVoxelsNode = reusableVoxelsNode;
            }

            public ManaContext Context { get; }

            public ManaScene Scene { get; }

            public ManaLevelLoadResult Result { get; }

            public ManaVoxelsNode? ReusableVoxelsNode { get; set; }

            public List<ManaVoxelsNode> VoxelNodes { get; } = new();
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

        private static ManaSceneNode? TryLoadNode(ManaLevelNode entry, LoadState state, ManaSceneNode? parent)
        {
            try
            {
                var node = LoadNode(entry, state, parent);
                if (node == null)
                {
                    return null;
                }

                state.Result.Nodes.Add(node);
                if (node is ManaPlayerStartNode start)
                {
                    state.Result.PlayerStart = start;
                }

                if (node is PlanetNode planet)
                {
                    state.Result.Planet = planet;
                }

                if (node is ManaWaterNode water)
                {
                    state.Result.Water = water;
                }

                return node;
            }
            catch (Exception exception)
            {
                state.Result.Warnings.Add($"'{entry.Name}' ({entry.Kind}) could not be loaded: {exception.Message}");
                return null;
            }
        }

        private static ManaSceneNode? LoadNode(ManaLevelNode entry, LoadState state, ManaSceneNode? parent)
        {
            switch (entry.Kind)
            {
                case ManaLevelSourceKind.Group:
                    return LoadGroup(entry, state, parent);

                case ManaLevelSourceKind.Shape:
                    var shape = LoadShape(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, shape);
                    AddCollisionIfPresent(entry, state.Context, shape);
                    AddScriptsIfPresent(entry, shape);
                    return shape;

                case ManaLevelSourceKind.Mesh:
                    var mesh = LoadMesh(entry, state.Context);
                    AttachToParent(state.Context, parent, mesh);
                    AddCollisionIfPresent(entry, state.Context, mesh);
                    AddScriptsIfPresent(entry, mesh);
                    return mesh;

                case ManaLevelSourceKind.Voxels:
                    var voxels = state.ReusableVoxelsNode ?? CreateVoxelsNode(state.Context, state.Scene, entry);
                    state.ReusableVoxelsNode = null;
                    LoadVoxels(entry, voxels);
                    AttachToParent(state.Context, parent, voxels);
                    AddScriptsIfPresent(entry, voxels);
                    state.VoxelNodes.Add(voxels);
                    return voxels;

                case ManaLevelSourceKind.PlayerStart:
                    var start = new ManaPlayerStartNode(state.Context, ToVector3(entry.Position));
                    start.Name = string.IsNullOrWhiteSpace(entry.Name) ? start.Name : entry.Name;
                    start.Rotation = ToVector3(entry.Rotation);
                    start.UpdateTransform();
                    lock (state.Context.World.SceneLock)
                    {
                        state.Scene.Children.Add(start);
                    }

                    AttachToParent(state.Context, parent, start);
                    AddScriptsIfPresent(entry, start);
                    return start;

                case ManaLevelSourceKind.Planet:
                    var planet = LoadPlanet(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, planet);
                    AddScriptsIfPresent(entry, planet);
                    return planet;

                case ManaLevelSourceKind.Water:
                    var water = LoadWater(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, water);
                    AddScriptsIfPresent(entry, water);
                    return water;

                default:
                    return null;
            }
        }

        private static ManaSceneNode LoadGroup(ManaLevelNode entry, LoadState state, ManaSceneNode? parent)
        {
            var group = new ManaSceneNode(state.Context, Matrix4x4.Identity)
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Group" : entry.Name,
                LevelSource = ManaLevelSource.Group
            };

            ApplyTransform(group, entry);
            lock (state.Context.World.SceneLock)
            {
                state.Scene.Children.Add(group);
            }

            AttachToParent(state.Context, parent, group);
            AddScriptsIfPresent(entry, group);

            if (entry.Children != null)
            {
                foreach (var child in entry.Children)
                {
                    TryLoadNode(child, state, group);
                }
            }

            return group;
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
                    "Wedge" => shapes.AddWedge(position, color, size),
                    "Disk" => shapes.AddDisk(position, color, size),
                    "Torus" => shapes.AddTorus(position, color, size),
                    "Icosahedron" => shapes.AddIcosahedron(position, color, size),
                    "Icosphere" => shapes.AddIsoSphere(position, color, size),
                    "Quad" => shapes.AddQuad(position, color, size),
                    _ => throw new InvalidDataException($"Unknown shape '{entry.Shape}'.")
                };
            }

            node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
            ApplyTransform(node, entry);
            return node;
        }

        private static ManaSceneNode LoadMesh(ManaLevelNode entry, ManaContext context)
        {
            var asset = entry.Asset ?? throw new InvalidDataException("Mesh entry has no asset reference.");
            var node = LoadMeshAsset(asset, context.World);
            node.LevelSource = ManaLevelSource.ForMesh(asset);
            node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
            ApplyTransform(node, entry);
            return node;
        }

        private static PlanetNode LoadPlanet(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            EnsureWaterMask();
            var settings = entry.Planet ?? new ManaLevelPlanet();
            var planet = new PlanetNode(
                context,
                Math.Max(1, settings.PatchesPerFaceEdge),
                MathF.Max(0.01f, settings.Radius),
                Math.Max(2, settings.Resolution),
                MathF.Max(0.0f, settings.HeightExaggeration),
                Math.Max(2, settings.OceanResolution))
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Planet" : entry.Name,
                AutoLodEnabled = settings.AutoLodEnabled,
                NearOceanEnabled = settings.NearOceanEnabled,
                LevelSource = ManaLevelSource.ForPlanet(entry.Asset)
            };

            ApplyTransform(planet, entry);
            lock (context.World.SceneLock)
            {
                scene.Children.Add(planet);
            }

            if (!string.IsNullOrWhiteSpace(entry.Asset))
            {
                var baselinePath = ResolveAssetPath(entry.Asset);
                if (File.Exists(baselinePath))
                {
                    planet.LoadHeightDataAsync(new GeoTiffLibrary(Path.GetDirectoryName(baselinePath)!));
                }
            }

            return planet;
        }

        private static ManaWaterNode LoadWater(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            var settings = entry.Water ?? new ManaLevelWater();
            var width = settings.Size.Length > 0 ? settings.Size[0] : 16.0f;
            var depth = settings.Size.Length > 1 ? settings.Size[1] : width;
            var water = new ManaWaterNode(
                context,
                new Vector2(MathF.Max(0.1f, width), MathF.Max(0.1f, depth)),
                Math.Clamp(settings.Resolution, 1, 512))
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Water" : entry.Name,
                LevelSource = ManaLevelSource.ForWater(
                    new Vector2(MathF.Max(0.1f, width), MathF.Max(0.1f, depth)),
                    Math.Clamp(settings.Resolution, 1, 512))
            };

            ApplyTransform(water, entry);
            lock (context.World.SceneLock)
            {
                scene.Children.Add(water);
            }

            return water;
        }

        private static void AttachToParent(ManaContext context, ManaSceneNode? parent, ManaSceneNode node)
        {
            if (parent == null)
            {
                node.UpdateTransform();
                return;
            }

            lock (context.World.SceneLock)
            {
                for (var i = context.World.Scenes.Count - 1; i >= 0; i--)
                {
                    var scene = context.World.Scenes[i];
                    if (!scene.Children.Remove(node))
                    {
                        continue;
                    }

                    if (!ReferenceEquals(scene, context.World.DefaultScene) && scene.Children.Count == 0)
                    {
                        context.World.Scenes.RemoveAt(i);
                    }
                }

                parent.AddChild(node);
            }

            node.UpdateTransform();
        }

        private static void AddCollisionIfPresent(ManaLevelNode entry, ManaContext context, ManaSceneNode node)
        {
            if (entry.Collision is { } collision)
            {
                if (collision.UseVisualHull
                    && ManaCollisionBounds.TryGetVisualHullPoints(node, out var visualPoints))
                {
                    context.Physics.AddConvexHullBody(
                        node,
                        visualPoints,
                        collision.Offset != null ? ToVector3(collision.Offset) : Vector3.Zero);
                    return;
                }

                if (collision.UseVisualBounds
                    && ManaCollisionBounds.TryGetVisualBox(node, out var visualSize, out var visualOffset))
                {
                    var offset = collision.Offset != null ? ToVector3(collision.Offset) : visualOffset;
                    context.Physics.AddBoxBody(node, visualSize, offset);
                    return;
                }

                if (collision.Size != null)
                {
                    context.Physics.AddBoxBody(node, ToVector3(collision.Size), ToVector3(collision.Offset));
                }
            }
        }

        private static void AddScriptsIfPresent(ManaLevelNode entry, ManaSceneNode node)
        {
            if (entry.Scripts == null)
            {
                return;
            }

            foreach (var script in entry.Scripts)
            {
                ManaLevelComponentScripts.AddToNode(node, script);
            }
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
                return world.LoadFromGltfMemory(memory.ToArray(), entryPath, sidecarResolver);
            }

            assetReference = ResolveAssetPath(assetReference);

            if (!File.Exists(assetReference))
            {
                throw new FileNotFoundException($"Mesh asset '{assetReference}' is missing.", assetReference);
            }

            return world.LoadFromGltf(assetReference);
        }

        public static string ResolveAssetPath(string assetReference)
        {
            if (Path.IsPathRooted(assetReference))
            {
                return Path.GetFullPath(assetReference);
            }

            var normalized = assetReference.Replace('/', Path.DirectorySeparatorChar);
            string[] roots =
            {
                Environment.CurrentDirectory,
                Path.Combine(Environment.CurrentDirectory, "Mana"),
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana")
            };

            foreach (var root in roots)
            {
                var candidate = Path.GetFullPath(Path.Combine(root, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
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
            node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
            node.Position = ToVector3(entry.Position);
            node.Rotation = ToVector3(entry.Rotation);
            node.Scale = ToVector3(entry.Scale);
            node.LevelSource = ManaLevelSource.ForVoxels(entry.Asset);
            node.UpdateTransform();

            IReadOnlyList<ManaVoxelData> voxels;
            if (entry.Voxels != null)
            {
                voxels = ManaLevelVoxelCodec.Decode(entry.Voxels);
            }
            else if (!string.IsNullOrWhiteSpace(entry.Asset))
            {
                var loader = new MagicaVoxLoader();
                loader.Load(ResolveAssetPath(entry.Asset));
                voxels = loader.Voxels
                    .Select(voxel => new ManaVoxelData
                    {
                        Position = voxel.Position + Vector3.One * 0.5f,
                        Color = voxel.Color
                    })
                    .ToArray();
            }
            else
            {
                return;
            }

            // AddVoxel rather than ReplaceCommittedVoxels, so each voxel gets its kinematic collider the
            // same way one placed by hand does. CommitData is left to the caller, which does it once for
            // the whole level rather than once per entry.
            foreach (var voxel in voxels)
            {
                node.VoxelShape.AddVoxel(voxel.Position, voxel.Color);
            }
        }

        private static void EnsureWaterMask()
        {
            lock (WaterMaskLock)
            {
                if (_waterMaskLoadAttempted)
                {
                    return;
                }

                _waterMaskLoadAttempted = true;
                var mask = new GeoWaterMask();
                var lakesPath = ResolveAssetPath(Path.Combine("Data", "ne_10m_lakes.zip"));
                var oceanPath = ResolveAssetPath(Path.Combine("Data", "ne_10m_ocean.zip"));
                mask.AddLakes(GeoPolygonSet.TryLoadFromZip(lakesPath, "ne_10m_lakes.shp"));
                mask.Add(GeoPolygonSet.TryLoadFromZip(oceanPath, "ne_10m_ocean.shp"));
                GeoWaterMask.Shared = mask;
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
