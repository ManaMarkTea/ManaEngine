using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaGeoTools.Code.Planet;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Walks a world and writes down what somebody placed in it.
    /// <para>
    /// Nodes carrying a <see cref="ManaLevelSource"/> are captured together with explicitly authored
    /// children. Imported model internals and generated helper geometry are rebuilt by their owners.
    /// Prefab roots instead retain their source reference and instance overrides.
    /// </para>
    /// </summary>
    public static class ManaLevelCapture
    {
        /// <summary>
        /// Captures the world. <paramref name="cameraPose"/> overrides where the camera is recorded as
        /// being -- passed while playing, when the live camera is welded to the character's boom and saving
        /// it would mean every subsequent open started behind whoever was being played.
        /// </summary>
        public static ManaLevel Capture(
            ManaWorld world,
            string name = "Untitled",
            (Vector3 Position, Vector3 Rotation)? cameraPose = null)
        {
            var level = new ManaLevel { Name = name };

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    CaptureSceneNodes(scene, level.Nodes);
                    level.Sky ??= scene.AuthoredSky as ManaLevelSky;
                    level.Web ??= ManaLevelWeb.From(scene.Publishing);
                    CaptureGameModes(scene, level);
                }

                CaptureCamera(world, level, cameraPose);
            }

            return level;
        }

        // Left out entirely when there are none, so a level without modes saves exactly as it did before.
        private static void CaptureGameModes(ManaScene scene, ManaLevel level)
        {
            foreach (var mode in scene.GameModes)
            {
                (level.GameModes ??= new()).Add(ManaLevelGameModes.Capture(mode));
            }
        }

        public static ManaLevel CaptureScene(
            ManaWorld world,
            ManaScene scene,
            string name,
            (Vector3 Position, Vector3 Rotation)? cameraPose = null,
            bool includeCamera = true)
        {
            var level = new ManaLevel { Name = name };

            lock (world.SceneLock)
            {
                CaptureSceneNodes(scene, level.Nodes);
                level.Sky = scene.AuthoredSky as ManaLevelSky;
                level.Web = ManaLevelWeb.From(scene.Publishing);
                CaptureGameModes(scene, level);
                if (includeCamera)
                {
                    CaptureCamera(world, level, cameraPose);
                }
            }

            return level;
        }

        public static string CaptureSceneContentJson(ManaWorld world, ManaScene scene, string name)
        {
            return CaptureScene(world, scene, name, includeCamera: false).ToJson();
        }

        private static void CaptureSceneNodes(ManaScene scene, List<ManaLevelNode> output)
        {
            foreach (var child in scene.Children)
            {
                CaptureNode(child, output);
            }
        }

        private static void CaptureCamera(
            ManaWorld world,
            ManaLevel level,
            (Vector3 Position, Vector3 Rotation)? cameraPose)
        {
            var pose = cameraPose ?? (world.CameraNode != null
                ? (world.CameraNode.Position, world.CameraNode.Rotation)
                : null);

            if (pose.HasValue)
            {
                level.Camera = new ManaLevelCamera
                {
                    Position = ToArray(pose.Value.Position),
                    Rotation = ToArray(pose.Value.Rotation)
                };
            }
        }

        public static ManaLevelNode CaptureSubtree(ManaSceneNode node)
        {
            lock (node.Context.World.SceneLock)
            {
                return node.LevelSource is { } source && CaptureSource(node, source) is { } captured
                    ? captured : throw new InvalidDataException("Select an authored node or group.");
            }
        }

        private static void CaptureNode(ManaSceneNode node, List<ManaLevelNode> output)
        {
            if (node.IsEditorTransient)
            {
                return;
            }
            if (node.LevelSource is { } source)
            {
                var captured = CaptureSource(node, source);
                if (captured != null)
                {
                    output.Add(captured);
                }

                // CaptureSource handles this node's explicitly authored descendants.
                return;
            }

            foreach (var child in node.Children)
            {
                CaptureNode(child, output);
            }
        }

        private static ManaLevelNode? CaptureSource(ManaSceneNode node, ManaLevelSource source)
        {
            var entry = new ManaLevelNode
            {
                Id = node.AuthoredId,
                Name = node.Name,
                Kind = source.Kind,
                Position = ToArray(node.Position),
                Rotation = ToArray(node.Rotation),
                Scale = ToArray(node.Scale)
            };

            switch (source.Kind)
            {
                case ManaLevelSourceKind.Prefab:
                    entry.Prefab = ManaPrefabRuntime.CaptureReference(node);
                    entry.Scripts = CaptureScripts(node);
                    return entry;
                case ManaLevelSourceKind.Group:
                    entry.Collision = CaptureCollision(node);
                    entry.Children = new List<ManaLevelNode>();
                    foreach (var child in node.Children)
                    {
                        CaptureNode(child, entry.Children);
                    }

                    break;

                case ManaLevelSourceKind.Shape:
                    entry.Shape = source.Shape;
                    entry.Color = ToArray(source.Color);
                    entry.Size = ToArray(source.Size);
                    entry.Collision = CaptureCollision(node);
                    if (node.Components.OfType<MeshRendererComponent>().FirstOrDefault() is { } shapeRenderer)
                    {
                        entry.Material = ManaLevelMaterial.Capture(shapeRenderer.Material, node.Context, source.Color);
                    }
                    break;

                case ManaLevelSourceKind.Mesh:
                    if (string.IsNullOrWhiteSpace(source.Asset))
                    {
                        // A mesh with no asset reference cannot be reloaded, so saving it would only
                        // produce a node that silently vanishes on the next open.
                        return null;
                    }

                    // Written readably (and findable by name) whenever the zip is one of ours; see
                    // ZipAssetReferences.ToReadableReference.
                    entry.Asset = ZipAssetReferences.ToReadableReference(source.Asset);
                    var overrides = new Dictionary<int, ManaLevelMaterial>();
                    var meshIndex = 0;
                    foreach (var meshRenderer in ManaLevelMaterial.MeshRenderers(node))
                    {
                        if (meshRenderer.Material.SourceZip != null
                            && ManaLevelMaterial.Capture(meshRenderer.Material, node.Context, Vector4.One) is { } surface)
                        {
                            overrides.Add(meshIndex, surface);
                        }
                        meshIndex++;
                    }
                    entry.MeshMaterials = overrides.Count == 0 ? null : overrides;
                    entry.Collision = CaptureCollision(node);
                    break;

                case ManaLevelSourceKind.Voxels:
                    if (node is not ManaVoxelsNode voxelsNode)
                    {
                        return null;
                    }

                    var voxels = voxelsNode.VoxelShape.CommittedVoxels;
                    entry.Asset = source.Asset;
                    entry.Voxels = ManaLevelVoxelCodec.Encode(voxels);
                    break;

                case ManaLevelSourceKind.PlayerStart:
                    // Written when it asks for a character or for a height other than the default, which is what a
                    // start with no entry gets on load.
                    if (node is ManaPlayerStartNode start
                        && (!string.IsNullOrWhiteSpace(start.CharacterName) || start.CharacterHeight != ManaPlayerStartNode.DefaultCharacterHeight))
                    {
                        entry.Character = new ManaLevelCharacter
                        {
                            Pack = start.CharacterPack ?? string.Empty,
                            Name = start.CharacterName ?? string.Empty,
                            Height = start.CharacterHeight
                        };
                    }

                    break;

                case ManaLevelSourceKind.Planet:
                    if (node is not PlanetNode planet)
                    {
                        return null;
                    }

                    entry.Asset = source.Asset;
                    entry.Planet = new ManaLevelPlanet
                    {
                        PatchesPerFaceEdge = planet.PatchesPerFaceEdge,
                        Radius = planet.Radius,
                        Resolution = planet.Resolution,
                        HeightExaggeration = planet.HeightExaggeration,
                        OceanResolution = planet.OceanResolution,
                        AutoLodEnabled = planet.AutoLodEnabled,
                        NearOceanEnabled = planet.NearOceanEnabled
                    };
                    break;

                case ManaLevelSourceKind.Water:
                    if (node is not ManaWaterNode water)
                    {
                        return null;
                    }

                    entry.Water = new ManaLevelWater
                    {
                        Size = new[] { water.Mesh.Size.X, water.Mesh.Size.Y },
                        Resolution = water.Mesh.Resolution
                    };
                    break;

                case ManaLevelSourceKind.GridMap:
                    if (node is not ManaGridMapNode gridMap)
                    {
                        return null;
                    }

                    entry.GridMap = ManaLevelGridMapCodec.Encode(gridMap);
                    break;

                case ManaLevelSourceKind.Terrain:
                    if (node is not ManaTerrainNode terrain)
                    {
                        return null;
                    }

                    entry.Terrain = ManaLevelTerrainCodec.Encode(terrain);
                    break;

                default:
                    return null;
            }

            entry.Scripts = CaptureScripts(node);
            if (source.Kind != ManaLevelSourceKind.Group)
            {
                // Imported/generated descendants have no authored source. Explicitly authored children
                // do, and belong to the scene tree regardless of their parent's node type.
                entry.Children = new List<ManaLevelNode>();
                foreach (var child in node.Children.Where(child => child.LevelSource != null && !child.IsEditorTransient))
                {
                    CaptureNode(child, entry.Children);
                }
                if (entry.Children.Count == 0)
                {
                    entry.Children = null;
                }
            }
            if (source.Kind == ManaLevelSourceKind.Mesh)
                entry.ChildColliders = ManaLevelMeshColliders.Capture(node);
            return entry;
        }

        private static List<ManaLevelScript>? CaptureScripts(ManaSceneNode node)
        {
            return ManaLevelComponentScripts.Capture(node);
        }

        /// <summary>
        /// The kinematic box a platform stands on, read back off the Bepu shape rather than remembered
        /// separately -- there is only one copy of the dimensions this way, and it is the one the
        /// simulation is actually using.
        /// </summary>
        private static ManaLevelCollision? CaptureCollision(ManaSceneNode node)
        {
            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                // Authored colliders are saved individually in Scripts, including multiple boxes.
                if (collision is ManaBoxColliderComponent) continue;
                if (collision.Shape is ConvexHull)
                {
                    return new ManaLevelCollision
                    {
                        UseVisualHull = true
                    };
                }

                if (collision.Shape is not Box box)
                {
                    continue;
                }

                var size = new Vector3(box.Width, box.Height, box.Length);
                var offset = collision.Position;
                // Still meant to fit the model, even while the model is part-loaded and the two differ.
                if (collision.FollowsVisualBounds ?? ManaCollisionBounds.MatchesVisualBox(node, size, offset))
                {
                    return new ManaLevelCollision
                    {
                        UseVisualBounds = true
                    };
                }

                return new ManaLevelCollision
                {
                    Size = ToArray(size),
                    Offset = IsNearlyZero(offset) ? null : ToArray(offset)
                };
            }

            return null;
        }

        private static bool IsNearlyZero(Vector3 value)
        {
            return MathF.Abs(value.X) <= 0.001f
                && MathF.Abs(value.Y) <= 0.001f
                && MathF.Abs(value.Z) <= 0.001f;
        }

        private static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };

        private static float[] ToArray(Vector4 value) => new[] { value.X, value.Y, value.Z, value.W };
    }
}
