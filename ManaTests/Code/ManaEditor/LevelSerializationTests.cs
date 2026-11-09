using System.Text.Json;
using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;
using ManaGeoTools.Code.Planet;
using ManaUI;
using ManaEditor.Code.Assets;
using ManaEditor.Code.Gameplay.Trains;
using ManaEditor.Code.Levels;
using ManaEditor.Code.UI;

namespace ManaTests;

/// <summary>
/// The level file: what it keeps, what it deliberately drops, and whether what it keeps survives a trip
/// through disk unchanged.
/// <para>
/// Checkable headlessly because the format is deliberately data, not scene graph -- capture produces plain
/// records and loading consumes them, so everything except the two ends (a live world in, a live world out)
/// can be exercised with no graphics device. The parts that need a world are covered where they are
/// cheapest to be wrong: the voxel codec, which is the only place a level can lose information silently.
/// </para>
/// </summary>
public static class LevelSerializationChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static IEnumerable<ManaLevelNode> WalkNodes(IEnumerable<ManaLevelNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                if (node.Children == null)
                {
                    continue;
                }

                foreach (var child in WalkNodes(node.Children))
                {
                    yield return child;
                }
            }
        }

        // ---------------------------------------------------------------- json round trip
        var level = new ManaLevel
        {
            Name = "Test Level",
            Camera = new ManaLevelCamera
            {
                Position = new[] { 1.0f, 2.0f, 3.0f },
                Rotation = new[] { 0.1f, 0.2f, 0.3f }
            },
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Platform",
                    Kind = ManaLevelSourceKind.Shape,
                    Shape = "Cube",
                    Position = new[] { 4.0f, 5.0f, 6.0f },
                    Rotation = new[] { 0.0f, 1.5f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Color = new[] { 0.25f, 0.5f, 0.75f, 1.0f },
                    Size = new[] { 1.5f, 0.25f, 1.5f },
                    Collision = new ManaLevelCollision
                    {
                        Size = new[] { 3.0f, 0.5f, 3.0f },
                        Offset = new[] { 0.0f, 0.0f, 0.0f }
                    },
                    Scripts = new List<ManaLevelScript>
                    {
                        new ManaLevelScript
                        {
                            Type = TrainCarComponent.ScriptType,
                            Properties =
                            {
                                ["routeKey"] = JsonValue("A"),
                                ["carIndex"] = JsonValue(2),
                                ["speed"] = JsonValue(6.25f)
                            }
                        }
                    }
                },
                new ManaLevelNode
                {
                    Name = "Player Start",
                    Kind = ManaLevelSourceKind.PlayerStart,
                    Position = new[] { 10.0f, 0.0f, -2.0f },
                    Rotation = new[] { 0.0f, 3.0f, 0.0f }
                }
            }
        };

        var reloaded = ManaLevel.FromJson(level.ToJson());

        Check("a level survives a json round trip",
            reloaded.Nodes.Count == 2
            && reloaded.Name == "Test Level"
            && reloaded.Camera?.Position[1] == 2.0f,
            $"{reloaded.Nodes.Count} nodes");

        var platform = reloaded.Nodes[0];
        Check("a shape keeps its colour, size and collider",
            platform.Shape == "Cube"
            && platform.Color![2] == 0.75f
            && platform.Size![1] == 0.25f
            && platform.Collision!.Size![0] == 3.0f);

        var platformScript = platform.Scripts?.FirstOrDefault();
        Check("a node keeps script component data",
            platformScript?.Type == TrainCarComponent.ScriptType
            && platformScript.Properties["routeKey"].GetString() == "A"
            && platformScript.Properties["carIndex"].GetInt32() == 2
            && Math.Abs(platformScript.Properties["speed"].GetSingle() - 6.25f) < 0.001f);

        Check("the player start round trips with its facing",
            reloaded.PlayerStart != null
            && reloaded.PlayerStart.Position[0] == 10.0f
            && reloaded.PlayerStart.Rotation[1] == 3.0f);

        var groupedLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Gameplay",
                    Kind = ManaLevelSourceKind.Group,
                    Position = new[] { 0.0f, 0.0f, 0.0f },
                    Rotation = new[] { 0.0f, 0.0f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Children = new List<ManaLevelNode>
                    {
                        new()
                        {
                            Name = "Nested Player Start",
                            Kind = ManaLevelSourceKind.PlayerStart,
                            Position = new[] { 7.0f, 0.0f, 8.0f },
                            Rotation = new[] { 0.0f, 1.0f, 0.0f }
                        }
                    }
                }
            }
        };

        var groupedReloaded = ManaLevel.FromJson(groupedLevel.ToJson());
        Check("a group keeps its named children",
            groupedReloaded.Nodes[0].Kind == ManaLevelSourceKind.Group
            && groupedReloaded.Nodes[0].Children?[0].Name == "Nested Player Start");

        Check("player start lookup walks grouped children",
            groupedReloaded.PlayerStart?.Position[0] == 7.0f);

        var waterReloaded = ManaLevel.FromJson(new ManaLevel
        {
            Name = "Water Round Trip",
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Pond Water",
                    Kind = ManaLevelSourceKind.Water,
                    Position = new[] { 32.0f, 0.2f, 32.0f },
                    Water = new ManaLevelWater
                    {
                        Size = new[] { 18.0f, 12.0f },
                        Resolution = 72
                    }
                }
            }
        }.ToJson());
        Check("water construction settings survive JSON",
            waterReloaded.Nodes.SingleOrDefault() is
            {
                Kind: ManaLevelSourceKind.Water,
                Water: { Resolution: 72 }
            } waterEntry
            && waterEntry.Water.Size.SequenceEqual(new[] { 18.0f, 12.0f }));

        var gizmoType = typeof(Mana.Code.GameModes.VoxelEditorGameMode).Assembly.GetType(
            "Mana.Code.GameModes.SceneTransformGizmo");
        var projectAxis = gizmoType?.GetMethod(
            "TryAxisParameterOnPlane",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var distanceToHandle = gizmoType?.GetMethod(
            "DistanceFromRayToSegment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check("gizmo drag projection helpers are available", projectAxis != null && distanceToHandle != null);

        var projectPlane = gizmoType?.GetMethod("TryPlanePoint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check("two-axis gizmo projection is available", projectPlane != null);
        if (projectPlane != null)
        {
            foreach (var normal in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
                         Vector3.Normalize(new Vector3(1, 2, 3)) })
            {
                var wanted = Vector3.Cross(normal, new Vector3(2, 3, 4));
                var pivot = new Vector3(40, 5, -20);
                object?[] ray = { pivot + normal * 10, Vector3.Normalize(wanted - normal * 10), pivot, normal, Vector3.Zero };
                var hit = projectPlane.Invoke(null, ray) is true;
                var point = (Vector3)ray[4]!;
                Check("plane movement preserves the excluded axis, including rotated handles",
                    hit && Vector3.Distance(point, wanted) < 0.001f && MathF.Abs(Vector3.Dot(point, normal)) < 0.001f);
            }
            object?[] centre = { new Vector3(0, 0, 10), -Vector3.UnitZ, Vector3.Zero, Vector3.UnitZ, Vector3.One };
            Check("a plane drag may start exactly at its origin", projectPlane.Invoke(null, centre) is true && (Vector3)centre[4]! == Vector3.Zero);
            object?[] parallel = { new Vector3(0, 0, 10), Vector3.UnitX, Vector3.Zero, Vector3.UnitZ, Vector3.Zero };
            Check("an edge-on plane refuses an unstable drag", projectPlane.Invoke(null, parallel) is false);
        }

        if (projectAxis != null)
        {
            object?[] centerRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 0, -10)),
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitZ,
                0.0f
            };
            object?[] rightRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(1, 0, -10)),
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitZ,
                0.0f
            };
            object?[] upRay =
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 1, -10)),
                Vector3.Zero,
                Vector3.UnitY,
                Vector3.UnitZ,
                0.0f
            };

            var centerProjected = projectAxis.Invoke(null, centerRay) is true;
            var rightProjected = projectAxis.Invoke(null, rightRay) is true;
            var upProjected = projectAxis.Invoke(null, upRay) is true;
            var centerParameter = centerRay[5] is float center ? center : float.NaN;
            var rightParameter = rightRay[5] is float right ? right : float.NaN;
            var upParameter = upRay[5] is float up ? up : float.NaN;
            Check("gizmo X follows the mouse right at scene scale",
                centerProjected && rightProjected
                && MathF.Abs(centerParameter) < 0.001f
                && MathF.Abs(rightParameter - 1.0f) < 0.001f,
                $"{centerParameter:0.###} -> {rightParameter:0.###}");
            Check("gizmo Y follows the mouse up at scene scale",
                upProjected && MathF.Abs(upParameter - 1.0f) < 0.001f,
                upParameter.ToString("0.###"));

            var farPivot = new Vector3(250_000.0f, 80_000.0f, -175_000.0f);
            object?[] farRightRay =
            {
                farPivot + new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(1, 0, -10)),
                farPivot,
                Vector3.UnitX,
                Vector3.UnitZ,
                0.0f
            };
            var farProjected = projectAxis.Invoke(null, farRightRay) is true;
            var farParameter = farRightRay[5] is float far ? far : float.NaN;
            Check("gizmo projection stays precise far from world origin",
                farProjected && MathF.Abs(farParameter - 1.0f) < 0.01f,
                farParameter.ToString("0.###"));
        }

        if (distanceToHandle != null)
        {
            var directHit = (float)distanceToHandle.Invoke(null, new object[]
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(1, 0, -10)),
                Vector3.Zero,
                new Vector3(2, 0, 0)
            })!;
            var clearMiss = (float)distanceToHandle.Invoke(null, new object[]
            {
                new Vector3(0, 0, 10),
                Vector3.Normalize(new Vector3(0, 1, -10)),
                Vector3.Zero,
                new Vector3(2, 0, 0)
            })!;
            Check("gizmo shafts have stable analytic hit targets",
                directHit < 0.001f && clearMiss > 0.5f,
                $"hit {directHit:0.###}, miss {clearMiss:0.###}");
        }

        var insertionWorld = new ManaWorld();
        var insertionContext = new ManaContext(null!, null!, insertionWorld) { DeferGpuUploads = true };
        var existingScene = new ManaScene(insertionContext) { Name = "Existing" };
        var importedScene = new ManaScene(insertionContext) { Name = "Imported" };
        var existingNode = new ManaSceneNode(insertionContext, Matrix4x4.Identity) { Name = "Existing Node" };
        var importedNode = new ManaSceneNode(insertionContext, Matrix4x4.Identity) { Name = "Imported Node" };
        var importedMesh = new ManaCube(insertionContext, Vector4.One, Vector3.One);
        var importedMaterial = new ManaMaterial(insertionContext) { Name = "Imported Material" };
        existingScene.Children.Add(existingNode);
        importedScene.Children.Add(importedNode);
        insertionWorld.Scenes.Add(existingScene);
        var sceneStart = insertionWorld.Scenes.Count;
        var meshStart = insertionWorld.Meshes.Count;
        var materialStart = insertionWorld.Materials.Count;
        insertionWorld.Scenes.Add(importedScene);
        insertionWorld.Meshes.Add(importedMesh);
        insertionWorld.Materials.Add(importedMaterial);

        var insertion = EditorWorldInsertion.Capture(insertionWorld, sceneStart, meshStart, materialStart);
        var insertionHistory = new EditorUndoHistory();
        insertionWorld.Selection.SelectedNode = existingNode;
        var selectionBeforeInsertion = EditorSelectionSnapshot.Capture(insertionWorld.Selection);
        EditorSelectionSnapshot.SelectNode(
            insertionWorld.Selection,
            importedNode,
            sceneGraphChanged: true);
        insertionHistory.RecordWorldInsertion(
            insertion,
            importedNode,
            selectionBeforeInsertion,
            "Place imported actor",
            insertionWorld.Selection);
        insertionHistory.Undo();
        Check("undoing an actor placement removes all imported world data",
            insertionWorld.Scenes.SequenceEqual(new[] { existingScene })
            && insertionWorld.Meshes.Count == 0
            && insertionWorld.Materials.Count == 0
            && ReferenceEquals(insertionWorld.Selection.SelectedNode, existingNode),
            $"{insertionWorld.Scenes.Count} scenes, {insertionWorld.Meshes.Count} meshes, "
            + $"{insertionWorld.Materials.Count} materials, selection {insertionWorld.Selection.SelectedNode?.Name}");
        insertionHistory.Redo();
        Check("redoing an actor placement restores the same objects",
            insertionWorld.Scenes.Count == 2
            && ReferenceEquals(insertionWorld.Scenes[1], importedScene)
            && ReferenceEquals(insertionWorld.Meshes.Single(), importedMesh)
            && ReferenceEquals(insertionWorld.Materials.Single(), importedMaterial)
            && ReferenceEquals(insertionWorld.Selection.SelectedNode, importedNode),
            $"{insertionWorld.Scenes.Count} scenes, {insertionWorld.Meshes.Count} meshes, "
            + $"{insertionWorld.Materials.Count} materials, selection {insertionWorld.Selection.SelectedNode?.Name}");

        var wedgeWorld = new ManaWorld();
        var wedgeContext = new ManaContext(null!, null!, wedgeWorld) { DeferGpuUploads = true };
        wedgeContext.Physics = new ManaPhysics(wedgeContext);
        var wedgeScene = new ManaScene(wedgeContext) { Name = "Wedge Scene" };
        wedgeWorld.Scenes.Add(wedgeScene);
        var wedgeLoadResult = ManaLevelLoader.Load(
            new ManaLevel
            {
                Nodes =
                {
                    new ManaLevelNode
                    {
                        Name = "Shore Slope",
                        Kind = ManaLevelSourceKind.Shape,
                        Shape = "Wedge",
                        Position = new[] { 2.0f, 3.0f, 4.0f },
                        Size = new[] { 6.0f, 2.0f, 4.0f },
                        Color = new[] { 0.2f, 0.6f, 0.3f, 1.0f },
                        Collision = new ManaLevelCollision { UseVisualHull = true }
                    }
                }
            },
            wedgeContext,
            wedgeScene,
            replaceAuthoredNodes: false);
        var wedgeNode = wedgeScene.Children.SingleOrDefault(node => node.Name == "Shore Slope");
        var wedgeMesh = wedgeNode?.Components.OfType<MeshRendererComponent>().SingleOrDefault()?.Mesh as ManaWedge;
        var wedgePositions = wedgeMesh?.GetOrCreateCpuData().Positions ?? Array.Empty<Vector3>();
        Check("wedge level data constructs a wedge mesh",
            wedgeNode?.LevelSource is { Shape: "Wedge", Size: var wedgeSize }
            && wedgeSize == new Vector3(6.0f, 2.0f, 4.0f)
            && wedgeMesh != null);
        Check("a wedge can derive matching convex collision from its mesh",
            wedgeNode?.Components.OfType<ManaCollisionComponent>().SingleOrDefault()?.Shape is ConvexHull,
            string.Join(" | ", wedgeLoadResult.Warnings));
        Check("a wedge occupies its full authored bounds",
            wedgePositions.Length == 24
            && wedgePositions.Min(position => position.X) == -3.0f
            && wedgePositions.Max(position => position.X) == 3.0f
            && wedgePositions.Min(position => position.Y) == -1.0f
            && wedgePositions.Max(position => position.Y) == 1.0f
            && wedgePositions.Min(position => position.Z) == -2.0f
            && wedgePositions.Max(position => position.Z) == 2.0f,
            $"{wedgePositions.Length} vertices");
        Check("saving a wedge preserves its primitive recipe",
            ManaLevelCapture.CaptureScene(wedgeWorld, wedgeScene, "Wedge Scene")
                .Nodes.SingleOrDefault() is { Shape: "Wedge" } capturedWedge
            && capturedWedge.Size!.SequenceEqual(new[] { 6.0f, 2.0f, 4.0f })
            && capturedWedge.Collision?.UseVisualHull == true);

        // ---------------------------------------------------------------- shape materials
        // Saved as only what differs from the material a shape is built with, so an untouched one writes nothing.
        Check("an untouched shape saves no material",
            ManaLevelCapture.CaptureScene(wedgeWorld, wedgeScene, "Wedge Scene").Nodes.Single().Material == null);

        var wedgeMaterial = wedgeNode!.Components.OfType<MeshRendererComponent>().Single().Material;
        wedgeMaterial.Metallic.Value = 1.0f;
        wedgeMaterial.Roughness.Value = 0.05f;
        wedgeMaterial.Unlit = true;
        var materialJson = ManaLevelCapture.CaptureScene(wedgeWorld, wedgeScene, "Wedge Scene").ToJson();
        var savedMaterial = ManaLevel.FromJson(materialJson).Nodes.Single().Material;
        Check("an edited shape saves just what changed",
            savedMaterial is { Metallic: 1.0f, Unlit: true, Albedo: null, Opacity: null }
            && MathF.Abs((savedMaterial.Roughness ?? 0.0f) - 0.05f) < 0.001f,
            $"metallic {savedMaterial?.Metallic}, roughness {savedMaterial?.Roughness}, unlit {savedMaterial?.Unlit}, albedo {savedMaterial?.Albedo}");

        var materialScene = new ManaScene(wedgeContext) { Name = "Material Scene" };
        wedgeWorld.Scenes.Add(materialScene);
        ManaLevelLoader.Load(ManaLevel.FromJson(materialJson), wedgeContext, materialScene, replaceAuthoredNodes: false);
        var reloadedMaterial = materialScene.Children.Single().Components.OfType<MeshRendererComponent>().Single().Material;
        Check("and loads it back",
            reloadedMaterial.Metallic.Value == 1.0f && MathF.Abs(reloadedMaterial.Roughness.Value - 0.05f) < 0.001f
            && reloadedMaterial.Unlit,
            $"metallic {reloadedMaterial.Metallic.Value}, roughness {reloadedMaterial.Roughness.Value}, unlit {reloadedMaterial.Unlit}");
        wedgeWorld.Scenes.Remove(materialScene);

        // On a prefab instance a material is an override like any other field, found and re-applied by the same diff.
        var prefabShape = new ManaLevelNode { Id = "a1", Name = "Slab", Kind = ManaLevelSourceKind.Shape, Shape = "Cube" };
        var editedShape = new ManaLevelNode
        {
            Id = "a1", Name = "Slab", Kind = ManaLevelSourceKind.Shape, Shape = "Cube",
            Material = new ManaLevelMaterial { Metallic = 1.0f, Roughness = 0.0f }
        };
        var materialOverrides = ManaEditor.Code.Assets.ManaPrefabOverrides.Diff(
            ManaEditor.Code.Assets.ManaPrefabOverrides.Flatten(prefabShape),
            ManaEditor.Code.Assets.ManaPrefabOverrides.Flatten(editedShape));
        var resolvedShape = ManaEditor.Code.Assets.ManaPrefabOverrides.Resolve(prefabShape, materialOverrides);
        Check("a prefab instance's material edit is an override that resolves back",
            materialOverrides.Count == 1 && materialOverrides[0].Path.Contains("material")
            && resolvedShape.Material is { Metallic: 1.0f, Roughness: 0.0f },
            string.Join(" | ", materialOverrides.Select(o => string.Join("/", o.Path))));

        var disk = new ManaDisk(wedgeContext, Vector4.One, new Vector3(4.0f, 0.1f, 2.0f));
        var diskData = disk.GetOrCreateCpuData();
        Check("a disk is flat, elliptical, and occupies its full authored bounds",
            diskData.Positions.Length == 384
            && diskData.Positions.Min(position => position.X) == -2.0f
            && diskData.Positions.Max(position => position.X) == 2.0f
            && diskData.Positions.Min(position => position.Y) == -0.05f
            && diskData.Positions.Max(position => position.Y) == 0.05f
            && diskData.Positions.Min(position => position.Z) == -1.0f
            && diskData.Positions.Max(position => position.Z) == 1.0f,
            $"{diskData.Positions.Length} vertices");
        Check("a disk has flat caps and separate side normals",
            diskData.Normals.Any(normal => normal.Y > 0.99f)
            && diskData.Normals.Any(normal => normal.Y < -0.99f)
            && diskData.Normals.Any(normal => MathF.Abs(normal.Y) < 0.01f));

        var slopePolicy = typeof(ManaPhysics).Assembly
            .GetType("Mana.Code.DemoNarrowPhaseCallbacks")?
            .GetMethod("IsWalkableSlope", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check("character friction is reserved for walkable slopes",
            slopePolicy?.Invoke(null, new object[] { Vector3.Normalize(new Vector3(0.4f, 1.0f, 0.0f)) }) is true
            && slopePolicy.Invoke(null, new object[] { Vector3.UnitY }) is false
            && slopePolicy.Invoke(null, new object[] { Vector3.UnitX }) is false);

        var supportPolicy = typeof(ManaPawnNode).GetMethod(
            "ComputeIdleGroundSupportVelocity",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var supportNormal = Vector3.Normalize(new Vector3(0.0f, 1.0f, 0.4f));
        const float supportDelta = 1.0f / 60.0f;
        var gravity = new Vector3(0.0f, -64.0f, 0.0f);
        var supportVelocity = supportPolicy?.Invoke(
            null,
            new object[] { gravity, supportNormal, supportDelta }) is Vector3 support
                ? support
                : new Vector3(float.NaN);
        var velocityAfterGravity = supportVelocity + gravity * supportDelta;
        var remainingTangent = velocityAfterGravity
            - Vector3.Dot(velocityAfterGravity, supportNormal) * supportNormal;
        Check("idle ground support cancels downhill gravity before the solver step",
            NearlyEqual(remainingTangent, Vector3.Zero, 0.0001f)
            && supportVelocity.Z < 0.0f,
            $"support {supportVelocity}, remaining tangent {remainingTangent}");

        var planetWorld = new ManaWorld();
        var planetContext = new ManaContext(null!, null!, planetWorld) { DeferGpuUploads = true };
        var planetScene = new ManaScene(planetContext) { Name = "Planet Scene" };
        planetWorld.Scenes.Add(planetScene);
        var planetResult = ManaLevelLoader.Load(
            new ManaLevel
            {
                Nodes =
                {
                    new ManaLevelNode
                    {
                        Name = "Tiny Planet",
                        Kind = ManaLevelSourceKind.Planet,
                        Position = new[] { 1.0f, 2.0f, 3.0f },
                        Scale = new[] { 1.0f, 1.0f, 1.0f },
                        Planet = new ManaLevelPlanet
                        {
                            PatchesPerFaceEdge = 1,
                            Radius = 3.0f,
                            Resolution = 2,
                            OceanResolution = 2,
                            HeightExaggeration = 4.0f,
                            AutoLodEnabled = true,
                            NearOceanEnabled = false
                        }
                    }
                }
            },
            planetContext,
            planetScene,
            replaceAuthoredNodes: false);
        Check("planet level data constructs a planet node",
            planetResult.Planet is
            {
                Name: "Tiny Planet",
                PatchesPerFaceEdge: 1,
                Radius: 3.0f,
                Resolution: 2,
                OceanResolution: 2,
                AutoLodEnabled: true,
                NearOceanEnabled: false
            }
            && planetResult.Planet.Position == new Vector3(1.0f, 2.0f, 3.0f));

        var capturedPlanet = ManaLevelCapture.CaptureScene(planetWorld, planetScene, "Planet Scene");
        Check("saving a planet preserves its construction settings",
            capturedPlanet.Nodes.SingleOrDefault()?.Planet is
            {
                PatchesPerFaceEdge: 1,
                Radius: 3.0f,
                OceanResolution: 2,
                HeightExaggeration: 4.0f
            });

        var builtInLevelNames = new[]
        {
            "Voxel Showcase",
            "Planet Showcase",
            "PBR Gallery",
            "Little Pond"
        };
        var builtInLevels = builtInLevelNames
            .Select(name => ManaLevelLibrary.ResolvePath(name))
            .Where(File.Exists)
            .Select(ManaLevel.Load)
            .ToList();
        Check("showcase content lives in separate level files",
            builtInLevels.Count == builtInLevelNames.Length,
            string.Join(", ", builtInLevels.Select(item => item.Name)));

        var voxelShowcase = builtInLevels.FirstOrDefault(item => item.Name == "Voxel Showcase");
        Check("the voxel showcase references the Captain asset",
            voxelShowcase?.Nodes.Any(node =>
                node.Kind == ManaLevelSourceKind.Voxels
                && node.Asset?.EndsWith("Captain.vox", StringComparison.OrdinalIgnoreCase) == true) == true);

        var planetShowcase = builtInLevels.FirstOrDefault(item => item.Name == "Planet Showcase");
        Check("the planet showcase carries authored streaming settings",
            planetShowcase?.Nodes.SingleOrDefault(node => node.Kind == ManaLevelSourceKind.Planet)?.Planet is
            {
                PatchesPerFaceEdge: 6,
                Radius: 40.0f
            });

        var gallery = builtInLevels.FirstOrDefault(item => item.Name == "PBR Gallery");
        var galleryMeshes = WalkNodes(gallery?.Nodes ?? Enumerable.Empty<ManaLevelNode>())
            .Where(node => node.Kind == ManaLevelSourceKind.Mesh)
            .ToList();
        Check("the PBR gallery presents three material-rich models",
            galleryMeshes.Count(node => node.Asset?.Contains("ShaderBall", StringComparison.Ordinal) != true) == 3,
            string.Join(", ", galleryMeshes.Select(node => node.Name)));
        Check("the PBR gallery compares 21 ZIP presets on shader balls",
            galleryMeshes.Count(node => node.Asset?.Contains("ShaderBall", StringComparison.Ordinal) == true
                && node.MeshMaterials?.GetValueOrDefault(0)?.SourceZip != null) == 21);
        Check("every gallery model resolves from its portable asset path",
            galleryMeshes.All(node =>
                node.Asset != null && File.Exists(ManaAssetPaths.Resolve(node.Asset))),
            string.Join(", ", galleryMeshes.Select(node => node.Asset)));

        var pond = builtInLevels.FirstOrDefault(item => item.Name == "Little Pond");
        var pondWater = WalkNodes(pond?.Nodes ?? Enumerable.Empty<ManaLevelNode>())
            .SingleOrDefault(node => node.Kind == ManaLevelSourceKind.Water);
        Check("the pond owns a ripple-capable water surface",
            pondWater?.Water is { Resolution: 112 } pondSettings
            && pondSettings.Size.SequenceEqual(new[] { 18.5f, 18.5f }));
        var pondNodes = WalkNodes(pond?.Nodes ?? Enumerable.Empty<ManaLevelNode>()).ToList();
        Check("the pond cuts its meadow around a recessed bed",
            pondNodes.Count(node => node.Shape == "Cube" && node.Name?.EndsWith("Meadow") == true) == 4
            && pondNodes.SingleOrDefault(node => node.Name == "Pond Bed") is
            {
                Shape: "Cube",
                Position: [_, 0.1f, _]
            });
        Check("the pond shoreline uses four directional wedges",
            pondNodes.Count(node => node.Shape == "Wedge") == 4
            && pondNodes.Where(node => node.Shape == "Wedge")
                .All(node => node.Collision?.UseVisualHull == true),
            string.Join(", ", pondNodes.Where(node => node.Shape == "Wedge").Select(node => node.Name)));
        Check("the pond uses flat disk lily pads",
            pondNodes.Count(node => node.Name?.StartsWith("Lily Pad", StringComparison.Ordinal) == true
                && node.Shape == "Disk") == 2);

        // Games' own levels (Games/<Name>/Content/Levels) are bundled too.
        var allBundledLevels = ManaLevelLibrary.List(Path.GetDirectoryName(ManaLevelLibrary.ResolvePath("Little Pond"))!)
            .Select(ManaLevel.Load)
            .ToList();
        Check("bundled level cameras stay clear of the zero-axis grid walls",
            allBundledLevels.All(item => item.Camera is { Position.Length: >= 3 }
                && item.Camera.Position[0] >= 32.0f
                && item.Camera.Position[2] >= 32.0f),
            string.Join(", ", allBundledLevels.Select(item => item.Name)));
        // Play spawns a character only for a level whose game mode asks for one (Character, or a game that drives it --
        // the runner); one with a Player Start that forgot would quietly play as a debug camera.
        var startsWithoutCharacter = allBundledLevels
            .Where(item => item.PlayerStart != null
                && item.GameModes?.Any(mode => mode.Type == "Character" || ManaLevelGameModes.Create(mode.Type)?.WantsPlayerCharacter == true) != true)
            .Select(item => item.Name)
            .ToList();
        Check("bundled levels with a Player Start play as a character (Character game mode)", startsWithoutCharacter.Count == 0,
            string.Join(", ", startsWithoutCharacter));
        Check("bundled level roots use the 32,0,32 scene offset",
            // A level may be all rules and no placed nodes -- a game mode that builds its own world.
            allBundledLevels.All(item => (item.Nodes.Count > 0 || item.GameModes is { Count: > 0 })
                && item.Nodes.All(node => node.Position.Length >= 3
                    && node.Position[0] >= 32.0f
                    && node.Position[2] >= 32.0f)),
            string.Join(", ", allBundledLevels.Select(item => item.Name)));

        // ---------------------------------------------------------------- scene capture
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world);
        var sceneA = new ManaScene(context) { Name = "Scene A", LevelPath = "C:/levels/a.manalevel" };
        var sceneB = new ManaScene(context) { Name = "Scene B", LevelPath = "C:/levels/b.manalevel" };
        var sceneANode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Scene A Box",
            LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One)
        };
        var sceneBNode = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Scene B Box",
            LevelSource = ManaLevelSource.ForShape("Cube", Vector4.One, Vector3.One)
        };
        var trainScript = new TrainCarComponent(sceneANode);
        trainScript.ConfigureDefaultRoute("B", 1, 0.25f);
        trainScript.Speed = 7.5f;
        sceneANode.Components.Add(trainScript);

        sceneA.Children.Add(sceneANode);
        sceneB.Children.Add(sceneBNode);
        world.Scenes.Add(sceneA);
        world.Scenes.Add(sceneB);

        var sceneOnly = ManaLevelCapture.CaptureScene(world, sceneA, "Scene A");
        Check("capturing one scene keeps only that scene's authored nodes",
            sceneOnly.Nodes.Count == 1 && sceneOnly.Nodes[0].Name == "Scene A Box",
            string.Join(", ", sceneOnly.Nodes.Select(node => node.Name)));

        var capturedScript = sceneOnly.Nodes[0].Scripts?.FirstOrDefault();
        Check("capturing a scene writes train scripts",
            capturedScript?.Type == TrainCarComponent.ScriptType
            && capturedScript.Properties["routeKey"].GetString() == "B"
            && capturedScript.Properties["carIndex"].GetInt32() == 1
            && Math.Abs(capturedScript.Properties["speed"].GetSingle() - 7.5f) < 0.001f);

        Check("runtime train state is transient",
            capturedScript != null && !capturedScript.Properties.ContainsKey("paused"));

        var canvasWorld = new ManaWorld();
        var canvasContext = new ManaContext(null!, null!, canvasWorld);
        var canvasScene = new ManaScene(canvasContext) { Name = "UI Scene" };
        canvasWorld.Scenes.Add(canvasScene);
        var canvasLevel = new ManaLevel
        {
            Nodes = { new ManaLevelNode
            {
                Name = "HUD", Kind = ManaLevelSourceKind.Group,
                Scripts = [new ManaLevelScript
                {
                    Type = "UiCanvas", Properties =
                    {
                        ["layout"] = JsonValue(new Mana.Code.Content.ManaAssetRef("", "@Contour/UI/Contour HUD.manaui")),
                        ["bindingSource"] = JsonValue("Contour"),
                        ["visible"] = JsonValue(false)
                    }
                }]
            } }
        };
        var loadedCanvas = ManaLevelLoader.Load(canvasLevel, canvasContext, canvasScene, replaceAuthoredNodes: false);
        var canvas = canvasScene.Children.Single().Components.OfType<ManaUiCanvasComponent>().Single();
        Check("a UI canvas loads as a component on a level node", loadedCanvas.Warnings.Count == 0 && !canvas.Visible && canvas.BindingSource == "Contour");
        var canvasJson = ManaLevelCapture.CaptureScene(canvasWorld, canvasScene, "UI Scene").ToJson();
        var savedCanvas = (ManaLevel.FromJson(canvasJson).Nodes.Single().Scripts ?? throw new InvalidDataException("Missing canvas script")).Single();
        Check("a UI canvas preserves its layout reference and settings on save", savedCanvas.Type == "UiCanvas"
            && savedCanvas.Properties["layout"].GetProperty("assetPath").GetString() == canvas.Layout.AssetPath
            && !savedCanvas.Properties["visible"].GetBoolean());
        var canvasRunner = new Mana.Code.GameModes.ManaGameModeRunner();
        canvasRunner.UseTheme(new UiTheme());
        canvasRunner.Begin([], new Mana.Code.GameModes.ManaPlaySession(canvasContext, () => false, () => false, () => { }, false));
        Check("a level canvas is hosted even without a game mode", ((Grid)canvasRunner.HudLayer).Children.Count == 1);
        canvasRunner.Tick(0);
        Check("a canvas is not rebuilt while nothing has changed", ((Grid)canvasRunner.HudLayer).Children.Count == 1);

        // One the scene graph no longer holds stops announcing itself as the world updates.
        canvasScene.Children.Clear();
        canvasWorld.BeginUiCanvasPass();
        canvasRunner.Tick(0);
        Check("removing a UI node removes its screen overlay", ((Grid)canvasRunner.HudLayer).Children.Count == 0);

        var lateNode = new ManaSceneNode(canvasContext, System.Numerics.Matrix4x4.Identity);
        var lateCanvas = new ManaUiCanvasComponent(lateNode) { Layout = canvas.Layout };
        canvasWorld.BeginUiCanvasPass();
        lateCanvas.UpdateComponent();
        canvasRunner.Tick(0);
        Check("a UI node that turns up mid-play gets its overlay", ((Grid)canvasRunner.HudLayer).Children.Count == 1);
        canvasRunner.End();

        var scriptedWorld = new ManaWorld();
        var scriptedContext = new ManaContext(null!, null!, scriptedWorld);
        var scriptedScene = new ManaScene(scriptedContext) { Name = "Scripted Scene" };
        scriptedWorld.Scenes.Add(scriptedScene);

        var scriptedLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Scripted Train",
                    Kind = ManaLevelSourceKind.Group,
                    Scripts = new List<ManaLevelScript>
                    {
                        new()
                        {
                            Type = TrainCarComponent.ScriptType,
                            Properties =
                            {
                                ["routeKey"] = JsonValue("C"),
                                ["carIndex"] = JsonValue(4),
                                ["speed"] = JsonValue(8.75f),
                                ["paused"] = JsonValue(false)
                            }
                        }
                    }
                }
            }
        };

        var scriptedResult = ManaLevelLoader.Load(
            scriptedLevel,
            scriptedContext,
            scriptedScene,
            replaceAuthoredNodes: false);
        var loadedTrain = scriptedResult.Nodes[0].GetComponents<TrainCarComponent>().FirstOrDefault();
        Check("script json applies directly to the train component",
            loadedTrain != null
            && loadedTrain.RouteKey == "C"
            && loadedTrain.CarIndex == 4
            && Math.Abs(loadedTrain.Speed - 8.75f) < 0.001f);

        Check("transient train state is not applied from json",
            loadedTrain?.Paused == true);

        var scriptedTrainStart = loadedTrain?.Owner.Position ?? Vector3.Zero;
        scriptedWorld.BeginPlay();
        Check("world play starts scripted components",
            scriptedWorld.IsPlaying && loadedTrain?.Paused == false);

        if (loadedTrain != null)
        {
            loadedTrain.Owner.Position = new Vector3(99.0f, 88.0f, 77.0f);
        }

        scriptedWorld.EndPlay();
        Check("world stop restores scripted component state",
            !scriptedWorld.IsPlaying
            && loadedTrain?.Paused == true
            && loadedTrain.Owner.Position == scriptedTrainStart);

        var visualCollisionWorld = new ManaWorld();
        var visualCollisionContext = new ManaContext(null!, null!, visualCollisionWorld);
        visualCollisionContext.Physics = new ManaPhysics(visualCollisionContext);
        var visualCollisionScene = new ManaScene(visualCollisionContext) { Name = "Visual Collision Scene" };
        visualCollisionWorld.Scenes.Add(visualCollisionScene);

        var visualCollisionLevel = new ManaLevel
        {
            Nodes =
            {
                new ManaLevelNode
                {
                    Name = "Visual Platform",
                    Kind = ManaLevelSourceKind.Shape,
                    Shape = "Cube",
                    Position = new[] { 0.0f, 0.0f, 0.0f },
                    Rotation = new[] { 0.0f, 0.0f, 0.0f },
                    Scale = new[] { 1.0f, 1.0f, 1.0f },
                    Color = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                    Size = new[] { 2.0f, 0.25f, 3.0f },
                    Collision = new ManaLevelCollision
                    {
                        UseVisualBounds = true
                    }
                }
            }
        };

        var visualCollisionResult = ManaLevelLoader.Load(
            visualCollisionLevel,
            visualCollisionContext,
            visualCollisionScene,
            replaceAuthoredNodes: false);
        var visualCollision = visualCollisionResult.Nodes[0]
            .GetComponents<ManaCollisionComponent>()
            .FirstOrDefault();
        var visualBox = visualCollision?.Shape is Box box ? box : (Box?)null;
        var visualSize = visualBox.HasValue
            ? new Vector3(visualBox.Value.Width, visualBox.Value.Height, visualBox.Value.Length)
            : Vector3.Zero;

        Check("visual-bounds collision loads from the rendered cube bounds",
            NearlyEqual(visualSize, new Vector3(2.0f, 0.25f, 3.0f))
            && NearlyEqual(visualCollision?.Position ?? new Vector3(float.NaN), Vector3.Zero),
            visualBox == null
                ? "missing"
                : $"{visualBox.Value.Width}, {visualBox.Value.Height}, {visualBox.Value.Length}; offset {visualCollision?.Position}");

        var capturedVisualCollision = ManaLevelCapture.CaptureScene(
            visualCollisionWorld,
            visualCollisionScene,
            "Visual Collision Scene").Nodes[0].Collision;
        Check("visual-bounds collision saves as mesh-derived collision",
            capturedVisualCollision is { UseVisualBounds: true, Size: null },
            capturedVisualCollision == null
                ? "missing"
                : $"useVisualBounds={capturedVisualCollision.UseVisualBounds}, size={capturedVisualCollision.Size?.Length}");

        var wholeWorld = ManaLevelCapture.Capture(world, "World");
        Check("capturing the world still walks every scene",
            wholeWorld.Nodes.Count == 2,
            string.Join(", ", wholeWorld.Nodes.Select(node => node.Name)));

        // ---------------------------------------------------------------- format guards
        Check("a foreign json is rejected by name, not by null reference",
            Throws(() => ManaLevel.FromJson("{\"format\":\"something-else\",\"version\":1}")));

        Check("a level from a newer build is rejected rather than half-read",
            Throws(() => ManaLevel.FromJson(
                $"{{\"format\":\"{ManaLevel.FormatName}\",\"version\":{ManaLevel.CurrentVersion + 1}}}")));

        // ---------------------------------------------------------------- voxel codec
        // Half-unit positions on purpose: that is the grid resolution ManaVoxelShape keys voxels at, so
        // this is the finest distinction the codec is required to preserve.
        var voxels = new List<ManaVoxelData>
        {
            new() { Position = new Vector3(0.5f, 0.5f, 0.5f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) },
            new() { Position = new Vector3(-1.5f, 2.5f, 3.5f), Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f) },
            new() { Position = new Vector3(10.0f, -4.0f, 0.0f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) }
        };

        var encoded = ManaLevelVoxelCodec.Encode(voxels);
        var decoded = ManaLevelVoxelCodec.Decode(encoded);

        Check("every voxel comes back",
            decoded.Count == voxels.Count, $"{decoded.Count} of {voxels.Count}");

        Check("voxel positions come back exactly, half-units included",
            decoded[0].Position == voxels[0].Position
            && decoded[1].Position == voxels[1].Position
            && decoded[2].Position == voxels[2].Position);

        Check("voxel colours come back exactly",
            decoded[0].Color == voxels[0].Color && decoded[1].Color == voxels[1].Color);

        // Two of the three voxels are red, so a palette that did its job holds two entries, not three.
        Check("repeated colours share one palette entry",
            encoded.Palette.Count == 2, $"{encoded.Palette.Count} entries");

        var voxelLevel = new ManaLevel
        {
            Nodes = { new ManaLevelNode { Kind = ManaLevelSourceKind.Voxels, Voxels = encoded } }
        };

        var voxelsAfterJson = ManaLevelVoxelCodec.Decode(
            ManaLevel.FromJson(voxelLevel.ToJson()).Nodes[0].Voxels!);

        Check("voxels survive the json trip as well as the blob",
            voxelsAfterJson.Count == voxels.Count
            && voxelsAfterJson[1].Position == voxels[1].Position
            && voxelsAfterJson[1].Color == voxels[1].Color);

        // A blob whose length disagrees with the declared count means one of the two was written by
        // something that is not this codec, and reading it would produce silently wrong geometry.
        Check("a truncated voxel blob is caught rather than decoded short",
            Throws(() => ManaLevelVoxelCodec.Decode(new ManaLevelVoxels
            {
                Palette = encoded.Palette,
                Count = 99,
                Data = encoded.Data
            })));

        Check("a voxel pointing past the palette is caught",
            Throws(() => ManaLevelVoxelCodec.Decode(new ManaLevelVoxels
            {
                Palette = new List<float[]>(),
                Count = voxels.Count,
                Data = encoded.Data
            })));

        Check("an empty grid encodes and decodes as empty",
            ManaLevelVoxelCodec.Decode(ManaLevelVoxelCodec.Encode(Array.Empty<ManaVoxelData>())).Count == 0);

        // ---------------------------------------------------------------- file i/o
        var directory = Path.Combine(Path.GetTempPath(), "ManaTests", Guid.NewGuid().ToString("n"));
        try
        {
            var path = Path.Combine(directory, "sample" + ManaLevel.FileExtension);
            level.Save(path);

            Check("saving creates the folder it was pointed at", File.Exists(path));

            var fromDisk = ManaLevel.Load(path);
            Check("a level reads back off disk", fromDisk.Nodes.Count == level.Nodes.Count);

            // Saving over an existing level must replace it, and must not leave the temp file behind.
            level.Nodes.RemoveAt(1);
            level.Save(path);

            Check("saving again replaces the file rather than failing",
                ManaLevel.Load(path).Nodes.Count == 1);

            Check("the write-then-move leaves no temp file behind", !File.Exists(path + ".tmp"));

            Check("a name typed into the panel resolves to a level file",
                ManaLevelLibrary.ResolvePath("My Level")
                    .EndsWith("My Level" + ManaLevel.FileExtension, StringComparison.Ordinal));

            Check("a full path is left as a full path",
                ManaLevelLibrary.ResolvePath(path) == path);

            Check("invalid filename characters are stripped rather than throwing",
                !Throws(() => ManaLevelLibrary.ResolvePath("bad:name?")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // ---------------------------------------------------------------- command line
        // The launch flags are the contract between the editor's Play In New Window and the second process
        // it starts. A typo here is invisible: the level argument is simply never seen and the game opens
        // an empty world, which looks exactly like a level that failed to save.
        var playArgs = ManaLaunchOptions.Parse(new[] { "--play", "C:/levels/My Level.manalevel" });
        Check("--play carries the level path",
            playArgs.PlayLevelPath == "C:/levels/My Level.manalevel", playArgs.PlayLevelPath ?? "null");

        var editArgs = ManaLaunchOptions.Parse(new[] { "--level", "sample.manalevel" });
        Check("--level opens the editor on a level, not the game",
            editArgs.EditLevelPath == "sample.manalevel" && editArgs.PlayLevelPath == null);

        foreach (var removedBackend in new[] { "opengl", "gl" })
        {
            var rejected = false;
            try { ManaLaunchOptions.Parse(new[] { "--graphics", removedBackend }); }
            catch (ArgumentException ex) { rejected = ex.Message.Contains("removed"); }
            Check($"removed backend {removedBackend} reports a useful error", rejected);
        }

        var combined = ManaLaunchOptions.Parse(
            new[] { "--play", "a.manalevel", "--graphics", "webgpu", "--wgpu-backend", "vulkan" });
        Check("play survives alongside the graphics flags it is launched with",
            combined.PlayLevelPath == "a.manalevel"
            && combined.GraphicsBackend == ManaGraphicsBackendKind.WebGpu
            && combined.WebGpuBackend == "vulkan",
            $"{combined.PlayLevelPath}, {combined.GraphicsBackend}, {combined.WebGpuBackend}");

        Check("=-form works too, as it does for every other flag",
            ManaLaunchOptions.Parse(new[] { "--play=b.manalevel" }).PlayLevelPath == "b.manalevel");

        Check("no level flag leaves both null",
            ManaLaunchOptions.Parse(Array.Empty<string>()) is { PlayLevelPath: null, EditLevelPath: null });

        // ---------------------------------------------------------------- level source tagging
        Check("a shape source records the visual factory size, not the node scale",
            ManaLevelSource.ForShape("Cube", Vector4.One, new Vector3(2.0f, 1.0f, 2.0f)).Size.X == 2.0f);

        Check("a mesh source carries its asset reference",
            ManaLevelSource.ForMesh("pack.zip|models/tree.glb").Asset == "pack.zip|models/tree.glb");

        var relativeZipAsset = MeshAssetLibrary.EncodeRelativeZip("Assets/pack.zip", "Models/train.glb");
        Check("a relative zip asset keeps its zip filename and entry",
            ZipAssetReferences.TryDecodeZip(relativeZipAsset, out var decodedZip, out var decodedEntry)
            && Path.GetFileName(decodedZip).Equals("pack.zip", StringComparison.OrdinalIgnoreCase)
            && decodedEntry == "Models/train.glb",
            $"{decodedZip} | {decodedEntry}");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool NearlyEqual(Vector3 left, Vector3 right, float epsilon = 0.001f)
    {
        return MathF.Abs(left.X - right.X) <= epsilon
            && MathF.Abs(left.Y - right.Y) <= epsilon
            && MathF.Abs(left.Z - right.Z) <= epsilon;
    }

    private static JsonElement JsonValue<T>(T value)
    {
        using var document = JsonSerializer.SerializeToDocument(value);
        return document.RootElement.Clone();
    }
}
