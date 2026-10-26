using System.Diagnostics.CodeAnalysis;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
#if !MANA_BROWSER
using ManaGeoTools.Code.Geo;
#endif
#if !MANA_BROWSER
using ManaGeoTools.Code.GeoTiff;
#endif
#if !MANA_BROWSER
using ManaGeoTools.Code.Planet;
#endif
#if !MANA_BROWSER
using ManaGeoTools.Code.Shapefile;
#endif
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
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

#if !MANA_BROWSER
        public PlanetNode? Planet { get; set; }
#endif

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
#if !MANA_BROWSER
        private static readonly object WaterMaskLock = new();
        private static bool _waterMaskLoadAttempted;
#endif

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
#if !MANA_BROWSER
            // What each instance looked like as loaded, for the editor to tell later edits from overrides. The
            // browser build only plays, and has no capture to do it with.
            foreach (var instance in result.Nodes.Where(n => n.PrefabLink?.BaselineJson != null && n.Children.Count == 1))
            {
                instance.PrefabLink!.AppliedJson = System.Text.Json.JsonSerializer.Serialize(
                    ManaLevelCapture.CaptureSubtree(instance.Children[0]), ManaLevel.SerializerOptions);
            }
#endif

            if (level.Sky != null)
            {
                level.Sky.ApplyTo(world.IrradianceSettings);
                scene.AuthoredSky = level.Sky;
                if (!string.IsNullOrWhiteSpace(level.Sky.Photo))
                {
                    world.EnvironmentReflectionSource = world.EnvironmentReflectionSource.Clone(level.Sky.Photo);
                }
            }

            scene.Publishing = level.Web?.ToPublishing() ?? new ManaScenePublishing();
            scene.GameModes.Clear();
            foreach (var entry in level.GameModes ?? new())
            {
                if (ManaLevelGameModes.Load(entry, result.Warnings) is { } mode)
                {
                    scene.GameModes.Add(mode);
                }
            }

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
        public static void RetirePlanetWork(ManaSceneNode node)
        {
#if !MANA_BROWSER
            if (node is PlanetNode planet)
            {
                planet.Retire();
            }
#endif
            foreach (var child in node.Children)
            {
                RetirePlanetWork(child);
            }
        }

        public static void RetirePlanetWork(ManaContext context)
        {
            lock (context.World.SceneLock)
            {
                foreach (var scene in context.World.Scenes)
                {
                    foreach (var node in scene.Children)
                    {
                        RetirePlanetWork(node);
                    }
                }
            }
        }

        public static void RemoveAuthoredNodes(ManaContext context, ManaSceneNode? keep = null)
        {
            context.Audio.StopAll();
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
                    RetirePlanetWork(node);
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
            foreach (var audio in node.Components.OfType<ManaAudioSourceComponent>()) { audio.Dispose(); }
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

                if (!string.IsNullOrWhiteSpace(entry.Id))
                {
                    node.AuthoredId = entry.Id;
                }
                if (parent != null)
                {
                    lock (state.Context.World.SceneLock)
                    {
                        state.Scene.Children.Remove(node);
                    }
                }
                if (entry.Kind is not (ManaLevelSourceKind.Group or ManaLevelSourceKind.Prefab))
                {
                    foreach (var child in entry.Children ?? [])
                    {
                        TryLoadNode(child, state, node);
                    }
                }

                state.Result.Nodes.Add(node);
                if (node is ManaPlayerStartNode start)
                {
                    state.Result.PlayerStart = start;
                }

#if !MANA_BROWSER
                if (node is PlanetNode planet)
                {
                    state.Result.Planet = planet;
                }
#endif

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

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        private static ManaSceneNode? LoadNode(ManaLevelNode entry, LoadState state, ManaSceneNode? parent)
        {
            switch (entry.Kind)
            {
                case ManaLevelSourceKind.Prefab:
                    var instance = LoadGroup(new ManaLevelNode
                    {
                        Name = entry.Name, Position = entry.Position, Rotation = entry.Rotation, Scale = entry.Scale, Scripts = entry.Scripts
                    }, state, parent);
                    instance.LevelSource = new ManaLevelSource { Kind = ManaLevelSourceKind.Prefab };
                    var reference = entry.Prefab ?? throw new InvalidDataException("Missing prefab reference.");
                    instance.PrefabLink = new ManaPrefabLink
                    {
                        AssetId = reference.AssetId, AssetPath = reference.AssetPath,
                        OverridesJson = System.Text.Json.JsonSerializer.Serialize(reference.Overrides, ManaLevel.SerializerOptions)
                    };
                    try
                    {
                        var contentRoot = ManaContentLibrary.RootFor(state.Context.World);
                        var path = ManaContentLibrary.ResolvePrefab(reference.AssetId, reference.AssetPath, contentRoot);
                        var prefab = ManaPrefab.Load(path, contentRoot);
                        instance.PrefabLink.AssetPath = ManaContentLibrary.Reference(path, contentRoot);
                        instance.PrefabLink.BaselineJson = System.Text.Json.JsonSerializer.Serialize(prefab.Root, ManaLevel.SerializerOptions);
                        var resolved = ManaPrefabOverrides.Resolve(prefab.Root, reference.Overrides, state.Result.Warnings);
                        new ManaPrefab { Root = resolved }.Validate();
                        if (TryLoadNode(resolved, state, instance) == null)
                        {
                            instance.PrefabLink.BaselineJson = null;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidOperationException)
                    {
                        instance.PrefabLink.BaselineJson = null;
                        state.Result.Warnings.Add($"Prefab '{entry.Name}': {exception.Message} The reference and overrides were preserved.");
                    }
                    return instance;

                case ManaLevelSourceKind.Group:
                    return LoadGroup(entry, state, parent);

                case ManaLevelSourceKind.Shape:
                    var shape = LoadShape(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, shape);
                    AddCollisionIfPresent(entry, state.Context, shape);
                    AddScriptsIfPresent(entry, shape);
                    return shape;

                case ManaLevelSourceKind.Mesh:
                    // A top-level mesh goes into the level's scene rather than a wrapper of its own. One under a
                    // parent is moved out of whatever scene it loads into straight away, so it keeps the old route.
                    var mesh = LoadMesh(entry, state.Context, parent == null ? state.Scene : null);
                    AttachToParent(state.Context, parent, mesh);
                    AddCollisionIfPresent(entry, state.Context, mesh);
                    AddScriptsIfPresent(entry, mesh);
                    ManaLevelMeshColliders.Apply(mesh, entry.ChildColliders, state.Result.Warnings);
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
                    if (entry.Character is { } character)
                    {
                        start.CharacterPack = character.Pack;
                        start.CharacterName = character.Name;
                        start.CharacterHeight = character.Height;
                    }

                    start.UpdateTransform();
                    lock (state.Context.World.SceneLock)
                    {
                        state.Scene.Children.Add(start);
                    }

                    AttachToParent(state.Context, parent, start);
                    AddScriptsIfPresent(entry, start);
                    return start;

#if !MANA_BROWSER
                case ManaLevelSourceKind.Planet:
                    var planet = LoadPlanet(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, planet);
                    AddScriptsIfPresent(entry, planet);
                    return planet;
#endif

                case ManaLevelSourceKind.Water:
                    var water = LoadWater(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, water);
                    AddScriptsIfPresent(entry, water);
                    return water;

                case ManaLevelSourceKind.GridMap:
                    var gridMap = LoadGridMap(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, gridMap);
                    AddScriptsIfPresent(entry, gridMap);
                    return gridMap;

                case ManaLevelSourceKind.Terrain:
                    var terrain = LoadTerrain(entry, state.Context, state.Scene);
                    AttachToParent(state.Context, parent, terrain);
                    AddScriptsIfPresent(entry, terrain);
                    return terrain;

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
            // A group can be solid too -- a sprite platform that moves stands the player on this box.
            AddCollisionIfPresent(entry, state.Context, group);
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
            if (entry.Material != null && node.Components.OfType<MeshRendererComponent>().FirstOrDefault() is { } renderer)
            {
                entry.Material.ApplyTo(renderer.Material);
            }

            return node;
        }

        private static ManaSceneNode LoadMesh(ManaLevelNode entry, ManaContext context, ManaScene? into)
        {
            var asset = entry.Asset ?? throw new InvalidDataException("Mesh entry has no asset reference.");
            var node = ManaMeshAssetLoader.For(context.World).Load(asset, into);
            node.LevelSource = ManaLevelSource.ForMesh(asset);
            if (entry.MeshMaterials != null)
            {
                var renderers = ManaLevelMaterial.MeshRenderers(node).ToList();
                foreach (var (index, surface) in entry.MeshMaterials)
                {
                    if (index >= 0 && index < renderers.Count)
                    {
                        surface.ApplyTo(renderers[index].DynamicMaterial);
                    }
                }
            }
            node.Name = string.IsNullOrWhiteSpace(entry.Name) ? node.Name : entry.Name;
            ApplyTransform(node, entry);
            return node;
        }

#if !MANA_BROWSER
        // Planet levels are backed by ManaGeoTools (GeoTIFF/shapefile terrain), which has no
        // browser-wasm build; this file is source-linked into ManaVoxel.Web (see its csproj). No
        // browser level uses a Planet node, so the whole path compiles out there rather than
        // dragging that project in.
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
                var baselinePath = ManaAssetPaths.Resolve(entry.Asset);
                if (File.Exists(baselinePath))
                {
                    planet.LoadHeightDataAsync(new GeoTiffLibrary(Path.GetDirectoryName(baselinePath)!));
                }
            }

            return planet;
        }
#endif

        private static ManaGridMapNode LoadGridMap(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            var gridMap = new ManaGridMapNode(context)
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Grid Map" : entry.Name
            };

            ApplyTransform(gridMap, entry);
            lock (context.World.SceneLock)
            {
                scene.Children.Add(gridMap);
            }

            // Tiles take their collider poses from the grid's world transform, so it has to be settled first.
            gridMap.UpdateTransform();
            if (entry.GridMap is { } settings)
            {
                gridMap.Load(
                    ManaLevelGridMapCodec.DecodeCellSize(settings),
                    settings.Palette,
                    ManaLevelGridMapCodec.DecodeLayers(settings),
                    settings.Collision,
                    ManaLevelGridMapCodec.DecodeSettings(settings),
                    ManaLevelGridMapCodec.DecodeAutoTiles(settings));
            }

            return gridMap;
        }

        private static ManaTerrainNode LoadTerrain(ManaLevelNode entry, ManaContext context, ManaScene scene)
        {
            var terrain = new ManaTerrainNode(context, 2, 2)
            {
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Terrain" : entry.Name
            };

            ApplyTransform(terrain, entry);
            lock (context.World.SceneLock)
            {
                scene.Children.Add(terrain);
            }

            // Patch colliders take their poses from the terrain's world transform, so it has to be settled first.
            terrain.UpdateTransform();
            ManaLevelTerrainCodec.Decode(entry.Terrain ?? new ManaLevelTerrain(), terrain);
            return terrain;
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
                    context.Physics.AddBoxBody(node, visualSize, offset).FollowsVisualBounds = collision.Offset == null;
                    return;
                }

                if (collision.Size != null)
                {
                    context.Physics.AddBoxBody(node, ToVector3(collision.Size), ToVector3(collision.Offset)).FollowsVisualBounds = false;
                }
            }
        }

        private static void AddScriptsIfPresent(ManaLevelNode entry, ManaSceneNode node)
        {
            if (entry.Scripts == null)
            {
                return;
            }

            // A node can carry two scripts of one type (two trigger volumes), so each is told which of its kind it is:
            // the first Trigger script belongs to the node's first trigger, the second to its second.
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var script in entry.Scripts)
            {
                occurrences.TryGetValue(script.Type, out var occurrence);
                occurrences[script.Type] = occurrence + 1;
                ManaLevelComponentScripts.AddToNode(node, script, occurrence);
            }
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
                loader.Load(ManaAssetPaths.Resolve(entry.Asset));
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

#if !MANA_BROWSER
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
                var lakesPath = ManaAssetPaths.Resolve(Path.Combine("Assets", "GeoData", "ne_10m_lakes.zip"));
                var oceanPath = ManaAssetPaths.Resolve(Path.Combine("Assets", "GeoData", "ne_10m_ocean.zip"));
                mask.AddLakes(GeoPolygonSet.TryLoadFromZip(lakesPath, "ne_10m_lakes.shp"));
                mask.Add(GeoPolygonSet.TryLoadFromZip(oceanPath, "ne_10m_ocean.shp"));
                GeoWaterMask.Shared = mask;
            }
        }
#endif

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
